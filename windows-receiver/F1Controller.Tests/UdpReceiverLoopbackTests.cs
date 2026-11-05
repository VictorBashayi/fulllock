using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using F1Controller.Security;
using F1Controller.Transport;
using Xunit;

namespace F1Controller.Tests;

public sealed class UdpReceiverLoopbackTests : IAsyncLifetime
{
    static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    static readonly byte[] Nonce = [1, 2, 3, 4, 5, 6, 7, 8];

    readonly Session _session = new(Key);
    readonly CancellationTokenSource _cts = new();
    readonly UdpClient _phone = new(new IPEndPoint(IPAddress.Loopback, 0));
    UdpReceiver _rx = null!;
    Task _run = null!;
    IPEndPoint _pc = null!;
    uint _ctr, _seq;

    public async Task InitializeAsync()
    {
        _rx = new UdpReceiver(0, false, _session);
        _run = _rx.RunAsync(_cts.Token);
        for (int i = 0; i < 200 && _rx.BoundPort == 0; i++) await Task.Delay(10);
        Assert.NotEqual(0, _rx.BoundPort);
        _pc = new IPEndPoint(IPAddress.Loopback, _rx.BoundPort);
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await Task.WhenAny(_run, Task.Delay(2000));
        _rx.Dispose();
        _phone.Dispose();
    }

    byte[] Seal(byte[] key, byte dir, byte[] ctx, byte[] payload, uint ctr)
    {
        var d = new byte[payload.Length + PacketAuth.TrailerSize];
        PacketAuth.Seal(key, dir, ctx, payload, ctr, d);
        return d;
    }

    byte[] Ping()
    {
        var p = new byte[Session.PingPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(p, ControlPacket.PingMarker);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), ++_seq);
        p[16] = 2;
        Nonce.CopyTo(p, 17);
        return Seal(Key, PacketAuth.DirPhoneToPc, [], p, 1);
    }

    byte[] Control(short steer, byte[]? key = null)
    {
        var p = new byte[ControlPacket.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(p, ++_seq);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(12), steer);
        return Seal(key ?? Key, PacketAuth.DirPhoneToPc, [.. _session.PcSession.ToArray(), .. Nonce], p, ++_ctr);
    }

    async Task<byte[]?> Recv(int ms)
    {
        var t = _phone.ReceiveAsync();
        if (await Task.WhenAny(t, Task.Delay(ms)) != t) return null;
        return (await t).Buffer;
    }

    async Task PairAsync()
    {
        await _phone.SendAsync(Ping(), _pc);
        var pong = await Recv(2000);
        Assert.NotNull(pong);
        Assert.True(PacketAuth.TryOpen(Key, PacketAuth.DirPcToPhone, [.. _session.PcSession.ToArray(), .. Nonce], pong, out _, out _));
    }

    [Fact]
    public async Task PairedPhoneDrivesPad()
    {
        var got = new TaskCompletionSource<ControlPacket>();
        _rx.OnPacketReceived += (p, _) => got.TrySetResult(p);
        await PairAsync();
        await _phone.SendAsync(Control(0x4000), _pc);
        Assert.True(await Task.WhenAny(got.Task, Task.Delay(2000)) == got.Task);
        Assert.Equal(0x4000, (await got.Task).Steer);
    }

    [Fact]
    public async Task ForgedAndReplayedPacketsDoNotFire()
    {
        int fired = 0;
        _rx.OnPacketReceived += (_, _) => Interlocked.Increment(ref fired);
        await PairAsync();
        var valid = Control(0x4000);
        await _phone.SendAsync(valid, _pc);
        for (int i = 0; i < 200 && fired == 0; i++) await Task.Delay(10);
        Assert.Equal(1, fired);

        await _phone.SendAsync(new byte[ControlPacket.Size], _pc);                 // unsealed V1
        await _phone.SendAsync(Control(0x1000, RandomNumberGenerator.GetBytes(32)), _pc); // wrong key
        await _phone.SendAsync(valid, _pc);                                          // replay
        for (int i = 0; i < 200 && _rx.RejectedPackets < 3; i++) await Task.Delay(10);
        Assert.True(_rx.RejectedPackets >= 3);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task SendToPhoneBeforePairingSendsNothing()
    {
        _rx.SendToPhone(new byte[16]);
        Assert.Null(await Recv(200));
    }

    [Fact]
    public async Task SendToPhoneAfterPairingIsSealed()
    {
        await PairAsync();
        await _phone.SendAsync(Control(0), _pc);
        for (int i = 0; i < 200 && !_rx.IsReceiving; i++) await Task.Delay(10);
        var state = new byte[16];
        state[0] = 0xFE;
        _rx.SendToPhone(state);
        var d = await Recv(2000);
        Assert.NotNull(d);
        Assert.True(PacketAuth.TryOpen(Key, PacketAuth.DirPcToPhone, [.. _session.PcSession.ToArray(), .. Nonce], d, out var n, out _));
        Assert.Equal(state, d[..n]);
    }
}
