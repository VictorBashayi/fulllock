namespace F1Controller.Pad;

/// <summary>Pad steering tuning.</summary>
public sealed class PadSettings
{
    /// <summary>Bank angle (degrees) at which steering reaches full lock.</summary>
    public float LockDeg { get; set; } = 18f;

    /// <summary>Bank angle (degrees) below which steering is zero.</summary>
    public float DeadzoneDeg { get; set; } = 1f;

    /// <summary>Centre smoothing 0..1, same as the phone's curve.</summary>
    public float Curve { get; set; }
}
