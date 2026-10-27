using F1Controller.Engineer;

namespace F1Controller.Tests;

public class CallLogTests
{
    [Fact]
    public void WritesOneLinePerCall()
    {
        var w = new StringWriter();
        var log = new CallLog(w, () => new DateTime(2026, 10, 27, 21, 5, 9, 250));
        log.Write(new RadioCall("sc-deployed", "Safety car deployed.", RadioPriority.Critical, RadioCategory.Flags));
        log.Write(new RadioCall("pb", "Personal best, 1:31.204.", RadioPriority.Info, RadioCategory.Lap));
        Assert.Equal(
            "21:05:09.250  P3  sc-deployed  Safety car deployed." + Environment.NewLine +
            "21:05:09.250  P1  pb  Personal best, 1:31.204." + Environment.NewLine,
            w.ToString());
    }

    [Fact]
    public void WritesErrors()
    {
        var w = new StringWriter();
        var log = new CallLog(w, () => new DateTime(2026, 10, 27, 21, 5, 9, 250));
        log.WriteError(new InvalidOperationException("boom"));
        Assert.StartsWith("21:05:09.250  ERROR  System.InvalidOperationException: boom", w.ToString());
    }
}
