using System;
using System.Buffers.Binary;

namespace F1Controller.Transport;

/// <summary>
/// 18-byte binary control packet from the phone.
/// Layout (little-endian): [seq:u32][timestamp_us:u64][steer:i16][throttle:u8][brake:u8][buttons:u16]
/// </summary>
public readonly record struct ControlPacket(
    uint Sequence,
    ulong TimestampUs,
    short Steer,
    byte Throttle,
    byte Brake,
    ushort Buttons)
{
    public const int Size = 18;

    /// <summary>Ping packets start with 0xFFFFFFFF.</summary>
    public const uint PingMarker = 0xFFFFFFFF;
    public const int PingSize = 16; // [marker:u32][seq:u32][timestamp_us:u64]; echoed back as is

    /// <summary>
    /// The phone's engineer chattiness (0 Off, 1 Quiet, 2 Balanced, 3 Talkative), sent as a
    /// 17th ping byte. Older apps send 16-byte pings: those, and any value out of range, mean Balanced.
    /// </summary>
    public static int ReadPingLevel(ReadOnlySpan<byte> ping) =>
        ping.Length > PingSize && ping[PingSize] <= 3 ? ping[PingSize] : 2;

    /// <summary>
    /// Game state sent back to the phone (16 bytes, little-endian):
    /// [marker:u32][gear:i8][revLightsPct:u8][drsAllowed:u8][drsOpen:u8]
    /// [speedKph:u16][rpm:u16][ersPct:u8][ersMode:u8][pitLimiter:u8][reserved:u8]
    /// </summary>
    public const uint GameStateMarker = 0xFFFFFFFE;
    public const int GameStateSize = 16;

    public static void WriteGameState(Span<byte> dest, in Validation.GameState s)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dest, GameStateMarker);
        dest[4] = unchecked((byte)s.Gear);
        dest[5] = s.RevLightsPercent;
        dest[6] = s.DrsAllowed ? (byte)1 : (byte)0;
        dest[7] = s.DrsOpen ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(dest[8..], s.SpeedKph);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[10..], s.Rpm);
        dest[12] = s.ErsPercent;
        dest[13] = s.ErsMode;
        dest[14] = s.PitLimiter ? (byte)1 : (byte)0;
        dest[15] = 0;
    }

    /// <summary>
    /// Race timing and car condition sent to the phone ~10 Hz (54 bytes, little-endian):
    /// [marker:u32][position:u8][totalLaps:u8][currentLap:u8][sector:u8][lapInvalid:u8]
    /// [compound:u8][tyreAge:u8][flags:u8 (1=delta, 2=gapAhead, 4=gapBehind valid)]
    /// [currentLapMs:u32][lastLapMs:u32][bestLapMs:u32][deltaMs:i32][s1Ms:u16][s2Ms:u16]
    /// [s1Status:u8][s2Status:u8][fuelLaps×100:i16][gapAheadMs:i32][gapBehindMs:i32]
    /// [tyreWear%:u8×4][tyreTempC:u8×4][reserved:u16]   (tyres FL, FR, RL, RR)
    /// </summary>
    public const uint RaceStateMarker = 0xFFFFFFFD;
    public const int RaceStateSize = 54;

    public static void WriteRaceState(Span<byte> dest, Validation.RaceState s)
    {
        var t = s.Timing;
        BinaryPrimitives.WriteUInt32LittleEndian(dest, RaceStateMarker);
        dest[4] = t.Position;
        dest[5] = s.TotalLaps;
        dest[6] = t.CurrentLap;
        dest[7] = t.Sector;
        dest[8] = t.LapInvalid ? (byte)1 : (byte)0;
        dest[9] = s.TyreCompound;
        dest[10] = s.TyreAgeLaps;
        dest[11] = (byte)((t.DeltaMs.HasValue ? 1 : 0) | (t.GapAheadMs.HasValue ? 2 : 0) | (t.GapBehindMs.HasValue ? 4 : 0));
        BinaryPrimitives.WriteUInt32LittleEndian(dest[12..], t.CurrentLapMs);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[16..], t.LastLapMs);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[20..], t.BestLapMs);
        BinaryPrimitives.WriteInt32LittleEndian(dest[24..], t.DeltaMs ?? 0);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[28..], t.S1);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[30..], t.S2);
        dest[32] = (byte)t.S1Status;
        dest[33] = (byte)t.S2Status;
        BinaryPrimitives.WriteInt16LittleEndian(dest[34..], (short)Math.Clamp(s.FuelLaps * 100f, short.MinValue, short.MaxValue));
        BinaryPrimitives.WriteInt32LittleEndian(dest[36..], t.GapAheadMs ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(dest[40..], t.GapBehindMs ?? 0);
        s.TyreWear.AsSpan(0, 4).CopyTo(dest[44..]);
        s.TyreTemp.AsSpan(0, 4).CopyTo(dest[48..]);
        dest[52] = 0;
        dest[53] = 0;
    }

    /// <summary>
    /// Race engineer radio call sent to the phone (9-byte header + text, little-endian):
    /// [marker:u32][callId:u16][priority:u8][category:u8][textLen:u8][UTF-8 text, max 120 bytes]
    /// Text longer than 120 bytes is truncated on a UTF-8 character boundary.
    /// </summary>
    public const uint RadioMarker = 0xFFFFFFFC;
    public const int RadioMaxText = 120;
    public const int RadioHeaderSize = 9;

    /// <summary>Writes a radio call into dest (must be at least RadioHeaderSize + RadioMaxText) and returns the total bytes written.</summary>
    public static int WriteRadioCall(Span<byte> dest, ushort callId, Engineer.RadioCall call)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(call.Text);
        int len = Math.Min(bytes.Length, RadioMaxText);
        if (len < bytes.Length)
        {
            // Step back off any UTF-8 continuation bytes so we never split a character.
            while (len > 0 && (bytes[len] & 0xC0) == 0x80) len--;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(dest, RadioMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], callId);
        dest[6] = (byte)call.Priority;
        dest[7] = (byte)call.Category;
        dest[8] = (byte)len;
        bytes.AsSpan(0, len).CopyTo(dest[RadioHeaderSize..]);
        return RadioHeaderSize + len;
    }

    /// <summary>
    /// "Receiver here" beacon, broadcast on <see cref="DiscoveryPort"/> about once a
    /// second so the phone can find the PC without a typed-in IP (10 bytes, little-endian):
    /// [marker:u32]["F1RX":4 ASCII bytes][controlPort:u16]
    /// </summary>
    public const uint DiscoveryMarker = 0xFFFFFFFB;
    public const int DiscoverySize = 10;
    public const int DiscoveryPort = 5556;

    public static void WriteDiscovery(Span<byte> dest, int controlPort)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dest, DiscoveryMarker);
        "F1RX"u8.CopyTo(dest[4..]);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[8..], (ushort)controlPort);
    }

    public static bool IsPing(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == PingMarker;
    }

    public static ControlPacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new ArgumentException($"Packet too small: {data.Length} bytes, expected {Size}");

        return new ControlPacket(
            Sequence:    BinaryPrimitives.ReadUInt32LittleEndian(data),
            TimestampUs: BinaryPrimitives.ReadUInt64LittleEndian(data[4..]),
            Steer:       BinaryPrimitives.ReadInt16LittleEndian(data[12..]),
            Throttle:    data[14],
            Brake:       data[15],
            Buttons:     BinaryPrimitives.ReadUInt16LittleEndian(data[16..])
        );
    }

    /// <summary>
    /// Button bitfield uses the XInput XUSB_GAMEPAD wButtons layout, same as
    /// the phone sends (see ControllerScreen.kt), so no translation is needed.
    /// </summary>
    public bool DPadUp      => (Buttons & 0x0001) != 0;
    public bool DPadDown    => (Buttons & 0x0002) != 0;
    public bool DPadLeft    => (Buttons & 0x0004) != 0;
    public bool DPadRight   => (Buttons & 0x0008) != 0;
    public bool ButtonStart => (Buttons & 0x0010) != 0;
    public bool ButtonBack  => (Buttons & 0x0020) != 0;
    public bool ButtonLT    => (Buttons & 0x0040) != 0; // left thumbstick click
    public bool ButtonRT    => (Buttons & 0x0080) != 0; // right thumbstick click
    public bool ButtonLB    => (Buttons & 0x0100) != 0;
    public bool ButtonRB    => (Buttons & 0x0200) != 0;
    public bool ButtonA     => (Buttons & 0x1000) != 0;
    public bool ButtonB     => (Buttons & 0x2000) != 0;
    public bool ButtonX     => (Buttons & 0x4000) != 0;
    public bool ButtonY     => (Buttons & 0x8000) != 0;
}
