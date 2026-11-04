using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace F1Controller;

/// <summary>Kernel drivers FullLock depends on, detected by their service registry key.</summary>
public static class Drivers
{
    public const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";
    public const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/latest";

    /// <summary>Exit code when a required driver is missing.</summary>
    public const int MissingDriverExitCode = 2;

    public static string ViGEmMissingMessage =>
        "FullLock needs the ViGEmBus driver to create a virtual Xbox controller.\n" +
        "Install it from " + ViGEmBusUrl + " (or re-run the FullLock installer), then start FullLock again.";

    public static bool IsServiceInstalled(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        return key != null;
    }

    public static bool ViGEmBusInstalled => IsServiceInstalled("ViGEmBus");
    public static bool HidHideInstalled => IsServiceInstalled("HidHide");

    /// <summary>
    /// Explain the missing ViGEmBus driver, open its download page and (when a person is
    /// watching) wait for a key so a double-clicked window doesn't vanish. Returns the exit code.
    /// </summary>
    public static int ReportViGEmMissing(bool interactive, string? detail = null)
    {
        if (detail != null) Console.WriteLine($"[ViGEm] {detail}");
        Console.WriteLine(ViGEmMissingMessage);
        if (interactive)
        {
            try { Process.Start(new ProcessStartInfo(ViGEmBusUrl) { UseShellExecute = true }); }
            catch (Exception) { /* no browser: the link is printed above */ }
            Console.WriteLine("Press any key to exit.");
            try { Console.ReadKey(intercept: true); } catch (InvalidOperationException) { }
        }
        return MissingDriverExitCode;
    }
}
