using F1Controller.Engineer;

namespace F1Controller.Tests;

public class RadioDispatcherTests
{
    private static readonly EngineerThresholds T = new();

    private static RadioCall Call(string key, RadioPriority p = RadioPriority.Info) =>
        new(key, key, p, RadioCategory.Other);

    [Fact]
    public void SpacesCallsFourSeconds()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("a"), Call("b") }, 10);
        Assert.Equal("a", d.Next(10)?.Key);
        Assert.Null(d.Next(12));
        Assert.Equal("b", d.Next(14)?.Key);
    }

    [Fact]
    public void CriticalBypassesSpacing()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("a") }, 10);
        Assert.Equal("a", d.Next(10)?.Key);
        d.Enqueue(new[] { Call("sc-deployed", RadioPriority.Critical) }, 11);
        Assert.Equal("sc-deployed", d.Next(11)?.Key);
    }

    [Fact]
    public void HighestPriorityFirst()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("info"), Call("important", RadioPriority.Important) }, 10);
        Assert.Equal("important", d.Next(10)?.Key);
    }

    [Fact]
    public void OldestFirstWithinPriority()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("first") }, 10);
        d.Enqueue(new[] { Call("second") }, 11);
        Assert.Equal("first", d.Next(11)?.Key);
    }

    [Fact]
    public void CooldownBlocksRepeat()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("tyre-hot-FL", RadioPriority.Important) }, 10);
        Assert.NotNull(d.Next(10));
        d.Enqueue(new[] { Call("tyre-hot-FL", RadioPriority.Important) }, 40);
        Assert.Null(d.Next(40));
        Assert.Null(d.Next(50));
        d.Enqueue(new[] { Call("tyre-hot-FL", RadioPriority.Important) }, 71);
        Assert.Equal("tyre-hot-FL", d.Next(71)?.Key);
    }

    [Fact]
    public void ZeroCooldownKeysRepeat()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("sc-deployed", RadioPriority.Critical) }, 10);
        Assert.NotNull(d.Next(10));
        d.Enqueue(new[] { Call("sc-deployed", RadioPriority.Critical) }, 11);
        Assert.Equal("sc-deployed", d.Next(11)?.Key);
    }

    [Fact]
    public void StaleNonCriticalDropped()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("a") }, 10);
        Assert.Null(d.Next(26));
        Assert.Null(d.Next(30));
    }

    [Fact]
    public void StaleCriticalKept()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("flag-red", RadioPriority.Critical) }, 10);
        Assert.Equal("flag-red", d.Next(26)?.Key);
    }

    [Fact]
    public void DuplicateQueuedKeyIgnored()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("a") }, 10);
        d.Enqueue(new[] { Call("a") }, 10);
        Assert.Equal("a", d.Next(10)?.Key);
        Assert.Null(d.Next(20));
    }

    [Fact]
    public void ResetClearsQueueAndCooldowns()
    {
        var d = new RadioDispatcher(T);
        d.Enqueue(new[] { Call("a"), Call("b") }, 10);
        Assert.NotNull(d.Next(10));
        d.Reset();
        Assert.Null(d.Next(20));
        d.Enqueue(new[] { Call("a") }, 1);
        Assert.Equal("a", d.Next(1)?.Key);
    }
}
