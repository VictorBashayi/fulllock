using F1Controller.Engineer;
using F1Controller.Validation;

namespace F1Controller.Tests;

public class EngineerRulesGapsLapTests
{
    private static readonly EngineerThresholds T = new();

    /// <summary>Feeds snapshots in order and returns the calls produced by the last one.</summary>
    private static IReadOnlyList<RadioCall> Last(EngineerRules rules, params RaceSnapshot[] snaps)
    {
        IReadOnlyList<RadioCall> calls = Array.Empty<RadioCall>();
        RaceSnapshot? prev = null;
        foreach (var s in snaps)
        {
            calls = rules.Evaluate(prev, s);
            prev = s;
        }
        return calls;
    }

    private static RadioCall? Find(IReadOnlyList<RadioCall> calls, string key) =>
        calls.FirstOrDefault(c => c.Key == key);

    [Fact]
    public void CatchingNeedsTwoLapsOfShrinkingGap()
    {
        var call = Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapAhead: 2400),
            Snapshots.Make(lap: 6, gapAhead: 2100),
            Snapshots.Make(lap: 7, gapAhead: 1800)), "gap-ahead");
        Assert.NotNull(call);
        Assert.Equal("You're catching the car ahead, 0.3 a lap. Gap 1.8.", call!.Text);
        Assert.Equal(RadioPriority.Info, call.Priority);
        Assert.Equal(RadioCategory.Gaps, call.Category);

        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapAhead: 2400),
            Snapshots.Make(lap: 6, gapAhead: 2100)), "gap-ahead"));
    }

    [Fact]
    public void CatchingIgnoredWhenGapTooBig()
    {
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapAhead: 4000),
            Snapshots.Make(lap: 6, gapAhead: 3700),
            Snapshots.Make(lap: 7, gapAhead: 3400)), "gap-ahead"));
    }

    [Fact]
    public void CatchingIgnoresNoiseSizedShrinks()
    {
        // Equal pace: the gap wobbles down by a few hundredths, which isn't catching anyone
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapAhead: 2400),
            Snapshots.Make(lap: 6, gapAhead: 2370),
            Snapshots.Make(lap: 7, gapAhead: 2340)), "gap-ahead"));
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapBehind: 1500),
            Snapshots.Make(lap: 6, gapBehind: 1300),
            Snapshots.Make(lap: 7, gapBehind: 1260)), "gap-behind"));
    }

    [Fact]
    public void GapSampledOnlyOnLapChange()
    {
        // Mid-lap gap changes are not samples: only the value at each new lap counts
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapAhead: 2400),
            Snapshots.Make(lap: 5, gapAhead: 2100),
            Snapshots.Make(lap: 5, gapAhead: 1800)), "gap-ahead"));
    }

    [Fact]
    public void PositionChangeResetsGapHistory()
    {
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, position: 4, gapAhead: 2400),
            Snapshots.Make(lap: 6, position: 4, gapAhead: 2100),
            Snapshots.Make(lap: 7, position: 3, gapAhead: 1800)), "gap-ahead"));
    }

    [Fact]
    public void ClosingBehind()
    {
        var call = Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, gapBehind: 1900),
            Snapshots.Make(lap: 6, gapBehind: 1500),
            Snapshots.Make(lap: 7, gapBehind: 1100)), "gap-behind");
        Assert.NotNull(call);
        Assert.Equal("Car behind is closing, 0.4 a lap.", call!.Text);
        Assert.Equal(RadioCategory.Gaps, call.Category);
    }

    [Fact]
    public void GapsOnlyInRace()
    {
        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 5, isRace: false, gapAhead: 2400),
            Snapshots.Make(lap: 6, isRace: false, gapAhead: 2100),
            Snapshots.Make(lap: 7, isRace: false, gapAhead: 1800)), "gap-ahead"));
    }

    [Fact]
    public void LapInvalidOncePerLap()
    {
        var rules = new EngineerRules(T);
        var a = Snapshots.Make(t: 1, lapInvalid: false);
        var b = Snapshots.Make(t: 2, lapInvalid: true);
        var c = Snapshots.Make(t: 3, lapInvalid: true);
        Assert.Empty(rules.Evaluate(null, a));
        var call = Assert.Single(rules.Evaluate(a, b));
        Assert.Equal("lap-invalid", call.Key);
        Assert.Equal("Lap deleted, track limits.", call.Text);
        Assert.Equal(RadioPriority.Important, call.Priority);
        Assert.Equal(RadioCategory.Lap, call.Category);
        Assert.Empty(rules.Evaluate(b, c));
    }

    [Fact]
    public void PersonalBestSkipsFirstLap()
    {
        var rules = new EngineerRules(T);
        var a = Snapshots.Make(t: 1, bestLapMs: 0);
        var b = Snapshots.Make(t: 2, bestLapMs: 91500);
        var c = Snapshots.Make(t: 3, bestLapMs: 91204);
        Assert.Empty(rules.Evaluate(a, b));
        var call = Assert.Single(rules.Evaluate(b, c));
        Assert.Equal("pb", call.Key);
        Assert.Equal("Personal best, 1:31.204.", call.Text);
        Assert.Equal(RadioPriority.Info, call.Priority);
    }

    [Fact]
    public void PurpleAndGreenSectors()
    {
        var rules = new EngineerRules(T);
        var a = Snapshots.Make(t: 1);
        var b = Snapshots.Make(t: 2, s1: RaceTracker.SectorStatus.OverallBest);
        var c = Snapshots.Make(t: 3, s1: RaceTracker.SectorStatus.OverallBest, s2: RaceTracker.SectorStatus.PersonalBest);

        var purple = Assert.Single(rules.Evaluate(a, b));
        Assert.Equal("sector-1", purple.Key);
        Assert.Equal("Purple sector.", purple.Text);
        Assert.Equal(RadioPriority.Chatter, purple.Priority);

        var green = Assert.Single(rules.Evaluate(b, c));
        Assert.Equal("sector-2", green.Key);
        Assert.Equal("Green sector.", green.Text);
    }

    [Fact]
    public void LastLapInRaceOnly()
    {
        var call = Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 27, totalLaps: 28),
            Snapshots.Make(lap: 28, totalLaps: 28)), "last-lap");
        Assert.NotNull(call);
        Assert.Equal("Last lap, bring it home.", call!.Text);
        Assert.Equal(RadioPriority.Info, call.Priority);

        Assert.Null(Find(Last(new EngineerRules(T),
            Snapshots.Make(lap: 27, totalLaps: 28, isRace: false),
            Snapshots.Make(lap: 28, totalLaps: 28, isRace: false)), "last-lap"));
    }
}
