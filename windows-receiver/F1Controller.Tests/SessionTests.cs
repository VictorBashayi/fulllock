using System.Buffers.Binary;
using System.Net;
using F1Controller.Security;
using F1Controller.Transport;
using Xunit;

namespace F1Controller.Tests;

public class SessionTests
{
    static readonly byte[] Key = Convert.FromHexString("60a20da146d5592c8a6ad61091a0551ecf63753c657085b111d631a5dbdc99d9");
    static readonly byte[] PcSession = Convert.FromHexString("0102030405060708");
    static readonly IPEndPoint EpA = new(IPAddress.Parse("192.168.1.10"), 40000);
    static readonly IPEndPoint EpB = new(IPAddress.Parse("192.168.1.11"), 40001);
    static readonly IPEndPoint EpEvil = new(IPAddress.Parse("192.168.1.66"), 6666);

    /// <summary>Seals like the Android app would.</summary>
    sealed class Phone(byte nonceSeed, byte[]? key = null)
    {
        readonly byte[] _key = key ?? Key;
        public readonly byte[] Nonce = Enumerable.Range(nonceSeed, 8).Select(i => (byte)i).ToArray();
        public uint Ctr;
        uint _seq;

        public byte[] Ctx(byte[] pcSession) => [.. pcSession, .. Nonce];

