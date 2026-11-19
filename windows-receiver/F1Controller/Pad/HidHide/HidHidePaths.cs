namespace F1Controller.Pad.HidHide;

public static class HidHidePaths
{
    /// <summary>
    /// HidSharp gives the device interface path (<c>\\?\hid#vid_054c&amp;pid_09cc&amp;mi_03#7&amp;…#{guid}</c>);
    /// HidHide wants the device instance path (<c>HID\VID_054C&amp;PID_09CC&amp;MI_03\7&amp;…</c>).
    /// </summary>
    public static string ToInstancePath(string hidSharpDevicePath)
    {
        string p = hidSharpDevicePath;
        if (p.StartsWith(@"\\?\")) p = p[4..];
        int lastHash = p.LastIndexOf('#');
        if (lastHash >= 0 && p.IndexOf('{', lastHash) == lastHash + 1) p = p[..lastHash];
        return p.Replace('#', '\\').ToUpperInvariant();
    }
}
