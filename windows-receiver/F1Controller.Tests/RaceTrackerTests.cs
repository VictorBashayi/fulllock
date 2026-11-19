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

    private static byte[] PlayerLap(byte lap, uint lastLapMs, bool invalid = false, byte driverStatus = 1)
    {
        var buf = new byte[Header + 22 * Entry];
        Car(buf, 0, position: 1, pitStatus: 0);
        int o = Header;
        buf[o + 25] = lap;
        buf[o + 29] = (byte)(invalid ? 1 : 0);
        buf[o + 35] = driverStatus;
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

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-5f)]
    [InlineData(1e30f)]
    public void LapDistanceNonFiniteOrNegativeDoesNotThrow(float lapDistance)
    {
        var buf = PlayerLap(1, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(Header + 12), lapDistance);

        var tracker = new RaceTracker();
        tracker.Process(buf, Header, 0, 50f);

        Assert.Null(tracker.Info.DeltaMs);
    }

    private static List<uint> Laps(RaceTracker t)
    {
        var laps = new List<uint>();
        t.OnPlayerLapCompleted += ms => laps.Add(ms);
        return laps;
    }

    [Fact]
    public void LapCompleteRaisesEvent()
    {
        var tracker = new RaceTracker();
        var laps = Laps(tracker);
        tracker.Process(PlayerLap(1, 0), Header, 0, 50f);
        tracker.Process(PlayerLap(2, 0), Header, 0, 51f);        // out-lap crossing, no time yet
        Assert.Empty(laps);
        tracker.Process(PlayerLap(3, 80000), Header, 0, 90f);
        tracker.Process(PlayerLap(4, 82000), Header, 0, 170f);   // slower valid lap still raises
        Assert.Equal(new uint[] { 80000, 82000 }, laps);
    }

    [Fact]
    public void InvalidLapDoesNotRaise()
    {
        var tracker = new RaceTracker();
        var laps = Laps(tracker);
        tracker.Process(PlayerLap(2, 0), Header, 0, 50f);
        tracker.Process(PlayerLap(2, 0, invalid: true), Header, 0, 70f);   // flag set before the crossing
        tracker.Process(PlayerLap(3, 80000), Header, 0, 90f);
        Assert.Empty(laps);
    }

    [Fact]
    public void OutLapDoesNotRaise()
    {
        var tracker = new RaceTracker();
        var laps = Laps(tracker);
        tracker.Process(PlayerLap(2, 0, driverStatus: 3), Header, 0, 50f);   // out-lap
        tracker.Process(PlayerLap(3, 80000), Header, 0, 90f);
        Assert.Empty(laps);
    }

    [Fact]
    public void LapNumberDropDoesNotRaise()
    {
        var tracker = new RaceTracker();
        var laps = Laps(tracker);
        tracker.Process(PlayerLap(5, 0), Header, 0, 50f);
        tracker.Process(PlayerLap(2, 80000), Header, 0, 90f);
        Assert.Empty(laps);
    }

    [Fact]
    public void ThrowingSubscriberDoesNotFreezeInfoOrRefire()
    {
        var tracker = new RaceTracker();
        int calls = 0;
        tracker.OnPlayerLapCompleted += _ => { calls++; throw new InvalidOperationException("boom"); };
        tracker.Process(PlayerLap(2, 0), Header, 0, 50f);
        Assert.Throws<InvalidOperationException>(() => tracker.Process(PlayerLap(3, 80000), Header, 0, 90f));
        Assert.Equal(3, tracker.Info.CurrentLap);
        tracker.Process(PlayerLap(3, 80000), Header, 0, 91f);
        Assert.Equal(1, calls);
    }
}
