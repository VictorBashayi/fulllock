using System;
using F1Controller.Pad.HidHide;

namespace F1Controller.Tests;

public class HidHidePathsTests
{
    [Fact]
    public void ToInstancePath_Usb() =>
        Assert.Equal(@"HID\VID_054C&PID_09CC&MI_03\7&18EAC8C&0&0000",
            HidHidePaths.ToInstancePath(@"\\?\hid#vid_054c&pid_09cc&mi_03#7&18eac8c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ToInstancePath_Bluetooth() =>
        Assert.Equal(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&09CC\8&3A65874A&A&0000",
            HidHidePaths.ToInstancePath(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&09cc#8&3a65874a&a&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"),
            StringComparer.OrdinalIgnoreCase);
}
