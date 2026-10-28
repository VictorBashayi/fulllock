using F1Controller.Engineer;

namespace F1Controller.Tests;

public class EngineerServiceTests
{
    private static (EngineerService service, List<RadioCall> sent) Make()
    {
        var sent = new List<RadioCall>();
        return (new EngineerService(new EngineerThresholds(), sent.Add), sent);
    }

    private static RaceSnapshot WithUid(RaceSnapshot s, ulong uid) => s with { SessionUid = uid };

    [Fact]
    public void SilentDuringWarmup()
    {
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 0, fiaFlag: 0));
        service.OnSnapshot(Snapshots.Make(t: 1, fiaFlag: 4));   // red flag during warm-up: discarded
        service.OnSnapshot(Snapshots.Make(t: 2, fiaFlag: 0));
        service.OnSnapshot(Snapshots.Make(t: 2.9f, fiaFlag: 0));
        Assert.Empty(sent);

        service.OnSnapshot(Snapshots.Make(t: 4, fiaFlag: 4));
        Assert.Equal("flag-red", Assert.Single(sent).Key);
    }

    [Fact]
    public void SessionRestartResets()
    {
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 90, wear: new byte[] { 49, 10, 10, 10 }));
        service.OnSnapshot(Snapshots.Make(t: 100, wear: new byte[] { 51, 10, 10, 10 }));
        Assert.Equal("tyre-wear-50", Assert.Single(sent).Key);

        // Restart: session time goes backwards, warm-up again, memory and cooldowns cleared
        service.OnSnapshot(Snapshots.Make(t: 5, wear: new byte[] { 5, 5, 5, 5 }));
        service.OnSnapshot(Snapshots.Make(t: 7, fiaFlag: 4, wear: new byte[] { 5, 5, 5, 5 }));
        Assert.Single(sent);  // red flag during warm-up is discarded
        service.OnSnapshot(Snapshots.Make(t: 9, wear: new byte[] { 49, 10, 10, 10 }));
        service.OnSnapshot(Snapshots.Make(t: 10, wear: new byte[] { 51, 10, 10, 10 }));
        Assert.Equal(2, sent.Count(c => c.Key == "tyre-wear-50"));
    }

    [Fact]
    public void NewSessionUidResets()
    {
        var (service, sent) = Make();
        service.OnSnapshot(WithUid(Snapshots.Make(t: 90, wear: new byte[] { 49, 10, 10, 10 }), 1));
        service.OnSnapshot(WithUid(Snapshots.Make(t: 100, wear: new byte[] { 51, 10, 10, 10 }), 1));
        Assert.Single(sent);

        service.OnSnapshot(WithUid(Snapshots.Make(t: 101, wear: new byte[] { 5, 5, 5, 5 }), 2));
        service.OnSnapshot(WithUid(Snapshots.Make(t: 102, fiaFlag: 4, wear: new byte[] { 5, 5, 5, 5 }), 2));
        Assert.Single(sent);  // warm-up after the new session
        service.OnSnapshot(WithUid(Snapshots.Make(t: 105, wear: new byte[] { 49, 10, 10, 10 }), 2));
        service.OnSnapshot(WithUid(Snapshots.Make(t: 106, wear: new byte[] { 51, 10, 10, 10 }), 2));
        Assert.Equal(2, sent.Count(c => c.Key == "tyre-wear-50"));
    }

    [Fact]
    public void SmallBackwardStepIsIgnoredNotAReset()
    {
        // A reordered packet steps session time back a fraction of a second
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 90, fiaFlag: 0));
        service.OnSnapshot(Snapshots.Make(t: 100, fiaFlag: 0));
        service.OnSnapshot(Snapshots.Make(t: 99.6f, fiaFlag: 0));
        // A reset would put this inside the warm-up and swallow it
        service.OnSnapshot(Snapshots.Make(t: 101, fiaFlag: 4));
        Assert.Equal("flag-red", Assert.Single(sent).Key);
    }

    [Fact]
    public void SilentAfterChequeredFlag()
    {
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 10, fiaFlag: 0, weather: 2));
        service.OnSnapshot(Snapshots.Make(t: 20, fiaFlag: 3, weather: 3));  // yellow + rain: one sent, one queued
        Assert.Single(sent);

        // Crossed the line: the queued call and anything new stay unsaid on the cool-down lap
        service.OnSnapshot(Snapshots.Make(t: 21, fiaFlag: 3, weather: 3, finished: true));
        service.OnSnapshot(Snapshots.Make(t: 25, fiaFlag: 3, weather: 3, finished: true));
        service.OnSnapshot(Snapshots.Make(t: 30, fiaFlag: 4, weather: 3, finished: true));
        Assert.Single(sent);
    }

    [Fact]
    public void LapReportUsesThePhonesLevel()
    {
        // Talkative: lap 5 (odd) gets a report, which Balanced would skip
        var sent = new List<RadioCall>();
        var service = new EngineerService(new EngineerThresholds(), sent.Add, level: () => 3);
        service.OnSnapshot(Snapshots.Make(t: 10, lap: 5));
        service.OnSnapshot(Snapshots.Make(t: 20, lap: 5, s1Ms: 25000, s2Ms: 30000));
        service.OnSnapshot(Snapshots.Make(t: 21, lap: 6, lastLapMs: 80000));
        Assert.Equal("lap-report", Assert.Single(sent).Key);
    }

    [Fact]
    public void ServiceForwardsLogLines()
    {
        var lines = new List<string>();
        var service = new EngineerService(new EngineerThresholds(), _ => { }, log: lines.Add);
        service.OnSnapshot(Snapshots.Make(t: 10, lap: 5));
        service.OnSnapshot(Snapshots.Make(t: 20, lap: 6));
        Assert.Equal(new[] { "lap 5 tyres  surface 90/90/90/90  inner 90/90/90/90" }, lines);
    }

    [Fact]
    public void OnlyCriticalInPitLane()
    {
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 10, fiaFlag: 0, pitStatus: 2));
        service.OnSnapshot(Snapshots.Make(t: 20, fiaFlag: 3, pitStatus: 2));  // yellow: dropped
        service.OnSnapshot(Snapshots.Make(t: 21, fiaFlag: 4, pitStatus: 2));  // red: critical
        service.OnSnapshot(Snapshots.Make(t: 30, fiaFlag: 4, pitStatus: 0));  // out of the pit lane
        Assert.Equal(new[] { "flag-red" }, sent.Select(c => c.Key));
    }

    [Fact]
    public void InLapReportSurvivesPitLane()
    {
        var sent = new List<RadioCall>();
        var service = new EngineerService(new EngineerThresholds(), sent.Add, level: () => 3);
        service.OnSnapshot(Snapshots.Make(t: 10, lap: 5));
        service.OnSnapshot(Snapshots.Make(t: 20, lap: 5, s1Ms: 25000, s2Ms: 30000, pitStatus: 1));
        service.OnSnapshot(Snapshots.Make(t: 21, lap: 6, lastLapMs: 95000, pitStatus: 1));
        var report = Assert.Single(sent);
        Assert.Equal("In lap.", report.Text);
    }

    [Fact]
    public void ErrorsAreContained()
    {
        // A failure inside the engineer must not escape into the telemetry receive loop
        var errors = new List<Exception>();
        var service = new EngineerService(new EngineerThresholds(), _ => throw new InvalidOperationException("boom"), errors.Add);
        service.OnSnapshot(Snapshots.Make(t: 10, fiaFlag: 0));
        service.OnSnapshot(Snapshots.Make(t: 20, fiaFlag: 4));
        Assert.Equal("boom", Assert.Single(errors).Message);
    }

    [Fact]
    public void QueuedCallsGoOutOnLaterSnapshots()
    {
        var (service, sent) = Make();
        service.OnSnapshot(Snapshots.Make(t: 10, fiaFlag: 0, weather: 2));
        service.OnSnapshot(Snapshots.Make(t: 20, fiaFlag: 3, weather: 3));  // yellow + rain: one goes now
        Assert.Single(sent);
        service.OnSnapshot(Snapshots.Make(t: 22, fiaFlag: 3, weather: 3));  // spacing not met
        Assert.Single(sent);
        service.OnSnapshot(Snapshots.Make(t: 24, fiaFlag: 3, weather: 3));
        Assert.Equal(new[] { "flag-yellow", "rain-now" }, sent.Select(c => c.Key).OrderBy(k => k));
    }
}
