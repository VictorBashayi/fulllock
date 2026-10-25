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
    /// Decode button bitfield into Xbox button flags.
    /// Bit layout: 0=A, 1=B, 2=X, 3=Y, 4=LB, 5=RB, 6=Start, 7=Back,
    ///             8=LThumb, 9=RThumb, 10=DUp, 11=DDown, 12=DLeft, 13=DRight
    /// </summary>
    public bool ButtonA     => (Buttons & (1 << 0)) != 0;
    public bool ButtonB     => (Buttons & (1 << 1)) != 0;
    public bool ButtonX     => (Buttons & (1 << 2)) != 0;
    public bool ButtonY     => (Buttons & (1 << 3)) != 0;
    public bool ButtonLB    => (Buttons & (1 << 4)) != 0;
    public bool ButtonRB    => (Buttons & (1 << 5)) != 0;
    public bool ButtonStart => (Buttons & (1 << 6)) != 0;
    public bool ButtonBack  => (Buttons & (1 << 7)) != 0;
    public bool ButtonLT    => (Buttons & (1 << 8)) != 0;
    public bool ButtonRT    => (Buttons & (1 << 9)) != 0;
    public bool DPadUp      => (Buttons & (1 << 10)) != 0;
    public bool DPadDown    => (Buttons & (1 << 11)) != 0;
    public bool DPadLeft    => (Buttons & (1 << 12)) != 0;
    public bool DPadRight   => (Buttons & (1 << 13)) != 0;
}
