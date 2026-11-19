using System.Net;
using F1Controller.Transport;
using Xunit;

namespace F1Controller.Tests;

public class DiscoveryBeaconTests
{
    [Fact]
    public void WritesMarkerTagAndPort()
    {
        var buf = new byte[ControlPacket.DiscoverySize];
        ControlPacket.WriteDiscovery(buf, 5555);
        Assert.Equal(new byte[] { 0xFB, 0xFF, 0xFF, 0xFF, (byte)'F', (byte)'L', (byte)'R', (byte)'X', 0xB3, 0x15, 0x02 },
            buf);
    }

    [Theory]
    [InlineData("192.168.206.224", "255.255.255.0", "192.168.206.255")]
    [InlineData("10.0.5.17", "255.255.0.0", "10.0.255.255")]
    [InlineData("172.20.10.3", "255.255.255.240", "172.20.10.15")]
    public void BroadcastAddressSetsHostBits(string ip, string mask, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected),
            DiscoveryBeacon.BroadcastAddress(IPAddress.Parse(ip), IPAddress.Parse(mask)));
    }
}
