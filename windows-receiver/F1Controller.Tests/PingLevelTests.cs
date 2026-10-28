using F1Controller.Transport;

namespace F1Controller.Tests;

public class PingLevelTests
{
    private static byte[] Ping(int length, byte level = 0)
    {
        var p = new byte[length];
        p[0] = p[1] = p[2] = p[3] = 0xFF;
        if (length > 16) p[16] = level;
        return p;
    }

    [Fact]
    public void PingLevelDefaultsAndClamps()
    {
        Assert.Equal(3, ControlPacket.ReadPingLevel(Ping(17, 3)));
        Assert.Equal(0, ControlPacket.ReadPingLevel(Ping(17, 0)));
        Assert.Equal(2, ControlPacket.ReadPingLevel(Ping(16)));     // old app: Balanced
        Assert.Equal(2, ControlPacket.ReadPingLevel(Ping(17, 7)));  // out of range: Balanced
    }
}
