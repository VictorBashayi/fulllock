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

    public IReadOnlyList<RadioCall> Evaluate(RaceSnapshot? previous, RaceSnapshot current)
    {
        var calls = new List<RadioCall>();
        calls.AddRange(TyresFuel(previous, current));
        // Later tasks add: calls.AddRange(GapsLap(previous, current)); calls.AddRange(FlagsWeatherDamage(previous, current));
        return calls;
    }

    public void Reset()
    {
        ResetTyresFuel();
    }
}
