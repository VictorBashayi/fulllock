using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace F1Controller.Pad.HidHide;

/// <summary>
/// Talks to HidHide through its bundled HidHideCLI.exe (one process per operation). The CLI exits 0
/// even when it refuses a command, so every change is checked by listing again.
/// </summary>
public sealed class HidHideCli : IHidHide
{
    private const int TimeoutMs = 5000;
    private readonly Func<string, string> _run;

    public HidHideCli(string cliPath) : this(args => RunProcess(cliPath, args)) { }

    /// <param name="run">CLI arguments → stdout (tests pass a fake).</param>
    public HidHideCli(Func<string, string> run) => _run = run;

    /// <summary>HidHideCLI.exe from HidHide's install folder, or null when HidHide isn't installed.</summary>
    public static HidHideCli? Locate()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
        if (key?.GetValue("Path") is not string dir) return null;
        string cli = Path.Combine(dir, "x64", "HidHideCLI.exe");
        return File.Exists(cli) ? new HidHideCli(cli) : null;
    }

    public bool IsCloaked() => ParseCloak(Run("--cloak-state", null));
    public IReadOnlyList<string> HiddenDevices() => ParseList(Run("--dev-list", null), "--dev-hide");
    public IReadOnlyList<string> RegisteredApps() => ParseList(Run("--app-list", null), "--app-reg");
    public void RegisterApp(string exePath)
    {
        Run("--app-reg", exePath);
        Verify(RegisteredApps().Any(a => Same(a, exePath)), "register FullLock");
    }

    public void UnregisterApp(string exePath)
    {
        Run("--app-unreg", exePath);
        Verify(!RegisteredApps().Any(a => Same(a, exePath)), "unregister FullLock");
    }

    public void Hide(string instancePath)
    {
        Run("--dev-hide", instancePath);
        Verify(HiddenDevices().Any(d => Same(d, instancePath)), "hide the DS4");
    }

    public void Unhide(string instancePath)
    {
        Run("--dev-unhide", instancePath);
        Verify(!HiddenDevices().Any(d => Same(d, instancePath)), "unhide the DS4");
    }

    public void SetCloak(bool on)
    {
        Run(on ? "--cloak-on" : "--cloak-off", null);
        Verify(IsCloaked() == on, on ? "turn hiding on" : "turn hiding off");
    }

    private static void Verify(bool ok, string what)
    {
        if (!ok) throw new HidHideException($"HidHide didn't {what} (it may need to be run as administrator)");
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lines like <c>--dev-hide "HID\…"</c> → the quoted values; other lines ignored.</summary>
    public static IReadOnlyList<string> ParseList(string stdout, string flag)
    {
        string prefix = flag + " \"";
        return stdout.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(prefix, StringComparison.Ordinal) && l.EndsWith('"') && l.Length > prefix.Length)
            .Select(l => l[prefix.Length..^1])
            .ToList();
    }

    public static bool ParseCloak(string stdout) => stdout.Trim() == "--cloak-on";

    public static string BuildArgs(string flag, string? value)
    {
        if (value == null) return flag;
        if (value.Contains('"')) throw new ArgumentException("HidHide argument must not contain quotes", nameof(value));
        return $"{flag} \"{value}\"";
    }

    private string Run(string flag, string? value) => _run(BuildArgs(flag, value));

    private static string RunProcess(string cliPath, string args)
    {
        string flag = args.Split(' ')[0];
        var psi = new ProcessStartInfo(cliPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new HidHideException("couldn't start HidHideCLI");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeoutMs))
        {
            try { p.Kill(); } catch (Exception) { }
            throw new HidHideException($"HidHideCLI {flag} timed out");
        }
        if (p.ExitCode != 0)
            throw new HidHideException($"HidHideCLI {flag} failed ({p.ExitCode}): {stderr.Result.Trim()}");
        return stdout.Result;
    }
}
