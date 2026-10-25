using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace F1Controller.Validation;

/// <summary>
/// Receives Codemasters F1 game UDP telemetry on port 20777.
/// Parses Car Telemetry packets (ID 6) to extract the player car's
/// steering, throttle, and brake values as the game sees them.
/// </summary>
public sealed class TelemetryReceiver : IDisposable
{
    // F1 23 / F1 24 / F1 25 packet header size
    private const int HeaderSize = 29;
    // packetFormat(u16) @0 + gameYear(u8) @2 + gameMajor(u8) @3 + gameMinor(u8) @4
    // + packetVersion(u8) @5 + packetId(u8) @6 + sessionUID(u64) @7 + sessionTime(f32) @15
    // + frameId(u32) @19 + overallFrameId(u32) @23 + playerCarIndex(u8) @27
    // + secondaryPlayerCarIndex(u8) @28
    private const int PacketIdOffset = 6;
    private const int PlayerCarIndexOffset = 27;
    private const int MaxCars = 22;

    private const byte PacketIdCarTelemetry = 6;

    // Each car telemetry data entry: speed(u16)=2, throttle(f32)=4, steer(f32)=4, brake(f32)=4,
    // clutch(u8)=1, gear(i8)=1, engineRPM(u16)=2, drs(u8)=1, revLightsPercent(u8)=1,
    // revLightsBitValue(u16)=2, brakesTemp[4](u16)=8, tyresSurfaceTemp[4](u8)=4,
    // tyresInnerTemp[4](u8)=4, engineTemp(u16)=2, tyresPressure[4](f32)=16,
    // surfaceType[4](u8)=4
    // Total per car: 2+4+4+4+1+1+2+1+1+2+8+4+4+2+16+4 = 60 bytes
    private const int CarTelemetryEntrySize = 60;

    private readonly int _port;
    private UdpClient? _client;

    /// <summary>Latest player car telemetry values.</summary>
    public float GameSteer { get; private set; }    // -1.0 to 1.0
    public float GameThrottle { get; private set; }  // 0.0 to 1.0
    public float GameBrake { get; private set; }     // 0.0 to 1.0
    public bool HasData { get; private set; }

    public event Action<float, float, float>? OnTelemetryReceived; // steer, throttle, brake

    public TelemetryReceiver(int port = 20777)
    {
        _port = port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _client = new UdpClient(_port);
        _client.Client.ReceiveBufferSize = 65536;
        Console.WriteLine($"[Telemetry] Listening for F1 game telemetry on UDP port {_port}...");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _client.ReceiveAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[Telemetry] Socket error: {ex.Message}");
                    continue;
                }

                if (!TryParseCarTelemetry(result.Buffer, out float steer, out float throttle, out float brake))
                    continue;

                GameSteer = steer;
                GameThrottle = throttle;
                GameBrake = brake;
                HasData = true;

                OnTelemetryReceived?.Invoke(steer, throttle, brake);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    // Spans can't live in an async method (C# 12), so parsing is separate.
    private static bool TryParseCarTelemetry(ReadOnlySpan<byte> data,
        out float steer, out float throttle, out float brake)
    {
        steer = throttle = brake = 0;
        if (data.Length < HeaderSize)
            return false;

        // Parse header (layout above). Older formats (F1 22 and earlier)
        // use a 24-byte header without gameYear and are not supported.
        ushort packetFormat = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (packetFormat < 2023)
            return false;

        byte packetId = data[PacketIdOffset];
        byte playerCarIndex = data[PlayerCarIndexOffset];

        if (packetId != PacketIdCarTelemetry || playerCarIndex >= MaxCars)
            return false;

        // Validate we have enough data for the player car's telemetry
        int playerOffset = HeaderSize + playerCarIndex * CarTelemetryEntrySize;
        if (data.Length < playerOffset + CarTelemetryEntrySize)
            return false;

        var carData = data[playerOffset..];

        // Parse: speed(u16), throttle(f32), steer(f32), brake(f32)
        throttle = BinaryPrimitives.ReadSingleLittleEndian(carData[2..]);
        steer = BinaryPrimitives.ReadSingleLittleEndian(carData[6..]);
        brake = BinaryPrimitives.ReadSingleLittleEndian(carData[10..]);
        return true;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
