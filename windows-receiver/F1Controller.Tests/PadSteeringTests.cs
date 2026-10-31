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
    public void TimestampWrapGivesSmallPositiveDt()
    {
        float dt = PadSteering.DtSeconds(65500, 100);
        Assert.InRange(dt, 136 * 16f / 3f * 1e-6f * 0.999f, 136 * 16f / 3f * 1e-6f * 1.001f);
    }
}
