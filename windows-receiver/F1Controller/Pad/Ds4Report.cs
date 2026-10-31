using System;
using System.Buffers.Binary;

namespace F1Controller.Pad;

/// <summary>
/// One DualShock 4 input report. Buttons use the XUSB layout of <see cref="Transport.ControlPacket"/>.
/// USB sends report 0x01 (data at byte 1); Bluetooth sends 0x11 (data at byte 3) once full reports are enabled.
/// </summary>
public readonly record struct Ds4Report(
    byte LX, byte LY, byte RX, byte RY,
    ushort Buttons,
    byte L2, byte R2,
    ushort Timestamp,
    short GyroX, short GyroY, short GyroZ,
    short AccelX, short AccelY, short AccelZ,
    byte Battery)
{
    private const int DataLength = 30; // up to and including the battery byte

    // Hat 0..7 = N, NE, E, SE, S, SW, W, NW → XUSB d-pad bits (up 1, down 2, left 4, right 8)
    private static readonly ushort[] HatToDpad = { 0x1, 0x9, 0x8, 0xA, 0x2, 0x6, 0x4, 0x5 };

    public static bool TryParse(ReadOnlySpan<byte> report, out Ds4Report r)
    {
        r = default;
        if (report.IsEmpty) return false;
        int d = report[0] switch { 0x01 => 1, 0x11 => 3, _ => -1 };
        if (d < 0 || report.Length < d + DataLength) return false;
        var s = report[d..];

        int hat = s[4] & 0x0F;
        ushort buttons = hat < 8 ? HatToDpad[hat] : (ushort)0;
        if ((s[4] & 0x10) != 0) buttons |= 0x4000; // square → X
        if ((s[4] & 0x20) != 0) buttons |= 0x1000; // cross → A
        if ((s[4] & 0x40) != 0) buttons |= 0x2000; // circle → B
        if ((s[4] & 0x80) != 0) buttons |= 0x8000; // triangle → Y
        if ((s[5] & 0x01) != 0) buttons |= 0x0100; // L1 → LB
        if ((s[5] & 0x02) != 0) buttons |= 0x0200; // R1 → RB
        if ((s[5] & 0x10) != 0) buttons |= 0x0020; // Share → Back
        if ((s[5] & 0x20) != 0) buttons |= 0x0010; // Options → Start
        if ((s[5] & 0x40) != 0) buttons |= 0x0040; // L3
        if ((s[5] & 0x80) != 0) buttons |= 0x0080; // R3

        r = new Ds4Report(
            s[0], s[1], s[2], s[3],
            buttons,
            s[7], s[8],
            BinaryPrimitives.ReadUInt16LittleEndian(s[9..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[12..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[14..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[16..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[18..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[20..]),
            BinaryPrimitives.ReadInt16LittleEndian(s[22..]),
            (byte)Math.Min((s[29] & 0x0F) * 10, 100));
        return true;
    }
}
