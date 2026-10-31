using F1Controller.Pad;

namespace F1Controller.Tests;

public class MadgwickFilterTests
{
    [Fact]
    public void ConvergesToAccelDirectionWhenStill()
    {
        var f = new MadgwickFilter();
        // Start ~19° away so convergence is actually exercised (beta 0.1 corrects ~0.1 rad/s)
        f.Update(0, 0, 0, 0, 1, 0, 0.004f);
        for (int i = 0; i < 2500; i++)
            f.Update(0, 0, 0, 0.3f, 0.9f, 0.1f, 0.004f);

        float n = MathF.Sqrt(0.3f * 0.3f + 0.9f * 0.9f + 0.1f * 0.1f);
        Assert.InRange(f.GravityX(), 0.3f / n - 0.01f, 0.3f / n + 0.01f);
        Assert.InRange(f.GravityY(), 0.9f / n - 0.01f, 0.9f / n + 0.01f);
        Assert.InRange(f.GravityZ(), 0.1f / n - 0.01f, 0.1f / n + 0.01f);
    }

    [Fact]
    public void FirstSampleInitialisesFromAccel()
    {
        var f = new MadgwickFilter();
        f.Update(0, 0, 0, 0, 1, 0, 0.004f);
        Assert.True(f.GravityY() > 0.99f);
    }

    [Fact]
    public void ZeroAccelDoesNotProduceNaN()
    {
        var f = new MadgwickFilter();
        f.Update(0, 0, 0, 0, 1, 0, 0.004f);
        for (int i = 0; i < 10; i++)
            f.Update(0.1f, 0, 0, 0, 0, 0, 0.004f);
        Assert.True(float.IsFinite(f.GravityX()) && float.IsFinite(f.GravityY()) && float.IsFinite(f.GravityZ()));
    }
}
