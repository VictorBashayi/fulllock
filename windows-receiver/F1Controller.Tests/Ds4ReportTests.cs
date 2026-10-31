using System.Buffers.Binary;
using F1Controller.Pad;

namespace F1Controller.Tests;

public class Ds4ReportTests
{
    private static byte[] Usb(Action<byte[], int> fill)
    {
        var b = new byte[64];
        b[0] = 0x01;
        b[1 + 4] = 0x08; // hat released
        fill(b, 1);
        return b;
    }

    private static byte[] Bt(Action<byte[], int> fill)
    {
        var b = new byte[78];
        b[0] = 0x11;
        b[3 + 4] = 0x08;
        fill(b, 3);
        return b;
    }

    private static void SticksTriggersImu(byte[] b, int d)
    {
        b[d] = 10; b[d + 1] = 20; b[d + 2] = 30; b[d + 3] = 40;
        b[d + 7] = 200; b[d + 8] = 255;
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 12), 100);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 14), -200);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 16), 300);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 18), -1000);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 20), 8192);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(d + 22), 50);
        b[d + 29] = 5;
    }

    [Fact]
    public void ParsesSticksTriggersAndImuUsb()
    {
        Assert.True(Ds4Report.TryParse(Usb(SticksTriggersImu), out var r));
        Assert.Equal((10, 20, 30, 40), (r.LX, r.LY, r.RX, r.RY));
        Assert.Equal((200, 255), (r.L2, r.R2));
        Assert.Equal((100, -200, 300), (r.GyroX, r.GyroY, r.GyroZ));
        Assert.Equal((-1000, 8192, 50), (r.AccelX, r.AccelY, r.AccelZ));
        Assert.Equal(50, r.Battery);
        Assert.Equal(0, r.Buttons);
    }

    [Fact]
    public void ParsesSameFieldsBluetooth()
    {
        Assert.True(Ds4Report.TryParse(Usb(SticksTriggersImu), out var usb));
        Assert.True(Ds4Report.TryParse(Bt(SticksTriggersImu), out var bt));
        Assert.Equal(usb, bt);
    }

    [Fact]
    public void MapsFaceAndShoulderButtons()
    {
        var b = Usb((b, d) => { b[d + 4] = 0x08 | 0x20; b[d + 5] = 0x01 | 0x20; });
        Assert.True(Ds4Report.TryParse(b, out var r));
        Assert.Equal(0x1000 | 0x0100 | 0x0010, r.Buttons);
    }

    [Fact]
    public void DiagonalHatSetsTwoDirections()
    {
        Assert.True(Ds4Report.TryParse(Usb((b, d) => b[d + 4] = 1), out var ne));
        Assert.Equal(0x0001 | 0x0008, ne.Buttons);
        Assert.True(Ds4Report.TryParse(Usb((b, d) => b[d + 4] = 8), out var none));
        Assert.Equal(0, none.Buttons);
    }

    [Fact]
    public void RejectsUnknownReportOrShortBuffer()
    {
        var unknown = Usb((_, _) => { });
        unknown[0] = 0x05;
        Assert.False(Ds4Report.TryParse(unknown, out _));
        Assert.False(Ds4Report.TryParse(Usb((_, _) => { })[..10], out _));
    }
}