        public byte[] Ping()
        {
            var p = new byte[Session.PingPayloadSize];
            BinaryPrimitives.WriteUInt32LittleEndian(p, ControlPacket.PingMarker);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), ++_seq);
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(8), 0x1122334455667788);
            p[16] = 2;
            Nonce.CopyTo(p, 17);
            return Seal(PacketAuth.DirPhoneToPc, [], p, 1);
        }

        public byte[] Control(uint? ctr = null)
        {
            var p = new byte[ControlPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(p, ++_seq);
            return Seal(PacketAuth.DirPhoneToPc, Ctx(PcSession), p, ctr ?? ++Ctr);
        }

        byte[] Seal(byte dir, byte[] ctx, byte[] payload, uint ctr)
        {
            var d = new byte[payload.Length + PacketAuth.TrailerSize];
            PacketAuth.Seal(_key, dir, ctx, payload, ctr, d);
            return d;
        }

        public (byte[] payload, uint ctr) Open(byte[] datagram)
        {
            Assert.True(PacketAuth.TryOpen(Key, PacketAuth.DirPcToPhone, Ctx(PcSession), datagram, out var n, out var ctr));
            return (datagram[..n], ctr);
        }
    }

    static Session NewSession() => new(Key, PcSession);

    static AcceptResult Accept(Session s, byte[] d, IPEndPoint from) => s.Accept(d, from, out _);

    static void Pair(Session s, Phone p, IPEndPoint ep)
    {
        Assert.Equal(AcceptResult.Ping, Accept(s, p.Ping(), ep));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, p.Control(), ep));
    }

    [Fact]
    public void ValidPingReturnsPingAndDoesNotActivate()
    {
        var s = NewSession();
        var ping = new Phone(0x11).Ping();
        Assert.Equal(AcceptResult.Ping, s.Accept(ping, EpA, out var n));
        Assert.Equal(Session.PingPayloadSize, n);
        Assert.Null(s.ActiveEndPoint);
        Assert.False(s.HasPaired);
    }

    [Fact]
    public void PongVerifiesWithPhoneNonce()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        var ping = phone.Ping();
        Assert.Equal(AcceptResult.Ping, s.Accept(ping, EpA, out var n));
        var (pong, ctr) = phone.Open(s.BuildPong(ping.AsSpan(0, n)));
        Assert.Equal(Session.PongPayloadSize, pong.Length);
        Assert.Equal(ping[..16], pong[..16]);
        Assert.Equal(PcSession, pong[16..24]);
        Assert.Equal(PcSession, s.PcSession.ToArray());
        Assert.Equal(1u, ctr);
    }

    [Fact]
    public void BuildPongRejectsWrongLength() =>
        Assert.Throws<ArgumentException>(() => NewSession().BuildPong(new byte[16]));

    [Fact]
    public void ControlAfterPingActivatesAndRaisesPairedOnce()
    {
        var s = NewSession();
        int paired = 0;
        s.Paired += () => paired++;
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        Assert.True(s.HasPaired);
        Assert.Equal(EpA, s.ActiveEndPoint);
        Assert.Equal(AcceptResult.Control, s.Accept(phone.Control(), EpA, out var n));
        Assert.Equal(ControlPacket.Size, n);
        Pair(s, new Phone(0x21), EpB);
        Assert.Equal(1, paired);
    }

    [Fact]
    public void ActiveEndPointFollowsRoamingPhone()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        Assert.Equal(AcceptResult.Control, Accept(s, phone.Control(), EpB));
        Assert.Equal(EpB, s.ActiveEndPoint);
    }

    [Fact]
    public void ControlWithoutPingIsRejected()
    {
        var s = NewSession();
        Assert.Equal(AcceptResult.Rejected, Accept(s, new Phone(0x11).Control(), EpA));
        Assert.Null(s.ActiveEndPoint);
        Assert.False(s.HasPaired);
    }

    [Fact]
    public void ReplayedControlIsRejected()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        var c = phone.Control();
        Assert.Equal(AcceptResult.Control, Accept(s, c, EpA));
        Assert.Equal(AcceptResult.Rejected, Accept(s, c, EpA));
    }

    [Fact]
    public void OlderCtrIsRejected()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        Assert.Equal(AcceptResult.Control, Accept(s, phone.Control(10), EpA));
        Assert.Equal(AcceptResult.Rejected, Accept(s, phone.Control(9), EpA));
        Assert.Equal(AcceptResult.Control, Accept(s, phone.Control(11), EpA));
    }

    [Fact]
    public void V1ControlPacketIsRejected()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        var v1 = Convert.FromHexString("010000000000000000000000004080000010");
        Assert.Equal(AcceptResult.Rejected, Accept(s, v1, EpEvil));
        Assert.Equal(AcceptResult.Rejected, Accept(s, [.. v1, .. new byte[12]], EpEvil));
        Assert.Equal(AcceptResult.Rejected, Accept(s, [.. v1, .. Enumerable.Repeat((byte)0xA5, 12)], EpEvil));
        Assert.Equal(EpA, s.ActiveEndPoint);
    }

    [Fact]
    public void WrongKeyRejected()
    {
        var s = NewSession();
        var wrong = new Phone(0x11, Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.Equal(AcceptResult.Rejected, Accept(s, wrong.Ping(), EpA));
        Assert.Equal(AcceptResult.Rejected, Accept(s, wrong.Control(), EpA));
        Assert.False(s.HasPaired);
    }

    [Fact]
    public void InvalidPacketDoesNotChangeActiveEndPoint()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        var forged = phone.Control();
        forged[^1] ^= 1;
        var badPing = phone.Ping();
        badPing[^1] ^= 1;
        foreach (var d in new[] { forged, badPing, new byte[30], new byte[37], [0xFF, 0xFF, 0xFF, 0xFF], [], new byte[3] })
            Assert.Equal(AcceptResult.Rejected, Accept(s, d, EpEvil));
        Assert.Equal(EpA, s.ActiveEndPoint);
        Assert.Equal(AcceptResult.Control, Accept(s, phone.Control(), EpA));
    }

    [Fact]
    public void PingAloneDoesNotStealActive()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        var ping = phone.Ping();
        Assert.Equal(AcceptResult.Ping, Accept(s, ping, EpA));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, phone.Control(), EpA));
        Assert.Equal(AcceptResult.Ping, Accept(s, ping, EpEvil));
        Assert.Equal(AcceptResult.Ping, Accept(s, new Phone(0x31).Ping(), EpEvil));
        Assert.Equal(EpA, s.ActiveEndPoint);
    }

    [Fact]
    public void NewNonceTakesOverAfterValidControl()
    {
        var s = NewSession();
        var a = new Phone(0x11);
        var b = new Phone(0x21);
        Pair(s, a, EpA);
        Pair(s, b, EpB);
        Assert.Equal(EpB, s.ActiveEndPoint);
        Assert.Equal(AcceptResult.Rejected, Accept(s, a.Control(), EpA));
        Assert.Equal(EpB, s.ActiveEndPoint);
        Assert.Equal(AcceptResult.Ping, Accept(s, a.Ping(), EpA));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, a.Control(), EpA));
        Assert.Equal(EpA, s.ActiveEndPoint);
    }

    [Fact]
    public void ReplayedPingAndControlOfDroppedPhoneCannotTakeOver()
    {
        var s = NewSession();
        var a = new Phone(0x11);
        var ping = a.Ping();
        Assert.Equal(AcceptResult.Ping, Accept(s, ping, EpA));
        var oldControl = a.Control();
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, oldControl, EpA));
        Pair(s, new Phone(0x21), EpB);
        // attacker captured A's ping and control earlier and replays both
        Assert.Equal(AcceptResult.Ping, Accept(s, ping, EpEvil));
        Assert.Equal(AcceptResult.Rejected, Accept(s, oldControl, EpEvil));
        Assert.Equal(EpB, s.ActiveEndPoint);
    }

    [Fact]
    public void PendingListKeepsFourMostRecent()
    {
        var s = NewSession();
        var phones = Enumerable.Range(0, 5).Select(i => new Phone((byte)(0x10 * (i + 1)))).ToArray();
        foreach (var p in phones) Assert.Equal(AcceptResult.Ping, Accept(s, p.Ping(), EpA));
        Assert.Equal(AcceptResult.Rejected, Accept(s, phones[0].Control(), EpA));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, phones[1].Control(), EpA));
    }

    [Fact]
    public void ActivePhonePingDoesNotEnterPending()
    {
        var s = NewSession();
        var a = new Phone(0x11);
        Pair(s, a, EpA);
        Assert.Equal(AcceptResult.Ping, Accept(s, a.Ping(), EpA));
        Pair(s, new Phone(0x21), EpB);
        Assert.Equal(AcceptResult.Rejected, Accept(s, a.Control(), EpA));
        Assert.Equal(AcceptResult.Ping, Accept(s, a.Ping(), EpA));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, a.Control(), EpA));
    }

    [Fact]
    public void RePingMovesToMostRecentWithoutDuplicating()
    {
        var s = NewSession();
        var p = Enumerable.Range(0, 5).Select(i => new Phone((byte)(0x10 * (i + 1)))).ToArray();
        // move: P0..P3, re-ping P0, then P4 evicts P1 (not P0)
        foreach (var i in new[] { 0, 1, 2, 3, 0, 4 }) Accept(s, p[i].Ping(), EpA);
        Assert.Equal(AcceptResult.Rejected, Accept(s, p[1].Control(), EpA));
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, p[0].Control(), EpA));

        // no duplicates: P0, P1, P0, P0, P2, P3 keeps P1 (duplicates would have evicted it)
        s = NewSession();
        p = Enumerable.Range(0, 4).Select(i => new Phone((byte)(0x50 + 0x10 * i))).ToArray();
        foreach (var i in new[] { 0, 1, 0, 0, 2, 3 }) Accept(s, p[i].Ping(), EpA);
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, p[1].Control(), EpA));
    }

    [Fact]
    public void PromotionRemovesNonceFromPending()
    {
        var s = NewSession();
        var p = Enumerable.Range(0, 5).Select(i => new Phone((byte)(0x10 * (i + 1)))).ToArray();
        foreach (var i in new[] { 0, 1, 2, 3 }) Accept(s, p[i].Ping(), EpA);
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, p[3].Control(), EpA));
        Accept(s, p[4].Ping(), EpA); // P3 left pending, so P4 fits without evicting P0
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, p[0].Control(), EpA));
    }

    [Fact]
    public void SealedPingWithWrongLengthIsRejected()
    {
        var s = NewSession();
        var p = new byte[Session.PingPayloadSize + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(p, ControlPacket.PingMarker);
        var d = new byte[p.Length + PacketAuth.TrailerSize];
        PacketAuth.Seal(Key, PacketAuth.DirPhoneToPc, [], p, 1, d);
        Assert.Equal(AcceptResult.Rejected, Accept(s, d, EpA));
        Assert.Equal(1, s.RejectedPackets);
    }

    [Fact]
    public void PcDirectionPacketIsRejected()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Pair(s, phone, EpA);
        var d = new byte[ControlPacket.Size + PacketAuth.TrailerSize];
        PacketAuth.Seal(Key, PacketAuth.DirPcToPhone, phone.Ctx(PcSession), new byte[ControlPacket.Size], 100, d);
        Assert.Equal(AcceptResult.Rejected, Accept(s, d, EpEvil));
        Assert.Equal(EpA, s.ActiveEndPoint);
    }

    [Fact]
    public void BuildPongRequiresPingMarker()
    {
        var p = new byte[Session.PingPayloadSize];
        Assert.Throws<ArgumentException>(() => NewSession().BuildPong(p));
    }

    [Fact]
    public void SendCounterStopsAtMaxInsteadOfWrapping()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        var ping = phone.Ping();
        Accept(s, ping, EpA);
        Accept(s, phone.Control(), EpA);
        s.NextSendCtr = uint.MaxValue;
        Assert.Equal(uint.MaxValue, phone.Open(s.SealForActive(new byte[4])!).ctr);
        Assert.Null(s.SealForActive(new byte[4]));
        Assert.Throws<InvalidOperationException>(() => s.BuildPong(ping.AsSpan(0, Session.PingPayloadSize)));
    }

    [Fact]
    public void SealForActiveNullBeforePairing()
    {
        var s = NewSession();
        Assert.Null(s.SealForActive(new byte[16]));
        Accept(s, new Phone(0x11).Ping(), EpA);
        Assert.Null(s.SealForActive(new byte[16]));
    }

    [Fact]
    public void SealForActiveCountersIncrease()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        var ping = phone.Ping();
        Assert.Equal(AcceptResult.Ping, s.Accept(ping, EpA, out var n));
        Assert.Equal(1u, phone.Open(s.BuildPong(ping.AsSpan(0, n))).ctr);
        Assert.Equal(AcceptResult.ControlNewPhone, Accept(s, phone.Control(), EpA));
        var data = Convert.FromHexString("feffffff033200000000000000000000");
        var (p2, c2) = phone.Open(s.SealForActive(data)!);
        var (_, c3) = phone.Open(s.SealForActive(data)!);
        Assert.Equal(data, p2);
        Assert.Equal(2u, c2);
        Assert.Equal(3u, c3);
    }

    [Fact]
    public void RejectedPacketsCounts()
    {
        var s = NewSession();
        var phone = new Phone(0x11);
        Assert.Equal(0, s.RejectedPackets);
        Accept(s, new byte[5], EpEvil);
        Accept(s, phone.Control(), EpEvil);
        Assert.Equal(2, s.RejectedPackets);
        Pair(s, phone, EpA);
        Assert.Equal(2, s.RejectedPackets);
    }

    [Fact]
    public void ConstructorValidatesLengths()
    {
        Assert.Throws<ArgumentException>(() => new Session(new byte[16]));
        Assert.Throws<ArgumentException>(() => new Session(Key, new byte[7]));
        Assert.Equal(Session.NonceSize, new Session(Key).PcSession.Length);
    }
}
