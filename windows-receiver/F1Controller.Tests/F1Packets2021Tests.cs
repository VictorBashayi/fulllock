using F1Controller.Engineer;
using F1Controller.Validation;

namespace F1Controller.Tests;

public class F1Packets2021Tests
{
    private static byte[] Sample(byte[] body, int i, byte type, byte offset, byte weather)
    {
        body[126] = (byte)Math.Max(body[126], i + 1);
        int o = 127 + i * 8;
        body[o] = type; body[o + 1] = offset; body[o + 2] = weather;
        return body;
    }

    [Fact]
    public void SessionReadsWeatherLapsTypeSafetyCar()
    {
        var body = new byte[632];
        body[0] = 2; body[3] = 28; body[6] = 10; body[124] = 1;
        Assert.Equal(new F1Packets2021.SessionInfo(2, 28, 10, 1, null, TrackId: 0), F1Packets2021.ParseSession(body));
    }

    [Fact]
    public void SessionRainInMinutesIsEarliestRainySample()
    {
        var body = new byte[632];
        body[6] = 10;
        Sample(body, 0, 10, 5, 1);
        Sample(body, 1, 10, 10, 3);
        Sample(body, 2, 10, 15, 4);
        Assert.Equal(10, F1Packets2021.ParseSession(body)!.Value.RainInMinutes);
    }

    [Fact]
    public void SessionIgnoresForecastForOtherSessionTypes()
    {
        var body = new byte[632];
        body[6] = 10;
        Sample(body, 0, 5, 10, 3);
        Assert.Null(F1Packets2021.ParseSession(body)!.Value.RainInMinutes);
    }

    [Fact]
    public void SessionTooShortReturnsNull() =>
        Assert.Null(F1Packets2021.ParseSession(new byte[100]));

    private static byte[] Pena(byte type, byte vehicle, byte time)
    {
        var b = new byte[12];
        "PENA"u8.CopyTo(b);
        b[4] = type; b[6] = vehicle; b[8] = time;
        return b;
    }

    [Fact]
    public void EventDrsEnabled()
    {
        var b = new byte[12];
        "DRSE"u8.CopyTo(b);
        Assert.Equal(new GameEvent(GameEventKind.DrsEnabled), F1Packets2021.ParseEvent(b, 0));
    }

    [Fact]
    public void EventPenaltyForPlayerOnly()
    {
        var b = Pena(4, 3, 5);
        Assert.Equal(new GameEvent(GameEventKind.Penalty, 4, 5), F1Packets2021.ParseEvent(b, 3));
        Assert.Null(F1Packets2021.ParseEvent(b, 2));
    }

    [Fact]
    public void EventWarningIsIgnored() =>
        Assert.Null(F1Packets2021.ParseEvent(Pena(5, 3, 5), 3));

    [Fact]
    public void DamageTakesWorstFrontWing()
    {
        var e = new byte[39];
        e[24] = 10; e[25] = 30; e[26] = 5;
        var d = F1Packets2021.ParseDamage(e);
        Assert.Equal(30, d.FrontWing);
        Assert.Equal(5, d.RearWing);
    }

    [Fact]
    public void FiaFlagReadsSignedByte28()
    {
        var e = new byte[47];
        e[28] = 0xFF;
        Assert.Equal(-1, F1Packets2021.FiaFlag(e));
    }

    [Fact]
    public void ParseSessionReadsTrackId()
    {
        var body = new byte[632];
        body[6] = 12; body[7] = 5;
        Assert.Equal(5, F1Packets2021.ParseSession(body)!.Value.TrackId);
        body[7] = 0xFF;
        Assert.Equal(-1, F1Packets2021.ParseSession(body)!.Value.TrackId);
    }
}
