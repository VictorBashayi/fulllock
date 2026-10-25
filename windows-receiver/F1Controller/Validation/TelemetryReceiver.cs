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
    /// <summary>Where the header fields sit for a given packetFormat.</summary>
    private readonly record struct HeaderLayout(int Size, int PacketIdOffset, int PlayerCarIndexOffset);

    // F1 2021 / F1 22: 24-byte header
    // packetFormat(u16) @0 + gameMajor(u8) @2 + gameMinor(u8) @3 + packetVersion(u8) @4
    // + packetId(u8) @5 + sessionUID(u64) @6 + sessionTime(f32) @14 + frameId(u32) @18
    // + playerCarIndex(u8) @22 + secondaryPlayerCarIndex(u8) @23
    private static readonly HeaderLayout Layout2021 = new(24, 5, 22);

    // F1 23 / F1 24 / F1 25: 29-byte header (adds gameYear and overallFrameId)
    // packetFormat(u16) @0 + gameYear(u8) @2 + gameMajor(u8) @3 + gameMinor(u8) @4
    // + packetVersion(u8) @5 + packetId(u8) @6 + sessionUID(u64) @7 + sessionTime(f32) @15
    // + frameId(u32) @19 + overallFrameId(u32) @23 + playerCarIndex(u8) @27
    // + secondaryPlayerCarIndex(u8) @28
    private static readonly HeaderLayout Layout2023 = new(29, 6, 27);

    private const int MinHeaderSize = 24;
    private const int MaxCars = 22;

    private const byte PacketIdCarTelemetry = 6;

    // Each car telemetry data entry: speed(u16)=2, throttle(f32)=4, steer(f32)=4, brake(f32)=4,
    // clutch(u8)=1, gear(i8)=1, engineRPM(u16)=2, drs(u8)=1, revLightsPercent(u8)=1,
    // revLightsBitValue(u16)=2, brakesTemp[4](u16)=8, tyresSurfaceTemp[4](u8)=4,
    // tyresInnerTemp[4](u8)=4, engineTemp(u16)=2, tyresPressure[4](f32)=16,
    // surfaceType[4](u8)=4
    // Total per car: 2+4+4+4+1+1+2+1+1+2+8+4+4+2+16+4 = 60 bytes
    // (identical in F1 2021 through F1 25)
    private const int CarTelemetryEntrySize = 60;

    /// <summary>packetFormat of the last telemetry packet seen (e.g. 2021, 2024); 0 if none.</summary>
    public int PacketFormat { get; private set; }

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

                if (!TryParseCarTelemetry(result.Buffer, out int format,
                        out float steer, out float throttle, out float brake))
                    continue;

                if (format != PacketFormat)
                {
                    PacketFormat = format;
                    Console.WriteLine($"[Telemetry] Receiving F1 {format} telemetry format.");
                }

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
    private static bool TryParseCarTelemetry(ReadOnlySpan<byte> data, out int packetFormat,
        out float steer, out float throttle, out float brake)
    {
        steer = throttle = brake = 0;
        packetFormat = 0;
        if (data.Length < MinHeaderSize)
            return false;

        // Header layout depends on the game year (see above). F1 2020 and
        // earlier use a different car-telemetry struct and are not supported.
        packetFormat = BinaryPrimitives.ReadUInt16LittleEndian(data);
        HeaderLayout layout;
        if (packetFormat >= 2023) layout = Layout2023;
        else if (packetFormat >= 2021) layout = Layout2021;
        else return false;

        if (data.Length < layout.Size)
            return false;

        byte packetId = data[layout.PacketIdOffset];
        byte playerCarIndex = data[layout.PlayerCarIndexOffset];

        if (packetId != PacketIdCarTelemetry || playerCarIndex >= MaxCars)
            return false;

        // Validate we have enough data for the player car's telemetry
        int playerOffset = layout.Size + playerCarIndex * CarTelemetryEntrySize;
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
