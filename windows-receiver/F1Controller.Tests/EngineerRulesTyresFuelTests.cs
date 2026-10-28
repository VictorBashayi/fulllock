using F1Controller.Engineer;

namespace F1Controller.Tests;

public class EngineerRulesTyresFuelTests
{
    private static readonly EngineerThresholds T = new();

    private static byte[] Temps(byte fl = 90, byte fr = 90, byte rl = 90, byte rr = 90) => new[] { fl, fr, rl, rr };
    private static byte[] Wear(byte fl = 10, byte fr = 10, byte rl = 10, byte rr = 10) => new[] { fl, fr, rl, rr };

    /// <summary>Feeds snapshots in order and returns the calls produced by each step.</summary>
    private static List<IReadOnlyList<RadioCall>> Run(EngineerRules rules, params RaceSnapshot[] snaps)
    {
        var results = new List<IReadOnlyList<RadioCall>>();
        RaceSnapshot? prev = null;
        foreach (var s in snaps)
        {
            results.Add(rules.Evaluate(prev, s));
            prev = s;
        }
        return results;
    }

    private static int Count(List<IReadOnlyList<RadioCall>> results, string key) =>
        results.Sum(r => r.Count(c => c.Key == key));

    [Fact]
    public void HotTyreNeedsFiveSeconds()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, temps: Temps(fl: 112)),
            Snapshots.Make(t: 104, temps: Temps(fl: 112)),
            Snapshots.Make(t: 105.1f, temps: Temps(fl: 112)));
        Assert.Empty(r[0]);
        Assert.Empty(r[1]);
        var call = Assert.Single(r[2]);
        Assert.Equal("tyre-hot-FL", call.Key);
        Assert.Equal("Front left is overheating, manage it.", call.Text);
        Assert.Equal(RadioPriority.Important, call.Priority);
        Assert.Equal(RadioCategory.Tyres, call.Category);
    }

    [Fact]
    public void HotTyreFiresOncePerSpell()
    {
        var rules = new EngineerRules(T);
        var snaps = new List<RaceSnapshot>();
        for (float t = 100; t <= 120; t += 1) snaps.Add(Snapshots.Make(t: t, temps: Temps(fl: 112)));
        Assert.Equal(1, Count(Run(rules, snaps.ToArray()), "tyre-hot-FL"));

        // Cools down, then a second spell.
        var second = new[]
        {
            Snapshots.Make(t: 121, temps: Temps(fl: 100)),
            Snapshots.Make(t: 122, temps: Temps(fl: 112)),
            Snapshots.Make(t: 125, temps: Temps(fl: 112)),
            Snapshots.Make(t: 127.2f, temps: Temps(fl: 112)),
        };
        var results = new List<IReadOnlyList<RadioCall>>();
        RaceSnapshot prev = snaps[^1];
        foreach (var s in second) { results.Add(rules.Evaluate(prev, s)); prev = s; }
        Assert.Equal(1, Count(results, "tyre-hot-FL"));
        Assert.Contains(results[^1], c => c.Key == "tyre-hot-FL");
    }

    [Theory]
    [InlineData(0, 0, 115, 115, "tyre-hot-rears", "Rears are overheating, manage them.")]
    [InlineData(115, 115, 0, 0, "tyre-hot-fronts", "Fronts are overheating, manage them.")]
    [InlineData(115, 115, 115, 115, "tyre-hot-all", "All four tyres are overheating, manage them.")]
    [InlineData(0, 115, 115, 115, "tyre-hot-multi", "Front right, rear left and rear right are overheating, manage them.")]
    public void TyresOverheatingTogetherAreOneCall(byte fl, byte fr, byte rl, byte rr, string key, string text)
    {
        // 0 = not hot
        byte[] temps = Temps(fl == 0 ? (byte)90 : fl, fr == 0 ? (byte)90 : fr, rl == 0 ? (byte)90 : rl, rr == 0 ? (byte)90 : rr);
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, temps: temps),
            Snapshots.Make(t: 106, temps: temps));
        var call = Assert.Single(r[1]);
        Assert.Equal(key, call.Key);
        Assert.Equal(text, call.Text);
        Assert.Equal(RadioPriority.Important, call.Priority);
    }

    [Fact]
    public void TyreHotLaterJoinsTheFirstCallAndDoesNotRepeat()
    {
        // Burnout: rear left over the limit first, rear right two seconds later
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, temps: Temps(rl: 115)),
            Snapshots.Make(t: 102, temps: Temps(rl: 115, rr: 115)),
            Snapshots.Make(t: 105, temps: Temps(rl: 115, rr: 115)),
            Snapshots.Make(t: 108, temps: Temps(rl: 115, rr: 115)));
        Assert.Equal("tyre-hot-rears", Assert.Single(r[2]).Key);
        Assert.Empty(r[3]);
    }

    [Fact]
    public void ColdTyresIgnoredOnLapOneAndUnderSafetyCar()
    {
        var rules = new EngineerRules(T);
        var cold = Temps(60, 60, 60, 60);
        var r = Run(rules,
            Snapshots.Make(t: 100, lap: 1, temps: cold),
            Snapshots.Make(t: 101, lap: 3, temps: cold, sc: 1),
            Snapshots.Make(t: 111, lap: 3, temps: cold, sc: 1),
            Snapshots.Make(t: 112, lap: 3, temps: cold, sc: 0),
            Snapshots.Make(t: 122, lap: 3, temps: cold, sc: 0));
        // The safety car itself is called too; only the cold-tyre call matters here
        Assert.Equal(0, Count(r.Take(4).ToList(), "tyre-cold"));
        var call = Assert.Single(r[4], c => c.Key == "tyre-cold");
        Assert.Equal("Tyres are cold, push to get heat in.", call.Text);
        Assert.Equal(RadioPriority.Important, call.Priority);
    }

    [Fact]
    public void ColdTyresNeedTenSeconds()
    {
        // Slow stretch after a push lap: brief dip below the limit, then warm again
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, temps: Temps(60, 60, 60, 60)),
            Snapshots.Make(t: 109, temps: Temps(60, 60, 60, 60)),
            Snapshots.Make(t: 110, temps: Temps(80, 80, 80, 80)),
            Snapshots.Make(t: 111, temps: Temps(60, 60, 60, 60)),
            Snapshots.Make(t: 120, temps: Temps(60, 60, 60, 60)),
            Snapshots.Make(t: 121, temps: Temps(60, 60, 60, 60)));
        Assert.Equal(0, Count(r.Take(5).ToList(), "tyre-cold"));
        Assert.Single(r[5], c => c.Key == "tyre-cold");
    }

    [Fact]
    public void WearCallsOncePerThreshold()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, wear: Wear(rl: 49)),
            Snapshots.Make(t: 101, wear: Wear(rl: 51)),
            Snapshots.Make(t: 102, wear: Wear(rl: 52)),
            Snapshots.Make(t: 103, wear: Wear(rl: 69)),
            Snapshots.Make(t: 104, wear: Wear(rl: 71)));
        Assert.Empty(r[0]);
        var warn = Assert.Single(r[1]);
        Assert.Equal("tyre-wear-50", warn.Key);
        Assert.Equal("Tyre wear at 50 percent.", warn.Text);
        Assert.Equal(RadioPriority.Info, warn.Priority);
        Assert.Empty(r[2]);
        Assert.Empty(r[3]);
        var box = Assert.Single(r[4]);
        Assert.Equal("tyre-wear-70", box.Key);
        Assert.Equal("Rears at 70 percent, box soon.", box.Text);
        Assert.Equal(RadioPriority.Important, box.Priority);
    }

    [Fact]
    public void WearBoxCallNamesFrontsWhenFrontIsWorst()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, wear: Wear(fr: 69)),
            Snapshots.Make(t: 101, wear: Wear(fr: 71)));
        Assert.Equal("Fronts at 70 percent, box soon.", Assert.Single(r[1], c => c.Key == "tyre-wear-70").Text);
    }

    [Fact]
    public void WearRearmsAfterNewTyres()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, wear: Wear(rl: 49)),
            Snapshots.Make(t: 101, wear: Wear(rl: 71)),
            Snapshots.Make(t: 102, wear: Wear(rl: 5, rr: 5, fl: 5, fr: 5)),
            Snapshots.Make(t: 103, wear: Wear(rl: 49)),
            Snapshots.Make(t: 104, wear: Wear(rl: 51)));
        Assert.Contains(r[4], c => c.Key == "tyre-wear-50");
        Assert.Equal(2, Count(r, "tyre-wear-50"));
    }

    [Fact]
    public void FuelCallsOnCrossingOnlyInRace()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, fuel: 0.0f),
            Snapshots.Make(t: 101, fuel: -0.4f),
            Snapshots.Make(t: 102, fuel: -0.5f),
            Snapshots.Make(t: 103, fuel: -0.9f),
            Snapshots.Make(t: 104, fuel: -1.1f));
        Assert.Empty(r[0]);
        var tight = Assert.Single(r[1]);
        Assert.Equal("fuel-tight", tight.Key);
        Assert.Equal("Fuel's tight, start lifting and coasting.", tight.Text);
        Assert.Equal(RadioPriority.Important, tight.Priority);
        Assert.Equal(RadioCategory.Fuel, tight.Category);
        Assert.Empty(r[2]);
        Assert.Empty(r[3]);
        var crit = Assert.Single(r[4]);
        Assert.Equal("fuel-critical", crit.Key);
        Assert.Equal("We won't make it on fuel, lift and coast now.", crit.Text);
        Assert.Equal(RadioPriority.Critical, crit.Priority);

        var other = new EngineerRules(T);
        var none = Run(other,
            Snapshots.Make(t: 100, fuel: 0.0f, isRace: false),
            Snapshots.Make(t: 101, fuel: -1.5f, isRace: false));
        Assert.All(none, Assert.Empty);
    }

    [Fact]
    public void FuelRearmsAboveZero()
    {
        var rules = new EngineerRules(T);
        var r = Run(rules,
            Snapshots.Make(t: 100, fuel: 0.1f),
            Snapshots.Make(t: 101, fuel: -0.4f),
            Snapshots.Make(t: 102, fuel: 0.5f),
            Snapshots.Make(t: 103, fuel: -0.4f));
        Assert.Equal(2, Count(r, "fuel-tight"));
    }

    [Fact]
    public void FirstSnapshotFiresNoCrossings()
    {
        var rules = new EngineerRules(T);
        var calls = rules.Evaluate(null, Snapshots.Make(fuel: -2f, wear: Wear(rl: 80)));
        Assert.Empty(calls);
    }

    [Fact]
    public void ResetClearsMemory()
    {
        var rules = new EngineerRules(T);
        Run(rules,
            Snapshots.Make(t: 100, temps: Temps(fl: 112)),
            Snapshots.Make(t: 106, temps: Temps(fl: 112)));
        rules.Reset();
        var r = Run(rules,
            Snapshots.Make(t: 200, temps: Temps(fl: 112)),
            Snapshots.Make(t: 206, temps: Temps(fl: 112)));
        Assert.Contains(r[1], c => c.Key == "tyre-hot-FL");
    }

    [Theory]
    [InlineData("gap-ahead-catch", 240f)]
    [InlineData("lap-report", 0f)]
    [InlineData("flag-blue", 30f)]
    [InlineData("tyre-cold", 120f)]
    [InlineData("rain-forecast", 600f)]
    [InlineData("sc-deployed", 0f)]
    [InlineData("flag-red", 0f)]
    [InlineData("flag-yellow", 60f)]
    [InlineData("damage-front-wing", 0f)]
    [InlineData("penalty", 0f)]
    [InlineData("drs", 0f)]
    [InlineData("pb", 0f)]
    [InlineData("last-lap", 0f)]
    [InlineData("lap-invalid", 0f)]
    [InlineData("tyre-hot-FL", 60f)]
    [InlineData("tyre-hot-rears", 60f)]
    [InlineData("fuel-tight", 60f)]
    [InlineData("something-else", 60f)]
    public void CooldownForMapsKeys(string key, float expected) =>
        Assert.Equal(expected, T.CooldownFor(key));
}
