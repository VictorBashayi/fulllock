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

    private static PbStore.TrackPbs Track(sbyte id, uint dry, ushort d1, ushort d2, uint wet, ushort w1, ushort w2) =>
        new(id, new PbStore.PbLap(dry, d1, d2), new PbStore.PbLap(wet, w1, w2));

    [Fact]
    public void PbListLayout()
    {
        var buf = ControlPacket.WritePbList(new[]
        {
            Track(3, 86839, 28100, 31000, 116657, 37900, 41200),
            Track(5, 72301, 0, 0, 0, 0, 0),
        });
        Assert.Equal(5 + 2 * 17, buf.Length);
        Assert.Equal(0xFFFFFFFBu, BinaryPrimitives.ReadUInt32LittleEndian(buf));
        Assert.Equal(2, buf[4]);
        Assert.Equal(3, buf[5]);
        Assert.Equal(86839u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(6)));
        Assert.Equal(28100, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(10)));
        Assert.Equal(31000, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(12)));
        Assert.Equal(116657u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(14)));
        Assert.Equal(37900, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(18)));
        Assert.Equal(41200, BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(20)));
        Assert.Equal(5, buf[22]);
        Assert.Equal(72301u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(23)));
    }

    [Fact]
    public void EmptyPbList()
    {
        var buf = ControlPacket.WritePbList(Array.Empty<PbStore.TrackPbs>());
        Assert.Equal(5, buf.Length);
        Assert.Equal(0, buf[4]);
    }

    [Fact]
    public void PbListCapsAt50()
    {
        var many = Enumerable.Range(0, 60).Select(i => Track((sbyte)i, 80000, 0, 0, 0, 0, 0)).ToArray();
        var buf = ControlPacket.WritePbList(many);
        Assert.Equal(50, buf[4]);
        Assert.Equal(5 + 50 * 17, buf.Length);
    }
}
