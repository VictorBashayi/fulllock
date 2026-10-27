using F1Controller.Engineer;

namespace F1Controller.Tests;

public class EngineerRulesFlagsTests
{
    private static readonly EngineerThresholds T = new();

    /// <summary>Calls produced by <paramref name="cur"/> after <paramref name="prev"/>, on a fresh rules instance.</summary>
    private static IReadOnlyList<RadioCall> Step(RaceSnapshot prev, RaceSnapshot cur)
    {
        var rules = new EngineerRules(T);
        rules.Evaluate(null, prev);
        return rules.Evaluate(prev, cur);
    }

    [Fact]
    public void SafetyCarTransitions()
    {
        var deployed = Assert.Single(Step(Snapshots.Make(sc: 0), Snapshots.Make(sc: 1)));
        Assert.Equal("sc-deployed", deployed.Key);
        Assert.Equal("Safety car deployed.", deployed.Text);
        Assert.Equal(RadioPriority.Critical, deployed.Priority);
        Assert.Equal(RadioCategory.Flags, deployed.Category);

        var vsc = Assert.Single(Step(Snapshots.Make(sc: 0), Snapshots.Make(sc: 2)));
        Assert.Equal("Virtual safety car.", vsc.Text);

        var cold = new byte[] { 60, 60, 60, 60 };  // tyres cool under SC; keep that call out of this test
        var inCall = Assert.Single(Step(Snapshots.Make(sc: 1, lap: 1, temps: cold), Snapshots.Make(sc: 0, lap: 1, temps: cold)));
        Assert.Equal("sc-in", inCall.Key);
        Assert.Equal("Safety car is in, get ready.", inCall.Text);
        Assert.Equal(RadioPriority.Critical, inCall.Priority);
    }

    [Fact]
    public void FlagChanges()
    {
        var yellow = Assert.Single(Step(Snapshots.Make(fiaFlag: 0), Snapshots.Make(fiaFlag: 3)));
        Assert.Equal("flag-yellow", yellow.Key);
        Assert.Equal("Yellow flag, careful.", yellow.Text);
        Assert.Equal(RadioPriority.Important, yellow.Priority);

        var red = Assert.Single(Step(Snapshots.Make(fiaFlag: 0), Snapshots.Make(fiaFlag: 4)));
        Assert.Equal("Red flag.", red.Text);
        Assert.Equal(RadioPriority.Critical, red.Priority);

        var blue = Assert.Single(Step(Snapshots.Make(fiaFlag: 0), Snapshots.Make(fiaFlag: 2)));
        Assert.Equal("flag-blue", blue.Key);
        Assert.Equal("Blue flags, let them by.", blue.Text);

        Assert.Empty(Step(Snapshots.Make(fiaFlag: 3), Snapshots.Make(fiaFlag: 3)));
    }

    [Fact]
    public void RainForecastAndRain()
    {
        var forecast = Assert.Single(Step(Snapshots.Make(weather: 1), Snapshots.Make(weather: 1, rainIn: 8)));
        Assert.Equal("rain-forecast", forecast.Key);
        Assert.Equal("Rain expected in 8 minutes.", forecast.Text);
        Assert.Equal(RadioPriority.Important, forecast.Priority);
        Assert.Equal(RadioCategory.Weather, forecast.Category);

        Assert.Empty(Step(Snapshots.Make(weather: 1), Snapshots.Make(weather: 1, rainIn: 20)));

        var rain = Assert.Single(Step(Snapshots.Make(weather: 2), Snapshots.Make(weather: 3)));
        Assert.Equal("rain-now", rain.Key);
        Assert.Equal("It's raining.", rain.Text);
    }

    [Fact]
    public void DamageStepsOfTen()
    {
        var rules = new EngineerRules(T);
        var s0 = Snapshots.Make(t: 1, damage: new Damage(FrontWing: 0, 0, 0, 0, 0));
        var s1 = Snapshots.Make(t: 2, damage: new Damage(FrontWing: 5, 0, 0, 0, 0));
        var s2 = Snapshots.Make(t: 3, damage: new Damage(FrontWing: 12, 0, 0, 0, 0));
        var s3 = Snapshots.Make(t: 4, damage: new Damage(FrontWing: 18, 0, 0, 0, 0));
        var s4 = Snapshots.Make(t: 5, damage: new Damage(FrontWing: 22, 0, 0, 0, 0));
        rules.Evaluate(null, s0);
        Assert.Empty(rules.Evaluate(s0, s1));
        var first = Assert.Single(rules.Evaluate(s1, s2));
        Assert.Equal("damage-front-wing", first.Key);
        Assert.Equal("Front wing damage, 12 percent.", first.Text);
        Assert.Equal(RadioPriority.Critical, first.Priority);
        Assert.Equal(RadioCategory.Damage, first.Category);
        Assert.Empty(rules.Evaluate(s2, s3));
        Assert.Equal("Front wing damage, 22 percent.", Assert.Single(rules.Evaluate(s3, s4)).Text);
    }

    [Fact]
    public void DamageNamesEachPart()
    {
        var calls = Step(Snapshots.Make(), Snapshots.Make(damage: new Damage(0, RearWing: 15, Floor: 20, Diffuser: 30, Sidepod: 40)));
        Assert.Equal(
            new[] { "Rear wing damage, 15 percent.", "Floor damage, 20 percent.", "Diffuser damage, 30 percent.", "Sidepod damage, 40 percent." },
            calls.Select(c => c.Text));
    }

    [Fact]
    public void DamageBaselineFollowsRepair()
    {
        var rules = new EngineerRules(T);
        var s0 = Snapshots.Make(t: 1, damage: new Damage(30, 0, 0, 0, 0));
        var s1 = Snapshots.Make(t: 2, damage: new Damage(0, 0, 0, 0, 0));
        var s2 = Snapshots.Make(t: 3, damage: new Damage(10, 0, 0, 0, 0));
        rules.Evaluate(null, s0);
        Assert.Empty(rules.Evaluate(s0, s1));
        Assert.Equal("Front wing damage, 10 percent.", Assert.Single(rules.Evaluate(s1, s2)).Text);
    }

    [Fact]
    public void EventsBecomeCalls()
    {
        var drs = Assert.Single(Step(Snapshots.Make(), Snapshots.Make(events: new[] { new GameEvent(GameEventKind.DrsEnabled) })));
        Assert.Equal("drs", drs.Key);
        Assert.Equal("DRS enabled.", drs.Text);
        Assert.Equal(RadioPriority.Info, drs.Priority);
        Assert.Equal(RadioCategory.Other, drs.Category);

        string Penalty(byte type, byte seconds) =>
            Assert.Single(Step(Snapshots.Make(), Snapshots.Make(events: new[] { new GameEvent(GameEventKind.Penalty, type, seconds) }))).Text;
        Assert.Equal("5 second penalty.", Penalty(4, 5));
        Assert.Equal("Stop-go penalty, 10 seconds.", Penalty(1, 10));
        Assert.Equal("Drive through penalty.", Penalty(0, 0));
        Assert.Equal("We've been disqualified.", Penalty(6, 0));
    }

    [Fact]
    public void NothingFiresWithoutPrevious()
    {
        var rules = new EngineerRules(T);
        Assert.Empty(rules.Evaluate(null, Snapshots.Make(sc: 1, fiaFlag: 4, weather: 4, damage: new Damage(50, 0, 0, 0, 0))));
    }
}
