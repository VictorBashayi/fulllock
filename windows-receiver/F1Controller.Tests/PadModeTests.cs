using F1Controller.Pad;

namespace F1Controller.Tests;

public class PadModeTests
{
    [Fact]
    public void StickAxisCentresAndClamps()
    {
        Assert.Equal(0, PadMode.StickAxis(128, invert: false));
        Assert.Equal(32766, PadMode.StickAxis(255, invert: false));
        Assert.Equal(-32768, PadMode.StickAxis(0, invert: false));
    }

    [Fact]
    public void StickAxisInvertsYSoUpIsPositive()
    {
        Assert.True(PadMode.StickAxis(0, invert: true) > 32000);   // DS4 stick pushed up
        Assert.True(PadMode.StickAxis(255, invert: true) < -32000);
    }
}
