using F1Controller.Pad;

namespace F1Controller.Tests;

public class PadSteeringTests
{
    private static readonly PadSettings Defaults = new();

    /// <summary>Gravity of a flat, face-up pad banked by b about Z, then nose-tilted by p about X.</summary>
    private static (float x, float y, float z) Grav(float bankDeg, float pitchDeg)
    {
        float b = bankDeg * MathF.PI / 180f, p = pitchDeg * MathF.PI / 180f;
        return (MathF.Sin(b), MathF.Cos(b) * MathF.Cos(p), MathF.Cos(b) * MathF.Sin(p));
    }

    private static short Steer(float bankDeg, float pitchDeg, PadSettings s)
    {
        var (x, y, z) = Grav(bankDeg, pitchDeg);
        return PadSteering.SteerFromBank(PadSteering.BankDeg(x, y, z), s);
    }

    [Fact]
    public void FlatIsZero() => Assert.Equal(0, Steer(0, 0, Defaults));

    [Fact]
    public void FullLockAtLockAngle()
    {
        Assert.Equal(32767, Steer(18, 0, Defaults));
        Assert.Equal(-32767, Steer(-18, 0, Defaults));
        Assert.Equal(32767, Steer(30, 0, Defaults));
    }

    [Fact]
    public void PitchDoesNotChangeOutput()
    {
        short level = Steer(10, 0, Defaults);
        Assert.Equal(level, Steer(10, 30, Defaults));
        Assert.Equal(level, Steer(10, -30, Defaults));
    }

    [Fact]
    public void DeadzoneAndHalfway()
    {
        Assert.Equal(0, Steer(0.9f, 0, Defaults));
        Assert.InRange(Steer(9.5f, 0, Defaults), 16383 - 1, 16384 + 1);
    }

    [Fact]
    public void CurveMatchesPhone()
    {
        var s = new PadSettings { Curve = 0.5f };
        int expected = (int)MathF.Round((0.5f * 0.5f + 0.5f * 0.125f) * 32767);
        Assert.InRange(Steer(9.5f, 0, s), expected - 1, expected + 1);
    }

    [Fact]
    public void UpsideDownDoesNotFlip()
    {
        // Rolled past vertical to the right: gravity Y has gone negative
        short steer = PadSteering.SteerFromBank(PadSteering.BankDeg(0.3f, -0.95f, 0f), Defaults);
        Assert.True(steer > 0);
    }

    [Fact]
    public void RightBankOnTheRealPadSteersRight()
    {
        // Measured on hardware: right side down gives negative accel X (-30° ≈ -4096 / 7094)
        var steering = new PadSteering(Defaults);
        var r = new Ds4Report(128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, -4096, 7094, 0, 100);
        steering.Process(r, Tick(0));
        short steer = steering.Process(r, Tick(5));
        Assert.Equal(32767, steer);
        Assert.True(steering.LastBankDeg > 29f);
    }

    [Fact]
    public void ResetFilterForgetsTheLeanBeforeAPadLoss()
    {
        var steering = new PadSteering(Defaults);
        var banked = new Ds4Report(128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, -4096, 7094, 0, 100);
        for (int t = 0; t < 50; t++) steering.Process(banked, Tick(t * 5));

        steering.ResetFilter();
        var flat = banked with { AccelX = 0, AccelY = 8192, Timestamp = 12345 };
        Assert.Equal(0, steering.Process(flat, Tick(1000)));
    }

    [Fact]
    public void RotationIsTimedByArrivalNotThePadTimestamp()
    {
        // Measured on hardware: the pad's timestamp ran at ~55% of real time, so it can't be trusted.
        // Same timestamp on every report, 5 ms apart on the PC clock, gyro Z (bank axis) at 90 °/s for 100 ms.
        var steering = new PadSteering(Defaults);
        var flat = new Ds4Report(128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0, 8192, 0, 100);
        steering.Process(flat, Tick(0));
        var turning = flat with { GyroZ = (short)(90 * PadSteering.GyroLsbPerDegSec) };
        for (int i = 1; i <= 20; i++) steering.Process(turning, Tick(i * 5));
        // 9° turned; the filter pulls ~3.4° back toward the (still flat) accel. Timestamp-based dt gave 0°.
        Assert.True(MathF.Abs(steering.LastBankDeg) > 4f, $"bank {steering.LastBankDeg}");
    }

    private static long Tick(double ms) => (long)(ms / 1000 * System.Diagnostics.Stopwatch.Frequency);
}
