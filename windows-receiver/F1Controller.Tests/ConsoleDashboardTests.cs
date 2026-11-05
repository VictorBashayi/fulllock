using System.Buffers.Binary;
using System.Net;
using F1Controller.Security;
using F1Controller.Transport;
using F1Controller.UI;
using Xunit;

namespace F1Controller.Tests;

public class ConsoleDashboardTests
{
    static readonly byte[] Key = PacketAuth.DeriveKey("K7QD9MXP2A");
    static readonly byte[] PcSession = Convert.FromHexString("0102030405060708");

    static (ConsoleDashboard dash, Session session) Make()
    {
        var session = new Session(Key, PcSession);
        var receiver = new UdpReceiver(0, false, session);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try { return (new ConsoleDashboard(receiver, null, null, null, PairingStore.LoadOrCreate(path, b => b, b => b)), session); }
        finally { File.Delete(path); }
    }

    static byte[] Seal(byte[] ctx, byte[] payload, uint ctr)
    {
        var d = new byte[payload.Length + PacketAuth.TrailerSize];
        PacketAuth.Seal(Key, PacketAuth.DirPhoneToPc, ctx, payload, ctr, d);
        return d;
    }

    static void Pair(Session s)
    {
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 40000);
        byte[] nonce = [1, 2, 3, 4, 5, 6, 7, 8];
        var ping = new byte[Session.PingPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(ping, ControlPacket.PingMarker);
        nonce.CopyTo(ping, 17);
        Assert.Equal(AcceptResult.Ping, s.Accept(Seal([], ping, 1), ep, out _));
        var ctl = new byte[ControlPacket.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(ctl, 1);
        Assert.Equal(AcceptResult.ControlNewPhone, s.Accept(Seal([.. PcSession, .. nonce], ctl, 2), ep, out _));
    }

    [Fact]
    public void PairingShownUntilPaired()
    {
        var (d, s) = Make();
        Assert.True(d.ShowPairing);
        d.HandleKey('p');
        Assert.True(d.ShowPairing); // P does nothing while unpaired
        Pair(s);
        Assert.False(d.ShowPairing);
    }

    [Fact]
    public void PKeyTogglesAfterPaired()
    {
        var (d, s) = Make();
        Pair(s);
        d.HandleKey('P');
        Assert.True(d.ShowPairing);
        d.HandleKey('p');
        Assert.False(d.ShowPairing);
    }

    [Fact]
    public void OtherKeysIgnored()
    {
        var (d, s) = Make();
        Pair(s);
        d.HandleKey('x');
        d.HandleKey(' ');
        Assert.False(d.ShowPairing);
    }
}
