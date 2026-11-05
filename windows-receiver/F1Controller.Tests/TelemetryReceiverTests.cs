using F1Controller.Validation;

namespace F1Controller.Tests;

public class TelemetryReceiverTests
{
    // F1 2021 header (24 bytes): format @0, packetId @5, playerCarIndex @22; car telemetry id 6.
    private static byte[] CarTelemetryPacket()
    {
        var buf = new byte[24 + 22 * 60];
        buf[0] = 0xE5; buf[1] = 0x07; // 2021
        buf[5] = 6;
        return buf;
    }

    [Fact]
    public void HandleSafelySurvivesThrowingSubscriber()
    {
        using var rx = new TelemetryReceiver(0);
        bool boom = true;
        int calls = 0;
        rx.OnStateChanged += _ => { calls++; if (boom) throw new InvalidOperationException("boom"); };

        Assert.False(rx.HandleSafely(CarTelemetryPacket()));
        boom = false;
        Assert.True(rx.HandleSafely(CarTelemetryPacket()));
        Assert.Equal(2, calls);
    }
}
