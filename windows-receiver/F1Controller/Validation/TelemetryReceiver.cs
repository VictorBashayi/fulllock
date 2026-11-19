using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using F1Controller.Engineer;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace F1Controller.Validation;

/// <summary>
/// Receives Codemasters F1 game UDP telemetry on port 20777.
/// Parses Car Telemetry (ID 6) and Car Status (ID 7) packets for the
/// player car: the inputs as the game sees them (for validation) plus
/// gear, revs, DRS and ERS (forwarded to the phone's dashboard).
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

    private const byte PacketIdSession = 1;
    private const byte PacketIdLapData = 2;
    private const byte PacketIdEvent = 3;
    private const byte PacketIdCarTelemetry = 6;
    private const byte PacketIdCarStatus = 7;
    private const byte PacketIdCarDamage = 10;

    // Car damage entry, F1 2021 (39 bytes): tyresWear[4](f32) @0, then damage bytes.
    private const int CarDamageEntrySize2021 = 39;

    // Wheel arrays in every packet are ordered RL, RR, FL, FR; the phone wants FL, FR, RL, RR.
    private static readonly int[] WheelOrder = { 2, 3, 0, 1 };

    private static readonly TimeSpan RaceSendInterval = TimeSpan.FromMilliseconds(100);

    // Each car telemetry data entry: speed(u16)=2, throttle(f32)=4, steer(f32)=4, brake(f32)=4,
    // clutch(u8)=1, gear(i8)=1, engineRPM(u16)=2, drs(u8)=1, revLightsPercent(u8)=1,
    // revLightsBitValue(u16)=2, brakesTemp[4](u16)=8, tyresSurfaceTemp[4](u8)=4,
    // tyresInnerTemp[4](u8)=4, engineTemp(u16)=2, tyresPressure[4](f32)=16,
    // surfaceType[4](u8)=4
    // Total per car: 2+4+4+4+1+1+2+1+1+2+8+4+4+2+16+4 = 60 bytes
    // (identical in F1 2021 through F1 25)
    private const int CarTelemetryEntrySize = 60;

    // Car status entry, F1 2021 / F1 22 (47 bytes): tractionControl @0, antiLockBrakes @1,
    // fuelMix @2, frontBrakeBias @3, pitLimiterStatus @4, fuelInTank(f32) @5,
    // fuelCapacity(f32) @9, fuelRemainingLaps(f32) @13, maxRPM(u16) @17, idleRPM(u16) @19,
    // maxGears @21, drsAllowed @22, drsActivationDistance(u16) @23, actualTyreCompound @25,
    // visualTyreCompound @26, tyresAgeLaps @27, vehicleFiaFlags(i8) @28,
    // ersStoreEnergy(f32) @29, ersDeployMode @33, ersHarvested MGUK/MGUH, ersDeployed,
    // networkPaused.
    // F1 23+ inserts enginePowerICE(f32) + enginePowerMGUK(f32) after vehicleFiaFlags,
    // pushing the ERS fields back 8 bytes (55-byte entry).
    private const int CarStatusEntrySize2021 = 47;
    private const int CarStatusEntrySize2023 = 55;

    /// <summary>Full ERS battery in joules (4 MJ).</summary>
    private const float ErsMaxJoules = 4_000_000f;

    /// <summary>packetFormat of the last telemetry packet seen (e.g. 2021, 2024); 0 if none.</summary>
    public int PacketFormat { get; private set; }

    private readonly int _port;
    private UdpClient? _client;

    /// <summary>Latest player car telemetry values.</summary>
    public float GameSteer { get; private set; }    // -1.0 to 1.0
    public float GameThrottle { get; private set; }  // 0.0 to 1.0
    public float GameBrake { get; private set; }     // 0.0 to 1.0
    public bool HasData { get; private set; }

    /// <summary>Dashboard values for the phone; combines the latest telemetry and status packets.</summary>
    public GameState State { get; private set; }

    public event Action<float, float, float>? OnTelemetryReceived; // steer, throttle, brake

    /// <summary>Fires whenever <see cref="State"/> changes.</summary>
    public event Action<GameState>? OnStateChanged;

    // ---- Race dashboard (F1 2021 only: lap data and damage layouts differ in later games) ----

    private readonly RaceTracker _race = new();
    private byte _totalLaps;
    private byte _tyreCompound;   // visual: 16 soft, 17 medium, 18 hard, 7 inter, 8 wet
    private byte _tyreAgeLaps;
    private float _fuelLaps;
    private readonly byte[] _tyreWear = new byte[4];  // %, FL FR RL RR
    private readonly byte[] _tyreTemp = new byte[4];  // surface °C, FL FR RL RR
    private readonly byte[] _tyreInnerTemp = new byte[4];  // inner °C, FL FR RL RR
    private long _lastRaceSend;

    // Engineer snapshot inputs (F1 2021 only)
    private F1Packets2021.SessionInfo? _session;
    private sbyte _fiaFlag = -1;
    private Damage _damage;
    private readonly List<GameEvent> _pendingEvents = new();

    /// <summary>Fires after every 2021 Lap Data packet (not throttled).</summary>
    public event Action<RaceSnapshot>? OnSnapshot;

    /// <summary>Fires at most every 100 ms while lap data arrives.</summary>
    public event Action<RaceState>? OnRaceStateChanged;

    private readonly PbStore? _pbs;

    public TelemetryReceiver(int port = 20777, PbStore? pbs = null)
    {
        _port = port;
        _pbs = pbs;
        _race.OnPlayerLapCompleted += OnLapCompleted;
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

                HandleSafely(result.Buffer);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    private readonly HashSet<string> _loggedErrors = new();

    /// <summary>Handles one datagram; a bad packet or throwing subscriber must not kill the receive loop.</summary>
    public bool HandleSafely(byte[] buffer)
    {
        try
        {
            Handle(buffer);
            return true;
        }
        catch (Exception ex)
        {
            if (_loggedErrors.Add(ex.Message))
                Console.Error.WriteLine($"[Telemetry] Packet handling failed: {ex.Message}");
            return false;
        }
    }

    private void Handle(byte[] buffer)
    {
        if (!TryParseHeader(buffer, out int format, out byte packetId, out int headerSize, out byte playerCarIndex))
            return;

        if (format != PacketFormat)
        {
            PacketFormat = format;
            Console.WriteLine($"[Telemetry] Receiving F1 {format} telemetry format.");
        }

        if (packetId == PacketIdCarTelemetry)
        {
            int offset = headerSize + playerCarIndex * CarTelemetryEntrySize;
            if (buffer.Length < offset + CarTelemetryEntrySize)
                return;

            var car = buffer.AsSpan(offset, CarTelemetryEntrySize);
            GameThrottle = BinaryPrimitives.ReadSingleLittleEndian(car[2..]);
            GameSteer = BinaryPrimitives.ReadSingleLittleEndian(car[6..]);
            GameBrake = BinaryPrimitives.ReadSingleLittleEndian(car[10..]);
            HasData = true;

            State = State with
            {
                SpeedKph = BinaryPrimitives.ReadUInt16LittleEndian(car),
                Gear = (sbyte)car[15],
                Rpm = BinaryPrimitives.ReadUInt16LittleEndian(car[16..]),
                DrsOpen = car[18] != 0,
                RevLightsPercent = car[19],
            };
            for (int w = 0; w < 4; w++)
            {
                _tyreTemp[w] = car[30 + WheelOrder[w]];
                _tyreInnerTemp[w] = car[34 + WheelOrder[w]];
            }

            OnTelemetryReceived?.Invoke(GameSteer, GameThrottle, GameBrake);
            OnStateChanged?.Invoke(State);
        }
        else if (packetId == PacketIdCarStatus)
        {
            bool newLayout = format >= 2023;
            int entrySize = newLayout ? CarStatusEntrySize2023 : CarStatusEntrySize2021;
            int ersOffset = newLayout ? 37 : 29;

            int offset = headerSize + playerCarIndex * entrySize;
            if (buffer.Length < offset + entrySize)
                return;

            var car = buffer.AsSpan(offset, entrySize);
            float ersJoules = BinaryPrimitives.ReadSingleLittleEndian(car[ersOffset..]);

            State = State with
            {
                PitLimiter = car[4] != 0,
                DrsAllowed = car[22] != 0,
                ErsPercent = (byte)Math.Clamp(ersJoules / ErsMaxJoules * 100f, 0f, 100f),
                ErsMode = car[ersOffset + 4],
            };
            _fuelLaps = BinaryPrimitives.ReadSingleLittleEndian(car[13..]);
            _tyreCompound = car[26];
            _tyreAgeLaps = car[27];
            if (format == 2021)
                _fiaFlag = F1Packets2021.FiaFlag(car);
            OnStateChanged?.Invoke(State);
        }
        else if (packetId == PacketIdSession)
        {
            // weather @0, trackTemperature @1, airTemperature @2, totalLaps @3
            if (format == 2021)
            {
                var session = F1Packets2021.ParseSession(buffer.AsSpan(headerSize));
                if (session is { } info)
                {
                    _session = info;
                    _totalLaps = info.TotalLaps;
                }
            }
            else if (buffer.Length > headerSize + 3)
                _totalLaps = buffer[headerSize + 3];
        }
        else if (packetId == PacketIdEvent && format == 2021)
        {
            var ev = F1Packets2021.ParseEvent(buffer.AsSpan(headerSize), playerCarIndex);
            if (ev is { } gameEvent)
                _pendingEvents.Add(gameEvent);
        }
        else if (packetId == PacketIdCarDamage && format == 2021)
        {
            int offset = headerSize + playerCarIndex * CarDamageEntrySize2021;
            if (buffer.Length < offset + CarDamageEntrySize2021)
                return;
            for (int w = 0; w < 4; w++)
            {
                float wear = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(offset + WheelOrder[w] * 4));
                _tyreWear[w] = (byte)Math.Clamp(wear, 0f, 100f);
            }
            _damage = F1Packets2021.ParseDamage(buffer.AsSpan(offset, CarDamageEntrySize2021));
        }
        else if (packetId == PacketIdLapData && format == 2021)
        {
            float sessionTime = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(14));
            _race.Process(buffer, headerSize, playerCarIndex, sessionTime);

            if (Stopwatch.GetElapsedTime(_lastRaceSend) >= RaceSendInterval)
            {
                _lastRaceSend = Stopwatch.GetTimestamp();
                OnRaceStateChanged?.Invoke(CurrentRaceState());
            }

            // Always drain pending events so they never outlive one Lap Data packet.
            var events = _pendingEvents.ToArray();
            _pendingEvents.Clear();

            if (OnSnapshot is { } onSnapshot)
            {
                ulong sessionUid = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(6));
                var session = _session;
                onSnapshot(new RaceSnapshot(
                    sessionTime, sessionUid, CurrentRaceState(),
                    session is { SessionType: 10 or 11 },
                    session?.Weather ?? 0, session?.RainInMinutes,
                    session?.SafetyCar ?? 0, _fiaFlag, _damage, events));
            }
        }
    }

    private const byte SessionTypeTimeTrial = 12;   // F1 2021
    private const byte CompoundFullWet = 8;          // visual compound

    private void OnLapCompleted(uint lapMs)
    {
        if (_session is { TrackId: >= 0 } s)
            _pbs?.TryRecord(s.TrackId, s.SessionType, _tyreCompound == CompoundFullWet, lapMs);
    }

    private RaceState CurrentRaceState()
    {
        sbyte track = _session?.TrackId ?? -1;
        return new(
            _race.Info, _totalLaps, _tyreCompound, _tyreAgeLaps, _fuelLaps,
            (byte[])_tyreWear.Clone(), (byte[])_tyreTemp.Clone(), (byte[])_tyreInnerTemp.Clone(),
            track,
            track >= 0 && _pbs != null ? _pbs.Get(track, SessionTypeTimeTrial, false) : 0,
            track >= 0 && _pbs != null ? _pbs.Get(track, SessionTypeTimeTrial, true) : 0);
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> data, out int packetFormat,
        out byte packetId, out int headerSize, out byte playerCarIndex)
    {
        packetFormat = 0;
        packetId = 0;
        headerSize = 0;
        playerCarIndex = 0;
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

        packetId = data[layout.PacketIdOffset];
        playerCarIndex = data[layout.PlayerCarIndexOffset];
        headerSize = layout.Size;
        return playerCarIndex < MaxCars;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

/// <summary>
/// Timing and car condition for the phone's Car and Timing views (and the engineer). Tyre arrays
/// are FL, FR, RL, RR; TyreTemp is the surface, TyreInnerTemp the inner (carcass) temperature.
/// </summary>
public sealed record RaceState(
    RaceInfo Timing,
    byte TotalLaps,
    byte TyreCompound,
    byte TyreAgeLaps,
    float FuelLaps,
    byte[] TyreWear,
    byte[] TyreTemp,
    byte[] TyreInnerTemp,
    sbyte TrackId = -1,
    uint TtDryMs = 0,
    uint TtWetMs = 0);

/// <summary>Player car values shown on the phone. ErsMode: 0 none, 1 medium, 2 hotlap, 3 overtake.</summary>
public readonly record struct GameState(
    sbyte Gear,
    byte RevLightsPercent,
    bool DrsAllowed,
    bool DrsOpen,
    ushort SpeedKph,
    ushort Rpm,
    byte ErsPercent,
    byte ErsMode,
    bool PitLimiter);
