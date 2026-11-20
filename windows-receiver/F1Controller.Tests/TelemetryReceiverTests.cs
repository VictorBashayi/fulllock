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

    private static byte[] Header2021(byte packetId, int bodySize, float sessionTime = 0f)
    {
        var buf = new byte[24 + bodySize];
        buf[0] = 0xE5; buf[1] = 0x07; // 2021
        buf[5] = packetId;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(14), sessionTime);
        return buf;
    }

    private static byte[] SessionPacket(byte sessionType, sbyte trackId)
    {
        var buf = Header2021(1, 632);
        buf[24 + 6] = sessionType;
        buf[24 + 7] = unchecked((byte)trackId);
        return buf;
    }

    private static byte[] StatusPacket(byte visualCompound)
    {
        var buf = Header2021(7, 22 * 47);
        buf[24 + 26] = visualCompound;
        return buf;
    }

    private static byte[] LapPacket(byte lap, uint lastLapMs, float sessionTime, byte sector = 0, ushort s1 = 0, ushort s2 = 0)
    {
        var buf = Header2021(2, 22 * 43, sessionTime);
        int o = 24;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o), lastLapMs);
        buf[o + 24] = 1;   // position
        buf[o + 25] = lap;
        buf[o + 35] = 1;   // flying lap
        buf[o + 28] = sector;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o + 8), s1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o + 10), s2);
        buf[o + 36] = 2;   // active
        return buf;
    }

    /// <summary>Sector 1 and 2 of <paramref name="lap"/> so the crossing after it is a whole lap.</summary>
    private static void DriveSectors(TelemetryReceiver rx, byte lap, float time)
    {
        rx.HandleSafely(LapPacket(lap, 0, time + 25, sector: 1, s1: 25000));
        rx.HandleSafely(LapPacket(lap, 0, time + 55, sector: 2, s1: 25000, s2: 30000));
    }

    private static string TempPb() => Path.Combine(Path.GetTempPath(), "pbrx-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void TimeTrialLapIsRecordedDryAndWet()
    {
        var pbs = new PbStore(TempPb());
        using var rx = new TelemetryReceiver(0, pbs);
        rx.HandleSafely(SessionPacket(13, 5));
        rx.HandleSafely(StatusPacket(8));                       // full wets
        rx.HandleSafely(LapPacket(1, 0, 50f));
        DriveSectors(rx, 1, 50f);
        rx.HandleSafely(LapPacket(2, 95000, 150f));
        Assert.Equal(95000u, pbs.Get(5, 13, wet: true));
        Assert.Equal(0u, pbs.Get(5, 13, wet: false));
        Assert.Equal(new PbStore.PbLap(95000, 25000, 30000), pbs.List(13)[0].Wet);

        rx.HandleSafely(StatusPacket(16));                      // softs
        DriveSectors(rx, 2, 150f);
        rx.HandleSafely(LapPacket(3, 80000, 240f));
        Assert.Equal(80000u, pbs.Get(5, 13, wet: false));
        Assert.Equal(95000u, pbs.Get(5, 13, wet: true));
    }

    [Fact]
    public void NoSessionPacketMeansNothingRecorded()
    {
        var pbs = new PbStore(TempPb());
        using var rx = new TelemetryReceiver(0, pbs);
        rx.HandleSafely(LapPacket(1, 0, 50f));
        DriveSectors(rx, 1, 50f);
        rx.HandleSafely(LapPacket(2, 95000, 150f));
        Assert.Equal(0u, pbs.Get(0, 13, false));
    }

    [Fact]
    public void RaceStateCarriesTrackAndTimeTrialPbs()
    {
        var pbs = new PbStore(TempPb());
        pbs.TryRecord(5, 13, false, 78123);
        pbs.TryRecord(5, 13, true, 91234);
        using var rx = new TelemetryReceiver(0, pbs);
        RaceState? last = null;
        rx.OnRaceStateChanged += s => last = s;
        rx.HandleSafely(SessionPacket(10, 5));                  // in a race, still shows the TT targets
        rx.HandleSafely(LapPacket(1, 0, 50f));
        Assert.Equal(5, last!.TrackId);
        Assert.Equal(78123u, last.TtDryMs);
        Assert.Equal(91234u, last.TtWetMs);
    }
}
