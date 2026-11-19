using System;

namespace F1Controller.Tests;

public class DriversTests
{
    [Fact]
    public void IsServiceInstalled_UnknownService_False() =>
        Assert.False(Drivers.IsServiceInstalled("FullLockNoSuchService_" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void IsServiceInstalled_KnownService_True() =>
        Assert.True(Drivers.IsServiceInstalled("Tcpip"));

    [Fact]
    public void ViGEmMissingMessage_ContainsUrl() =>
        Assert.Contains(Drivers.ViGEmBusUrl, Drivers.ViGEmMissingMessage);
}
