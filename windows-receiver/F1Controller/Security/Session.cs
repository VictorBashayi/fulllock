using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using F1Controller.Transport;

namespace F1Controller.Security;

public enum AcceptResult { Rejected, Ping, Control, ControlNewPhone }

/// <summary>Gatekeeper for the paired control port: decides which datagrams are authentic,
/// which phone is active, and seals everything the PC sends (spec §3.3).</summary>
public sealed class Session
{
    public const int NonceSize = 8, PingPayloadSize = 25, PongPayloadSize = 24;
    const int PingDatagramSize = PingPayloadSize + PacketAuth.TrailerSize;
    const int ControlDatagramSize = ControlPacket.Size + PacketAuth.TrailerSize;
    const int MaxPending = 4;
    const int PingNonceOffset = 17, PingEchoSize = 16;

    readonly byte[] _key;
    readonly byte[] _pcSession;
    readonly object _lock = new();
    readonly List<ulong> _pending = []; // most recent last
    // Last accepted ctr per nonce, kept after a nonce is dropped so a replayed ping + old
    // control of a former phone can't re-enter at ctr 0. Grows by one per phone (re)connect only.
    readonly Dictionary<ulong, uint> _lastCtr = [];
    ulong? _active;
    uint _sendCtr = 1;
    IPEndPoint? _activeEndPoint;
    bool _hasPaired;
    long _rejected;

    public Session(byte[] key) : this(key, RandomNumberGenerator.GetBytes(NonceSize)) { }

    public Session(byte[] key, byte[] pcSession)
    {
        if (key is not { Length: 32 }) throw new ArgumentException("key must be 32 bytes", nameof(key));
        if (pcSession is not { Length: NonceSize }) throw new ArgumentException("pcSession must be 8 bytes", nameof(pcSession));
        _key = (byte[])key.Clone();
        _pcSession = (byte[])pcSession.Clone();
    }

    public ReadOnlyMemory<byte> PcSession => _pcSession;
    public IPEndPoint? ActiveEndPoint { get { lock (_lock) return _activeEndPoint; } }
    public bool HasPaired { get { lock (_lock) return _hasPaired; } }
    public long RejectedPackets { get { lock (_lock) return _rejected; } }
    public event Action? Paired;

    void Ctx(ulong nonce, Span<byte> ctx)
    {
        _pcSession.CopyTo(ctx);
        BinaryPrimitives.WriteUInt64LittleEndian(ctx[NonceSize..], nonce);
    }

    public AcceptResult Accept(ReadOnlySpan<byte> datagram, IPEndPoint from, out int payloadLength)
    {
        payloadLength = 0;
        bool raisePaired = false;
        AcceptResult result;
        lock (_lock)
        {
            result = datagram.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(datagram) == ControlPacket.PingMarker
                ? AcceptPing(datagram, out payloadLength)
                : AcceptControl(datagram, from, out payloadLength);
            if (result == AcceptResult.Rejected) _rejected++;
            else if (result == AcceptResult.ControlNewPhone && !_hasPaired) raisePaired = _hasPaired = true;
        }
        if (raisePaired) Paired?.Invoke();
        return result;
    }

    AcceptResult AcceptPing(ReadOnlySpan<byte> d, out int n)
    {
        if (d.Length != PingDatagramSize || !PacketAuth.TryOpen(_key, PacketAuth.DirPhoneToPc, [], d, out n, out _))
        {
            n = 0;
            return AcceptResult.Rejected;
        }
        ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(d[PingNonceOffset..]);
        _pending.Remove(nonce);
        _pending.Add(nonce);
        if (_pending.Count > MaxPending) _pending.RemoveAt(0);
        return AcceptResult.Ping;
    }

    AcceptResult AcceptControl(ReadOnlySpan<byte> d, IPEndPoint from, out int n)
    {
        n = 0;
        if (d.Length != ControlDatagramSize) return AcceptResult.Rejected;
        Span<byte> ctx = stackalloc byte[PacketAuth.ContextSize];
        for (int i = -1; i < _pending.Count; i++)
        {
            ulong nonce;
            if (i < 0) { if (_active is not { } a) continue; nonce = a; }
            else nonce = _pending[_pending.Count - 1 - i];
            Ctx(nonce, ctx);
            if (!PacketAuth.TryOpen(_key, PacketAuth.DirPhoneToPc, ctx, d, out int len, out uint ctr)) continue;
            // A tag matches only one nonce, so a stale ctr here is a replay: stop searching.
            if (ctr <= _lastCtr.GetValueOrDefault(nonce)) return AcceptResult.Rejected;
            _lastCtr[nonce] = ctr;
            _activeEndPoint = from;
            n = len;
            if (nonce == _active) return AcceptResult.Control;
            _pending.Remove(nonce);
            _active = nonce;
            return AcceptResult.ControlNewPhone;
        }
        return AcceptResult.Rejected;
    }

    public byte[] BuildPong(ReadOnlySpan<byte> pingPayload)
    {
        if (pingPayload.Length != PingPayloadSize) throw new ArgumentException("ping payload must be 25 bytes", nameof(pingPayload));
        Span<byte> pong = stackalloc byte[PongPayloadSize];
        pingPayload[..PingEchoSize].CopyTo(pong);
        _pcSession.CopyTo(pong[PingEchoSize..]);
        Span<byte> ctx = stackalloc byte[PacketAuth.ContextSize];
        Ctx(BinaryPrimitives.ReadUInt64LittleEndian(pingPayload[PingNonceOffset..]), ctx);
        var dest = new byte[PongPayloadSize + PacketAuth.TrailerSize];
        lock (_lock) PacketAuth.Seal(_key, PacketAuth.DirPcToPhone, ctx, pong, _sendCtr++, dest);
        return dest;
    }

    public byte[]? SealForActive(ReadOnlySpan<byte> payload)
    {
        Span<byte> ctx = stackalloc byte[PacketAuth.ContextSize];
        var dest = new byte[payload.Length + PacketAuth.TrailerSize];
        lock (_lock)
        {
            if (_active is not { } nonce) return null;
            Ctx(nonce, ctx);
            PacketAuth.Seal(_key, PacketAuth.DirPcToPhone, ctx, payload, _sendCtr++, dest);
        }
        return dest;
    }
}
