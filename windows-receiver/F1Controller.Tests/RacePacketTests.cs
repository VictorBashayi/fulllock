using System.Buffers.Binary;
using F1Controller.Transport;
using F1Controller.Validation;

namespace F1Controller.Tests;

public class RacePacketTests
{
    private static RaceState State(sbyte track, uint dry, uint wet) => new(
        new RaceInfo(1, 1, 0, false, 0, 0, 0, null, 0, 0,
            RaceTracker.SectorStatus.None, RaceTracker.SectorStatus.None, null, null),
        TotalLaps: 3, TyreCompound: 16, TyreAgeLaps: 0, FuelLaps: 0,
        TyreWear: new byte[4], TyreTemp: new byte[4], TyreInnerTemp: new byte[4],
        TrackId: track, TtDryMs: dry, TtWetMs: wet);

    [Fact]
    public void RacePacketCarriesTrackAndPbs()
    {
        var buf = new byte[ControlPacket.RaceStateSize];
        ControlPacket.WriteRaceState(buf, State(5, 78123, 91234));
        Assert.Equal(62, ControlPacket.RaceStateSize);
        Assert.Equal(5, buf[52]);
        Assert.Equal(78123u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(54)));
        Assert.Equal(91234u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(58)));
    }

    [Fact]
    public void UnknownTrackIsMinusOneOnTheWire()
    {
        var buf = new byte[ControlPacket.RaceStateSize];
        ControlPacket.WriteRaceState(buf, State(-1, 0, 0));
        Assert.Equal(0xFF, buf[52]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(54)));
    }
}
