using F1Controller.Pad;

namespace F1Controller.Tests;

public class Ds4OutputTests
{
    [Fact]
    public void UsbRumble_PutsMotorsInTheRightBytes_AndOnlySetsTheRumbleFlag()
    {
        var b = Ds4Output.UsbRumble(heavy: 200, light: 50);

        Assert.Equal(32, b.Length);
        Assert.Equal(0x05, b[0]);
        Assert.Equal(0x01, b[1]);   // rumble only: lightbar untouched
        Assert.Equal(50, b[4]);     // right / light motor
        Assert.Equal(200, b[5]);    // left / heavy motor
        Assert.All(b[6..], x => Assert.Equal(0, x));
    }
}
