using System;
using System.IO;
using System.Text.Json;

namespace F1Controller.Pad;

/// <summary>Pad steering tuning, nudged with keys and kept in pad_settings.json.</summary>
public sealed class PadSettings
{
    /// <summary>Bank angle (degrees) at which steering reaches full lock.</summary>
    public float LockDeg { get; set; } = 45f;

    /// <summary>Bank angle (degrees) below which steering is zero.</summary>
    public float DeadzoneDeg { get; set; } = 1f;

    /// <summary>Centre smoothing 0..1, same as the phone's curve.</summary>
    public float Curve { get; set; }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "pad_settings.json");

    public void NudgeLock(int steps)
    {
        LockDeg = Math.Clamp(LockDeg + steps, 5f, 60f);
        Normalise();
    }

    public void NudgeDeadzone(int steps)
    {
        DeadzoneDeg += steps * 0.5f;
        Normalise();
    }

    public void NudgeCurve(int steps)
    {
        // Rounded so repeated 0.05 steps don't drift
        Curve = MathF.Round(Math.Clamp(Curve + steps * 0.05f, 0f, 1f), 2);
    }

    /// <summary>Missing or unreadable file → defaults; values out of range are clamped.</summary>
    public static PadSettings Load(string path)
    {
        PadSettings s;
        try { s = JsonSerializer.Deserialize<PadSettings>(File.ReadAllText(path)) ?? new PadSettings(); }
        catch (Exception) { s = new PadSettings(); }
        s.LockDeg = Math.Clamp(s.LockDeg, 5f, 60f);
        s.Curve = Math.Clamp(s.Curve, 0f, 1f);
        s.Normalise();
        return s;
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Save without throwing: a read-only or locked file must not end a drive.</summary>
    public bool TrySave(string path, out string? error)
    {
        try { Save(path); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // Deadzone stays within 0..10 and at least half a degree below the lock
    private void Normalise() =>
        DeadzoneDeg = Math.Clamp(DeadzoneDeg, 0f, MathF.Min(10f, LockDeg - 0.5f));
}
