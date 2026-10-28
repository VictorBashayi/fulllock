using F1Controller.Validation;

namespace F1Controller.Tests;

public class RaceTrackerTests
{
    private const int Header = 24;
    private const int Entry = 43;

    private static void Car(byte[] buf, int idx, byte position, byte pitStatus, byte pitStops = 0)
    {
        int o = Header + idx * Entry;
        buf[o + 24] = position;
        buf[o + 25] = 5;          // lap
        buf[o + 26] = pitStatus;
        buf[o + 27] = pitStops;
        buf[o + 36] = 2;          // active
    }

    private static byte[] PlayerLap(byte lap, uint lastLapMs)
    {
        var buf = new byte[Header + 22 * Entry];
        Car(buf, 0, position: 1, pitStatus: 0);
        int o = Header;
        buf[o + 25] = lap;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o), lastLapMs);
        return buf;
    }

    [Fact]
    public void FlashbackKeepsBestLapRestartClearsIt()
    {
        var tracker = new RaceTracker();
        tracker.Process(PlayerLap(1, 0), Header, 0, 50f);
        tracker.Process(PlayerLap(2, 80000), Header, 0, 90f);
        Assert.Equal(80000u, tracker.Info.BestLapMs);

        tracker.Process(PlayerLap(2, 80000), Header, 0, 60f);   // flashback 30 s
        Assert.Equal(80000u, tracker.Info.BestLapMs);

        tracker.Process(PlayerLap(1, 0), Header, 0, 2f);        // restart: clock back near zero
        Assert.Equal(0u, tracker.Info.BestLapMs);
    }

    [Fact]
    public void ReadsPitStatusAndNeighboursInPit()
    {
        var buf = new byte[Header + 22 * Entry];
        Car(buf, 0, position: 2, pitStatus: 1, pitStops: 1);  // player
        Car(buf, 1, position: 1, pitStatus: 2);
        Car(buf, 2, position: 3, pitStatus: 0);

        var tracker = new RaceTracker();
        tracker.Process(buf, Header, 0, 100f);

        Assert.Equal(1, tracker.Info.PitStatus);
        Assert.Equal(1, tracker.Info.NumPitStops);
        Assert.True(tracker.Info.CarAheadInPit);
        Assert.False(tracker.Info.CarBehindInPit);
    }
}
