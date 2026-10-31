using System;

namespace F1Controller.Pad;

/// <summary>
/// Steering from a DS4 held flat and banked left/right.
/// Pipeline: gyro + accel → Madgwick gravity → bank angle → deadzone → lock scaling → curve → i16.
/// The bank angle ignores nose tilt, like the phone's HORIZON axis.
/// </summary>
public sealed class PadSteering
{
    // Fitted on a 20 s hardware recording: filtered bank tracked the accelerometer best at
    // 12.8 LSB per °/s (16 left 6.8° rms error) and beta 0.3 (2.2° rms)
    private const float GyroLsbPerDegSec = 12.8f;
    private const float DegToRad = MathF.PI / 180f;

    private readonly PadSettings _settings;
    private readonly MadgwickFilter _filter = new() { Beta = 0.3f };
    private double _biasX, _biasY, _biasZ;
    private long _biasCount;
    private ushort? _lastTimestamp;

    public PadSteering(PadSettings settings) => _settings = settings;

    /// <summary>Latest bank from the filtered gravity (degrees, + = right).</summary>
    public float LastBankDeg { get; private set; }

    /// <summary>Latest bank from the raw accelerometer alone, to sanity-check the gyro axis signs.</summary>
    public float LastAccelBankDeg { get; private set; }

    /// <summary>Bank angle in degrees from gravity in the pad frame (flat = gravity along Y).</summary>
    public static float BankDeg(float gX, float gY, float gZ) =>
        MathF.Atan2(gX, MathF.Sqrt(gY * gY + gZ * gZ)) / DegToRad;

    /// <summary>Same deadzone / scaling / curve as the phone's SteeringProcessor.</summary>
    public static short SteerFromBank(float bankDeg, PadSettings s)
    {
        float a = MathF.Abs(bankDeg);
        if (a < s.DeadzoneDeg) return 0;
        float x = MathF.Min((a - s.DeadzoneDeg) / MathF.Max(s.LockDeg - s.DeadzoneDeg, 1f), 1f);
        float k = Math.Clamp(s.Curve, 0f, 1f);
        x = (1f - k) * x + k * x * x * x;
        return (short)(MathF.Sign(bankDeg) * MathF.Round(x * short.MaxValue));
    }

    /// <summary>Interval between two DS4 report timestamps (u16, 16/3 µs per tick, wraps).</summary>
    public static float DtSeconds(ushort previous, ushort current) =>
        (ushort)(current - previous) * (16f / 3f) * 1e-6f;

    public void AddBiasSample(Ds4Report r)
    {
        _biasX += r.GyroX; _biasY += r.GyroY; _biasZ += r.GyroZ;
        _biasCount++;
    }

    public void FinishBias()
    {
        if (_biasCount == 0) return;
        _biasX /= _biasCount; _biasY /= _biasCount; _biasZ /= _biasCount;
        _biasCount = 0;
    }

    /// <summary>
    /// Forget orientation after a pad loss: the next report re-seeds the filter from its accelerometer,
    /// instead of easing out of the lean held before the dropout.
    /// </summary>
    public void ResetFilter()
    {
        _filter.Reset();
        _lastTimestamp = null;
    }

    public short Process(Ds4Report r)
    {
        float dt = _lastTimestamp is ushort last ? DtSeconds(last, r.Timestamp) : 0f;
        _lastTimestamp = r.Timestamp;

        float gx = (float)(r.GyroX - _biasX) / GyroLsbPerDegSec * DegToRad;
        float gy = (float)(r.GyroY - _biasY) / GyroLsbPerDegSec * DegToRad;
        float gz = (float)(r.GyroZ - _biasZ) / GyroLsbPerDegSec * DegToRad;
        _filter.Update(gx, gy, gz, r.AccelX, r.AccelY, r.AccelZ, dt);

        // On the DS4, right side down reads negative X, so X is negated to make right lean positive
        LastAccelBankDeg = BankDeg(-r.AccelX, r.AccelY, r.AccelZ);
        LastBankDeg = BankDeg(-_filter.GravityX(), _filter.GravityY(), _filter.GravityZ());
        return SteerFromBank(LastBankDeg, _settings);
    }
}
