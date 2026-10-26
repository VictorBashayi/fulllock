using F1Controller.Engineer;
using F1Controller.Transport;
using Xunit;

namespace F1Controller.Tests;

public class RadioPacketTests
{
    [Fact]
    public void WritesHeaderAndText()
    {
        var buf = new byte[ControlPacket.RadioHeaderSize + ControlPacket.RadioMaxText];
        int n = ControlPacket.WriteRadioCall(buf, 0x1234,
            new RadioCall("k", "Box", RadioPriority.Important, RadioCategory.Tyres));
        Assert.Equal(12, n);
        Assert.Equal(new byte[] { 0xFC, 0xFF, 0xFF, 0xFF, 0x34, 0x12, 2, 0, 3, (byte)'B', (byte)'o', (byte)'x' },
            buf[..n]);
    }

    [Fact]
    public void TruncatesLongTextOnCharacterBoundary()
    {
        var buf = new byte[ControlPacket.RadioHeaderSize + ControlPacket.RadioMaxText];
        string text = new string('a', 119) + "é";  // 'é' is 2 bytes: 121 total
        int n = ControlPacket.WriteRadioCall(buf, 1, new RadioCall("k", text, RadioPriority.Info, RadioCategory.Other));
        Assert.Equal(119, buf[8]);
        Assert.Equal(ControlPacket.RadioHeaderSize + 119, n);
    }
}
