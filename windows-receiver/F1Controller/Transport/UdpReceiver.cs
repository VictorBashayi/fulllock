using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace F1Controller.Transport;

/// <summary>
/// High-performance UDP receiver for phone controller packets.
/// Handles control packets (18 bytes) and ping/pong RTT measurement (16 bytes).
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
    public string? RemoteAddress { get; private set; }

    private volatile int _engineerLevel = 2;
    /// <summary>The phone's engineer chattiness from its latest ping (see <see cref="ControlPacket.ReadPingLevel"/>).</summary>
    public int EngineerLevel { get => _engineerLevel; private set => _engineerLevel = value; }

    // Where the phone's packets come from; telemetry is sent back here so it
    // reuses the same socket (and NAT/firewall path) as the pongs.
    private volatile IPEndPoint? _phoneEndPoint;
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

    public UdpReceiver(int port, bool logTransport)
    {
        _port = port;
        _logTransport = logTransport;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _client = new UdpClient(_port);
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
                RemoteAddress = result.RemoteEndPoint.Address.ToString();
                _phoneEndPoint = result.RemoteEndPoint;

                // Ping/pong: echo back immediately for RTT measurement
                if (ControlPacket.IsPing(data) && data.Length >= ControlPacket.PingSize)
                {
                    EngineerLevel = ControlPacket.ReadPingLevel(data);
                    try
                    {
                        await _client.SendAsync(data.AsMemory(0, ControlPacket.PingSize),
                            result.RemoteEndPoint, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { }
                    continue;
                }

                if (data.Length < ControlPacket.Size)
                    continue;

                var packet = ControlPacket.Parse(data);

                // Sequence tracking for packet loss. Signed 32-bit delta handles
                // wrap-around; late/duplicate packets (delta <= 0) are dropped so a
                // stale value can't overwrite newer input; a large jump means the
                // phone reconnected and restarted its counter.
                if (_lastSeq is uint last)
                {
                    int delta = unchecked((int)(packet.Sequence - last));
                    if (delta <= 0 && delta > -1000)
                        continue;
                    if (delta > 0 && delta < 1000)
                    {
                        _packetsLost += delta - 1;
                        _totalExpected += delta;
                    }
                    else
                    {
                        _totalExpected += 1; // counter reset
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
        var phone = _phoneEndPoint;
        if (client == null || phone == null || !IsReceiving)
            return;
        try
        {
            client.Send(data, data.Length, phone);
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
