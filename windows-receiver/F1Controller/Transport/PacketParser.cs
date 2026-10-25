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
    public const int PingSize = 16; // [marker:u32][seq:u32][timestamp_us:u64]

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
