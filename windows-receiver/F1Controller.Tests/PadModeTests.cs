using F1Controller.Pad;

namespace F1Controller.Tests;

public class PadModeTests
{
    [Fact]
    public void StickAxisCentresAndClamps()
    {
        Assert.Equal(0, PadMode.StickAxis(128, invert: false));
        Assert.Equal(32766, PadMode.StickAxis(255, invert: false));
        Assert.Equal(-32768, PadMode.StickAxis(0, invert: false));
    }

    [Fact]
    public void ReportsGoStaleEarlyEnoughForNeutralWithin100ms()
    {
        // The 20 ms timer really fires every ~31 ms (15.6 ms system tick), so the threshold
        // plus one late tick must still land inside 100 ms
        long f = System.Diagnostics.Stopwatch.Frequency;
        Assert.False(PadMode.IsStale(lastReportTicks: f, nowTicks: f + f * 50 / 1000));
        Assert.True(PadMode.IsStale(lastReportTicks: f, nowTicks: f + f * 70 / 1000));
        Assert.True(PadMode.IsStale(lastReportTicks: 0, nowTicks: f));   // never had a report
    }

    [Fact]
    public void StickAxisInvertsYSoUpIsPositive()
    {
        Assert.True(PadMode.StickAxis(0, invert: true) > 32000);   // DS4 stick pushed up
        Assert.True(PadMode.StickAxis(255, invert: true) < -32000);
    }
}
