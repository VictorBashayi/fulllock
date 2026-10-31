using F1Controller.Pad;

namespace F1Controller.Tests;

public class PadSettingsTests
{
    [Fact]
    public void DefaultsAre18_1_0()
    {
        var s = new PadSettings();
        Assert.Equal((18f, 1f, 0f), (s.LockDeg, s.DeadzoneDeg, s.Curve));
    }

    [Fact]
    public void NudgesClampToRanges()
    {
        var s = new PadSettings();
        s.NudgeLock(-100);
        Assert.Equal(5f, s.LockDeg);
        s.NudgeLock(100);
        Assert.Equal(60f, s.LockDeg);
        s.NudgeCurve(100);
        Assert.Equal(1f, s.Curve, 3);
        s.NudgeCurve(-100);
        Assert.Equal(0f, s.Curve, 3);
        s.NudgeDeadzone(-100);
        Assert.Equal(0f, s.DeadzoneDeg);
    }

    [Fact]
    public void DeadzoneStaysBelowLock()
    {
        var s = new PadSettings();
        s.NudgeLock(-100);
        s.NudgeDeadzone(100);
        Assert.Equal(4.5f, s.DeadzoneDeg);

        // Shrinking the lock afterwards also pulls the deadzone down
        var t = new PadSettings();
        t.NudgeDeadzone(100);       // 10
        t.NudgeLock(-100);          // 5
        Assert.Equal(4.5f, t.DeadzoneDeg);
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{oops");
            var s = PadSettings.Load(path);
            Assert.Equal((18f, 1f, 0f), (s.LockDeg, s.DeadzoneDeg, s.Curve));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = PadSettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal(18f, s.LockDeg);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        string path = Path.GetTempFileName();
        try
        {
            new PadSettings { LockDeg = 22, DeadzoneDeg = 2, Curve = 0.3f }.Save(path);
            var s = PadSettings.Load(path);
            Assert.Equal((22f, 2f, 0.3f), (s.LockDeg, s.DeadzoneDeg, s.Curve));
        }
        finally { File.Delete(path); }
    }
}
