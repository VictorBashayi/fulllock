namespace F1Controller.Engineer;

/// <summary>Tunable limits and cooldowns for the race engineer.</summary>
public sealed class EngineerThresholds
{
    public int TyreHotC { get; init; } = 110;
    public float TyreHotSeconds { get; init; } = 5f;
    public int TyreColdC { get; init; } = 70;
    public float TyreColdSeconds { get; init; } = 10f;
    public int WearWarnPct { get; init; } = 50;
    public int WearBoxPct { get; init; } = 70;
    public int WearRearmPct { get; init; } = 10;
    public float FuelTightLaps { get; init; } = -0.3f;
    public float FuelCriticalLaps { get; init; } = -1.0f;
    public int CatchMinLaps { get; init; } = 2;
    /// <summary>Smallest lap-to-lap gap shrink that counts as a trend; less is timing noise.</summary>
    public int CatchMinShrinkMs { get; init; } = 100;
    public int CatchMaxGapMs { get; init; } = 3000;
    public int ClosingMaxGapMs { get; init; } = 2000;
    public int DamageStepPct { get; init; } = 10;
    public int RainForecastMinutes { get; init; } = 10;

    public float MinSpacingSeconds { get; init; } = 4f;
    public float DefaultCooldownSeconds { get; init; } = 60f;
    public float GapCooldownSeconds { get; init; } = 240f;
    public float SectorCooldownSeconds { get; init; } = 20f;
    public float BlueFlagCooldownSeconds { get; init; } = 30f;
    public float ColdCooldownSeconds { get; init; } = 120f;
    public float RainForecastCooldownSeconds { get; init; } = 600f;
    public float StaleSeconds { get; init; } = 15f;
    public float WarmupSeconds { get; init; } = 3f;

    /// <summary>How long a call with this key stays quiet after being spoken. State-change calls have no cooldown.</summary>
    public float CooldownFor(string key)
    {
        if (key.StartsWith("gap-")) return GapCooldownSeconds;
        if (key.StartsWith("sector-")) return SectorCooldownSeconds;
        if (key.StartsWith("flag-blue")) return BlueFlagCooldownSeconds;
        if (key.StartsWith("tyre-cold")) return ColdCooldownSeconds;
        if (key.StartsWith("rain-forecast")) return RainForecastCooldownSeconds;
        if (key.StartsWith("sc-") || key.StartsWith("damage-")
            || key == "flag-red"
            || key == "penalty" || key == "drs" || key == "pb"
            || key == "last-lap" || key == "lap-invalid")
            return 0f;
        return DefaultCooldownSeconds;
    }
}
