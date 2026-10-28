using F1Controller.Engineer;

namespace F1Controller.Tests;

public class EngineerRulesLapReportTests
{
    private static readonly EngineerThresholds T = new();

    /// <summary>
    /// One completed lap: a snapshot near the end of <paramref name="lap"/> (its sector times and
    /// validity) followed by the first snapshot of the next lap (the lap time).
    /// </summary>
    private sealed record Lap(
        byte Number, uint TimeMs, ushort S1 = 25000, ushort S2 = 30000, bool Invalid = false,
        byte PitMid = 0, byte PitAtStart = 0, byte PitAfter = 0,
        uint BestBefore = 0, uint BestAfter = 0,
        int? GapAhead = null, int? GapBehind = null, bool IsRace = true);

    /// <summary>Feeds the laps in order and returns the calls made at each lap's rollover.</summary>
    private static List<IReadOnlyList<RadioCall>> Run(int level, params Lap[] laps)
    {
        var rules = new EngineerRules(T);
        var results = new List<IReadOnlyList<RadioCall>>();
        RaceSnapshot? prev = null;
        float t = 100;
        foreach (var l in laps)
        {
            var start = Snapshots.Make(t: t++, lap: l.Number, pitStatus: l.PitAtStart, bestLapMs: l.BestBefore, isRace: l.IsRace);
            var mid = Snapshots.Make(t: t++, lap: l.Number, pitStatus: l.PitMid, bestLapMs: l.BestBefore, isRace: l.IsRace);
            var end = Snapshots.Make(t: t++, lap: l.Number, s1Ms: l.S1, s2Ms: l.S2, lapInvalid: l.Invalid,
                bestLapMs: l.BestBefore, isRace: l.IsRace);
            var next = Snapshots.Make(t: t++, lap: (byte)(l.Number + 1), lastLapMs: l.TimeMs, pitStatus: l.PitAfter,
                bestLapMs: l.BestAfter, gapAhead: l.GapAhead, gapBehind: l.GapBehind, isRace: l.IsRace);
            foreach (var s in new[] { start, mid, end })
            {
                rules.Evaluate(prev, s, level);
                prev = s;
            }
            results.Add(rules.Evaluate(prev, next, level));
            prev = next;
        }
        return results;
    }

    private static RadioCall? Report(IReadOnlyList<RadioCall> calls) => calls.FirstOrDefault(c => c.Key == "lap-report");

    private static Lap[] Laps(int count) =>
        Enumerable.Range(1, count).Select(n => new Lap((byte)n, 80000)).ToArray();

    [Theory]
    [InlineData(1, new[] { 3, 6 })]
    [InlineData(2, new[] { 2, 4, 6 })]
    [InlineData(3, new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(0, new int[0])]
    public void ReportRhythmFollowsLevel(int level, int[] reportedLaps)
    {
        var results = Run(level, Laps(6));
        var reported = Enumerable.Range(1, 6).Where(n => Report(results[n - 1]) != null).ToArray();
        Assert.Equal(reportedLaps, reported);
    }

    [Fact]
    public void ReportComparesSectorsWithBestLap()
    {
        var results = Run(3,
            new Lap(1, 80000),
            new Lap(2, 79930, S1: 24800, S2: 30100, GapAhead: 1200, GapBehind: 800));
        var call = Report(results[1])!;
        Assert.Equal("Lap 2, 1:19.9. Sector 1 up 0.2, sector 2 down 0.1, sector 3 matched. Gap ahead 1.2, behind 0.8.", call.Text);
        Assert.Equal(RadioPriority.Important, call.Priority);
        Assert.Equal(RadioCategory.Lap, call.Category);
    }

    [Fact]
    public void FirstLapHasNoComparison()
    {
        Assert.Equal("Lap 1, 1:20.0.", Report(Run(3, new Lap(1, 80000))[0])!.Text);
    }

    [Fact]
    public void DeletedLap()
    {
        var results = Run(3,
            new Lap(1, 80000),
            new Lap(2, 70000, S1: 20000, S2: 25000, Invalid: true, GapAhead: 1200),
            new Lap(3, 79900, S1: 24900, S2: 30000));
        Assert.Equal("Lap 2 deleted. Gap ahead 1.2.", Report(results[1])!.Text);
        // The deleted lap never became the best: lap 3 compares with lap 1
        Assert.Equal("Lap 3, 1:19.9. Sector 1 up 0.1, sector 2 matched, sector 3 matched.", Report(results[2])!.Text);
    }

    [Fact]
    public void PbLapReportReplacesPbCall()
    {
        var results = Run(2,
            new Lap(1, 80000, BestAfter: 80000),
            new Lap(2, 79500, S1: 24500, BestBefore: 80000, BestAfter: 79500),
            new Lap(3, 79400, S1: 24400, BestBefore: 79500, BestAfter: 79400));
        // Lap 2 is due at Balanced: one report, no separate pb call
        Assert.Equal("Personal best. Lap 2, 1:19.5. Sector 1 up 0.5, sector 2 matched, sector 3 matched.",
            Report(results[1])!.Text);
        Assert.DoesNotContain(results[1], c => c.Key == "pb");
        // Lap 3 isn't due: the pb call still comes
        Assert.Null(Report(results[2]));
        Assert.Contains(results[2], c => c.Key == "pb");
    }

    [Fact]
    public void InAndOutLaps()
    {
        var results = Run(3,
            new Lap(1, 80000),
            new Lap(2, 95000, PitMid: 1, PitAfter: 2, GapBehind: 3000),
            new Lap(3, 75000, S1: 20000, S2: 25000, PitAtStart: 2),
            new Lap(4, 79900, S1: 24900));
        Assert.Equal("In lap. Gap behind 3.0.", Report(results[1])!.Text);
        Assert.Equal("Out lap.", Report(results[2])!.Text);
        // Neither became the best: lap 4 compares with lap 1
        Assert.StartsWith("Lap 4, 1:19.9. Sector 1 up 0.1,", Report(results[3])!.Text);
    }

    [Fact]
    public void NoGapsOutsideRace()
    {
        var results = Run(3, new Lap(1, 80000, GapAhead: 1200, GapBehind: 800, IsRace: false));
        Assert.Equal("Lap 1, 1:20.0.", Report(results[0])!.Text);
    }

    [Fact]
    public void NoReportUnlessLapAdvancesByOne()
    {
        var rules = new EngineerRules(T);
        var a = Snapshots.Make(t: 1, lap: 5, s1Ms: 25000, s2Ms: 30000);
        var back = Snapshots.Make(t: 2, lap: 4, lastLapMs: 80000);
        var jump = Snapshots.Make(t: 3, lap: 6, lastLapMs: 80000);
        rules.Evaluate(null, a, 3);
        Assert.Null(Report(rules.Evaluate(a, back, 3)));
        Assert.Null(Report(rules.Evaluate(back, jump, 3)));
    }

    [Fact]
    public void MissingSectorTimesSkipComparison()
    {
        var results = Run(3,
            new Lap(1, 80000),
            new Lap(2, 79000, S1: 0),
            new Lap(3, 79900, S1: 24900));
        Assert.Equal("Lap 2, 1:19.0.", Report(results[1])!.Text);
        // Lap 2 wasn't stored as best: lap 3 compares with lap 1
        Assert.StartsWith("Lap 3, 1:19.9. Sector 1 up 0.1,", Report(results[2])!.Text);
    }
}
