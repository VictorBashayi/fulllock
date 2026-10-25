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
    // F1 2023/2024 packet header size
    private const int HeaderSize = 29;
    // packetFormat(u16) + gameMajor(u8) + gameMinor(u8) + packetVersion(u8)
    // + packetId(u8) + sessionUID(u64) + sessionTime(f32)
    // + frameId(u32) + overallFrameId(u32) + playerCarIndex(u8) + secondaryPlayerCarIndex(u8)

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

                var data = result.Buffer.AsSpan();
                if (data.Length < HeaderSize)
                    continue;

                // Parse header
                // ushort packetFormat = BinaryPrimitives.ReadUInt16LittleEndian(data);
                // byte gameMajor = data[2];
                // byte gameMinor = data[3];
                // byte packetVersion = data[4];
                byte packetId = data[5];
                // ulong sessionUID = BinaryPrimitives.ReadUInt64LittleEndian(data[6..]);
                // float sessionTime = BitConverter.ToSingle(data[14..18]);
                // uint frameId = BinaryPrimitives.ReadUInt32LittleEndian(data[18..]);
                // uint overallFrameId = BinaryPrimitives.ReadUInt32LittleEndian(data[22..]);
                byte playerCarIndex = data[26];
                // byte secondaryPlayerCarIndex = data[27];

                if (packetId != PacketIdCarTelemetry)
                    continue;

                // Validate we have enough data for the player car's telemetry
                int playerOffset = HeaderSize + playerCarIndex * CarTelemetryEntrySize;
                if (data.Length < playerOffset + CarTelemetryEntrySize)
                    continue;

                var carData = data[playerOffset..];

                // Parse: speed(u16), throttle(f32), steer(f32), brake(f32)
                // ushort speed = BinaryPrimitives.ReadUInt16LittleEndian(carData);
                float throttle = BitConverter.ToSingle(carData[2..6]);
                float steer = BitConverter.ToSingle(carData[6..10]);
                float brake = BitConverter.ToSingle(carData[10..14]);

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

    public void Dispose()
    {
        _client?.Dispose();
    }
}
