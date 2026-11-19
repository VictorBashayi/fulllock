using System;
using F1Controller.Pad.HidHide;

namespace F1Controller.Tests;

public class HidHideCliTests
{
    [Fact]
    public void ParseList_DevHide()
    {
        // Copied from HidHideCLI --dev-list on the dev PC
        const string stdout =
            "--dev-hide \"HID\\VID_054C&PID_09CC&MI_03\\7&18eac8c&0&0000\"\r\n" +
            "--dev-hide \"HID\\VID_054C&PID_09CC&MI_03\\7&210509CF&0&0000\"\r\n" +
            "--dev-hide \"HID\\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc\\8&3a65874a&a&0000\"\r\n";
        Assert.Equal(new[]
        {
            @"HID\VID_054C&PID_09CC&MI_03\7&18eac8c&0&0000",
            @"HID\VID_054C&PID_09CC&MI_03\7&210509CF&0&0000",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc\8&3a65874a&a&0000",
        }, HidHideCli.ParseList(stdout, "--dev-hide"));
    }

    [Fact]
    public void ParseList_AppReg_PathWithSpaces()
    {
        const string stdout =
            "--app-reg \"C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideCLI.exe\"\r\n" +
            "some unrelated line\r\n" +
            "--app-reg \"C:\\Users\\Ünïcode User\\FullLock\\FullLock.exe\"\r\n";
        Assert.Equal(new[]
        {
            @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
            @"C:\Users\Ünïcode User\FullLock\FullLock.exe",
        }, HidHideCli.ParseList(stdout, "--app-reg"));
    }

    [Fact]
    public void ParseCloak_On() => Assert.True(HidHideCli.ParseCloak("--cloak-on\r\n"));

    [Fact]
    public void ParseCloak_Off() => Assert.False(HidHideCli.ParseCloak("--cloak-off\r\n"));

    [Fact]
    public void Cli_BuildArgs_QuotesPathWithSpaces() =>
        Assert.Equal("--app-reg \"C:\\Program Files\\FullLock\\FullLock.exe\"",
            HidHideCli.BuildArgs("--app-reg", @"C:\Program Files\FullLock\FullLock.exe"));

    [Fact]
    public void Cli_BuildArgs_NoValue() => Assert.Equal("--cloak-state", HidHideCli.BuildArgs("--cloak-state", null));

    [Fact]
    public void Cli_BuildArgs_RejectsQuote() =>
        Assert.Throws<ArgumentException>(() => HidHideCli.BuildArgs("--dev-hide", "bad\"path"));

    // HidHideCLI exits 0 even when it refuses a command, so changes are verified by listing again
    [Fact]
    public void Hide_NotListedAfterwards_Throws()
    {
        var cli = new HidHideCli(_ => "");
        Assert.Throws<HidHideException>(() => cli.Hide(@"HID\VID_054C&PID_09CC&MI_03\7&1&0&0000"));
    }

    [Fact]
    public void Hide_ListedAfterwards_Succeeds()
    {
        const string path = @"HID\VID_054C&PID_09CC&MI_03\7&1&0&0000";
        var cli = new HidHideCli(args => args == "--dev-list" ? $"--dev-hide \"{path.ToLowerInvariant()}\"\r\n" : "");
        cli.Hide(path);
    }

    [Fact]
    public void RegisterApp_NotListedAfterwards_Throws()
    {
        var cli = new HidHideCli(_ => "");
        Assert.Throws<HidHideException>(() => cli.RegisterApp(@"C:\FullLock\FullLock.exe"));
    }

    [Fact]
    public void SetCloakOn_StillOff_Throws()
    {
        var cli = new HidHideCli(args => args == "--cloak-state" ? "--cloak-off" : "");
        Assert.Throws<HidHideException>(() => cli.SetCloak(true));
    }
}
