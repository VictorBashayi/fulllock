using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using F1Controller.Security;

namespace F1Controller.Transport;

/// <summary>
/// High-performance UDP receiver for phone controller packets.
/// Every datagram must pass <see cref="Session.Accept"/> (sealed control = 18 B payload,
/// ping = 25 B payload); pongs and telemetry to the phone are sealed too.
/// </summary>
public sealed class UdpReceiver : IDisposable
{
    /// <summary>No control packet for this long → phone considered gone.</summary>
    public static readonly TimeSpan LinkTimeout = TimeSpan.FromMilliseconds(250);

    // Windows: stop ICMP "port unreachable" from surfacing as WSAECONNRESET
    // on the next receive after a pong is sent to a phone that went away.
    private const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);

    private readonly int _port;
    private readonly bool _logTransport;
    private readonly Session _session;
    private UdpClient? _client;
    private StreamWriter? _csvWriter;

    // --- Metrics (read from dashboard thread) ---
    private long _packetsReceived;
    private long _packetsLost;
    private uint? _lastSeq;
    private long _totalExpected;
    private double _jitterUs;
    private long _lastArrivalTicks;
    private double _lastIntervalUs;

    // RTT tracking (populated from pong responses — phone sends pings, we echo)
    // The phone computes its own RTT; we track per-packet processing latency instead.

    public int PacketsPerSecond { get; private set; }
    public double PacketLossPercent => _totalExpected > 0
        ? _packetsLost * 100.0 / _totalExpected
        : 0;
    public double JitterUs => _jitterUs;
    public string? RemoteAddress => _session.ActiveEndPoint?.Address.ToString();
    public Session Session => _session;
    public long RejectedPackets => _session.RejectedPackets;
    /// <summary>Actual UDP port once <see cref="RunAsync"/> has bound the socket (0 before).</summary>
    public int BoundPort => _boundPort;
    private volatile int _boundPort;

    private volatile int _engineerLevel = 2;
    /// <summary>The phone's engineer chattiness from its latest ping (see <see cref="ControlPacket.ReadPingLevel"/>).</summary>
    public int EngineerLevel { get => _engineerLevel; private set => _engineerLevel = value; }

    public ControlPacket LastPacket { get; private set; }

    /// <summary>True while control packets keep arriving (within <see cref="LinkTimeout"/>).</summary>
    public bool IsReceiving
    {
        get
        {
            long last = Interlocked.Read(ref _lastArrivalTicks);
            return last != 0 && Stopwatch.GetElapsedTime(last) < LinkTimeout;
        }
    }

    // Fires for each valid control packet
    public event Action<ControlPacket, long>? OnPacketReceived; // packet + Stopwatch.GetTimestamp()

    // PPS counter
    private int _ppsCounter;
    private long _ppsWindowStart;

    public UdpReceiver(int port, bool logTransport, Session session)
    {
        _session = session;
        _port = port;
        _logTransport = logTransport;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _client = new UdpClient(_port);
        _boundPort = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
        _client.Client.ReceiveBufferSize = 65536; // 64KB receive buffer
        if (OperatingSystem.IsWindows())
            _client.Client.IOControl(SIO_UDP_CONNRESET, new byte[4], null);

        if (_logTransport)
        {
            var logPath = AppPaths.Default.LogFile("transport_log", "csv");
            _csvWriter = new StreamWriter(logPath, append: false);
            _csvWriter.WriteLine("timestamp,seq,jitter_us,loss_pct,pps");
        }

        _ppsWindowStart = Stopwatch.GetTimestamp();
        Console.WriteLine($"[Transport] Listening on UDP port {_port}...");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _client.ReceiveAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[Transport] Socket error: {ex.Message}");
                    continue;
                }

                long receiveTicks = Stopwatch.GetTimestamp();
                var data = result.Buffer;

                var accepted = _session.Accept(data, result.RemoteEndPoint, out int payloadLength);
                if (accepted == AcceptResult.Rejected)
                    continue;
                var payload = data[..payloadLength]; // array, not Span: this is an async method

                // Ping/pong: sealed pong back for RTT measurement
                if (accepted == AcceptResult.Ping)
                {
                    EngineerLevel = ControlPacket.ReadPingLevel(payload);
                    try
                    {
                        var pong = _session.BuildPong(payload);
                        await _client.SendAsync(pong, result.RemoteEndPoint, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { }
                    catch (InvalidOperationException) { } // PC send counter exhausted: skip the pong
                    continue;
                }

                var packet = ControlPacket.Parse(payload);

                // The session already rejects stale/replayed datagrams, so sequence
                // numbers only move forward within one phone; a new phone starts fresh.
                if (accepted == AcceptResult.ControlNewPhone)
                    _lastSeq = null;
                if (_lastSeq is uint last)
                {
                    int delta = unchecked((int)(packet.Sequence - last));
                    if (delta > 0)
                    {
                        _packetsLost += delta - 1;
                        _totalExpected += delta;
                    }
                    else
                    {
                        _totalExpected += 1; // phone restarted its counter under the same nonce
                    }
                }
                else
                {
                    _totalExpected = 1;
                }
                _lastSeq = packet.Sequence;
                _packetsReceived++;
                LastPacket = packet;

                // Jitter calculation (RFC 3550 style: running average of interval deviation)
                long prevArrival = _lastArrivalTicks;
                if (prevArrival > 0)
                {
                    double intervalUs = Stopwatch.GetElapsedTime(prevArrival, receiveTicks).TotalMicroseconds;
                    double deviation = Math.Abs(intervalUs - _lastIntervalUs);
                    _jitterUs += (deviation - _jitterUs) / 16.0; // exponential smoothing
                    _lastIntervalUs = intervalUs;
                }
                else
                {
                    _lastIntervalUs = 4000; // assume ~4ms initial interval (250 Hz)
                }
                Interlocked.Exchange(ref _lastArrivalTicks, receiveTicks);

                // PPS counter
                _ppsCounter++;
                if (Stopwatch.GetElapsedTime(_ppsWindowStart, receiveTicks).TotalMilliseconds >= 1000)
                {
                    PacketsPerSecond = _ppsCounter;
                    _ppsCounter = 0;
                    _ppsWindowStart = receiveTicks;

                    // CSV log once per second
                    _csvWriter?.WriteLine(
                        $"{DateTime.UtcNow:O},{packet.Sequence},{_jitterUs:F1},{PacketLossPercent:F2},{PacketsPerSecond}");
                    _csvWriter?.Flush();
                }

                OnPacketReceived?.Invoke(packet, receiveTicks);
            }
        }
        finally
        {
            _csvWriter?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>Sends a datagram to the phone, if one is connected. Safe to call from any thread.</summary>
    public void SendToPhone(byte[] data)
    {
        var client = _client;
        var phone = _session.ActiveEndPoint;
        if (client == null || phone == null || !IsReceiving)
            return;
        try
        {
            var sealedData = _session.SealForActive(data);
            if (sealedData != null)
                client.Send(sealedData, sealedData.Length, phone);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _csvWriter?.Dispose();
        _client?.Dispose();
    }
}
