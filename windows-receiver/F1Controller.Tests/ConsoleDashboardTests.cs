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

    static string[] Lines(string panel) => panel.TrimEnd('\r', '\n').Split(Environment.NewLine);

    [Fact]
    public void PairingPanelWithQrFitsDefaultConsole()
    {
        var (d, s) = Make();
        // worst case for the notes: paired (P toggled back on) + rejected packets
        Pair(s);
        s.Accept(new byte[37], new IPEndPoint(IPAddress.Loopback, 1), out _);
        d.HandleKey('p');
        var lines = Lines(d.BuildPairingPanel(119, 30, "Signal lost — inputs released"));
        Assert.True(lines.Length < 30, $"{lines.Length} rows");
        Assert.All(lines, l => Assert.True(l.Length <= 119));
        Assert.Contains(lines, l => l.Contains("████████"));
        Assert.Contains(lines, l => l.StartsWith("  Pair code: "));
        Assert.Contains(lines, l => l.Contains("Scan or type this in the FullLock app"));
    }

    [Fact]
    public void PairingPanelTooSmallFallsBackToText()
    {
        var (d, _) = Make();
        var lines = Lines(d.BuildPairingPanel(119, 20, "Waiting for phone..."));
        Assert.DoesNotContain(lines, l => l.Contains('█'));
        Assert.Contains(lines, l => l.StartsWith("  Pair code: "));
        Assert.Contains(lines, l => l.Contains("Enlarge this window to show the QR code"));
        // no console (0x0) gives the same text-only panel
        Assert.Contains(Lines(d.BuildPairingPanel(0, 0, "x")), l => l.Contains("Enlarge this window"));
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
