using System.Collections.Generic;

namespace F1Controller.Engineer;

/// <summary>
/// Looks at consecutive race snapshots and returns the calls the engineer could make.
/// Rule groups live in partial-class files and keep their memory in private fields; <see cref="Reset"/> clears it.
/// Rate limiting is the dispatcher's job, not the rules'.
/// </summary>
public sealed partial class EngineerRules
{
    private readonly EngineerThresholds _t;

    public EngineerRules(EngineerThresholds t) => _t = t;

    /// <param name="level">Engineer chattiness from the phone (0 Off … 3 Talkative); paces the lap report.</param>
    public IReadOnlyList<RadioCall> Evaluate(RaceSnapshot? previous, RaceSnapshot current, int level = 2)
    {
        // First, so the other rules see which lap (out-lap or not) this snapshot belongs to
        bool personalBest = previous != null && IsPersonalBest(previous.Race.Timing, current.Race.Timing);
        var report = LapReport(previous, current, level, personalBest);

        var calls = new List<RadioCall>();
        calls.AddRange(TyresFuel(previous, current));
        calls.AddRange(GapsLap(previous, current));
        calls.AddRange(FlagsWeatherDamage(previous, current));

        if (report != null)
        {
            // The report opens with "Personal best." itself
            calls.RemoveAll(c => c.Key == "pb");
            calls.Add(report);
        }
        return calls;
    }

    /// <summary>Log-only lines produced since the last call (per-lap tyre temperatures); clears them.</summary>
    public IReadOnlyList<string> TakeLogLines()
    {
        var lines = _logLines.ToArray();
        _logLines.Clear();
        return lines;
    }

    private readonly List<string> _logLines = new();

    public void Reset()
    {
        ResetTyresFuel();
        ResetGapsLap();
        ResetFlagsWeatherDamage();
        ResetLapReport();
        _logLines.Clear();
    }
}
