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
    private readonly int _port;
    private readonly bool _logTransport;
    private UdpClient? _client;
    private StreamWriter? _csvWriter;

    // --- Metrics (read from dashboard thread) ---
    private long _packetsReceived;
    private long _packetsLost;
    private long _lastSeq = -1;
    private long _totalExpected;
    private double _jitterUs;
    private long _lastArrivalTicks;
    private double _lastIntervalUs;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    // RTT tracking (populated from pong responses — phone sends pings, we echo)
    // The phone computes its own RTT; we track per-packet processing latency instead.

    public int PacketsPerSecond { get; private set; }
    public double PacketLossPercent => _totalExpected > 0
        ? _packetsLost * 100.0 / _totalExpected
        : 0;
    public double JitterUs => _jitterUs;
    public string? RemoteAddress { get; private set; }
    public ControlPacket LastPacket { get; private set; }
    public bool IsReceiving => _packetsReceived > 0;

    // Fires for each valid control packet
    public event Action<ControlPacket, long>? OnPacketReceived; // packet + receive ticks

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
        _client.Client.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        if (_logTransport)
        {
            var logPath = $"transport_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            _csvWriter = new StreamWriter(logPath, append: false);
            _csvWriter.WriteLine("timestamp,seq,jitter_us,loss_pct,pps");
        }

        _ppsWindowStart = _sw.ElapsedMilliseconds;
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

                long receiveTicks = _sw.ElapsedTicks;
                var data = result.Buffer;
                RemoteAddress = result.RemoteEndPoint.Address.ToString();

                // Ping/pong: echo back immediately for RTT measurement
                if (ControlPacket.IsPing(data) && data.Length >= ControlPacket.PingSize)
                {
                    try
                    {
                        await _client.SendAsync(data.AsMemory(0, ControlPacket.PingSize),
                            result.RemoteEndPoint, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (data.Length < ControlPacket.Size)
                    continue;

                var packet = ControlPacket.Parse(data);
                LastPacket = packet;

                // Sequence tracking for packet loss
                if (_lastSeq >= 0)
                {
                    long expected = packet.Sequence - (uint)_lastSeq;
                    if (expected > 1)
                        _packetsLost += expected - 1;
                    _totalExpected += expected;
                }
                else
                {
                    _totalExpected = 1;
                }
                _lastSeq = packet.Sequence;
                _packetsReceived++;

                // Jitter calculation (RFC 3550 style: running average of interval deviation)
                if (_lastArrivalTicks > 0)
                {
                    double intervalUs = (receiveTicks - _lastArrivalTicks)
                        * 1_000_000.0 / Stopwatch.Frequency;
                    double deviation = Math.Abs(intervalUs - _lastIntervalUs);
                    _jitterUs += (deviation - _jitterUs) / 16.0; // exponential smoothing
                    _lastIntervalUs = intervalUs;
                }
                else
                {
                    _lastIntervalUs = 4000; // assume ~4ms initial interval (250 Hz)
                }
                _lastArrivalTicks = receiveTicks;

                // PPS counter
                _ppsCounter++;
                long now = _sw.ElapsedMilliseconds;
                if (now - _ppsWindowStart >= 1000)
                {
                    PacketsPerSecond = _ppsCounter;
                    _ppsCounter = 0;
                    _ppsWindowStart = now;

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

    public void Dispose()
    {
        _csvWriter?.Dispose();
        _client?.Dispose();
    }
}
