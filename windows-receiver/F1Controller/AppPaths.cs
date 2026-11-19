using System;
using System.IO;

namespace F1Controller;

/// <summary>
/// Where FullLock keeps its files. Program Files isn't writable, so settings live in
/// %APPDATA%\FullLock and logs / HidHide restore state in %LOCALAPPDATA%\FullLock.
/// </summary>
public sealed class AppPaths(string roamingDir, string localDir, string exeDir)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FullLock"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FullLock"),
        AppContext.BaseDirectory);

    public string PadSettingsFile => Path.Combine(roamingDir, "pad_settings.json");
    public string PairingFile => Path.Combine(roamingDir, "pairing.json");
    public string LogsDir => Path.Combine(localDir, "logs");
    public string HidHideStateFile => Path.Combine(localDir, "hidhide-state.json");

    /// <summary>New timestamped log file path, e.g. logs\engineer_log_20261104_181500.txt.</summary>
    public string LogFile(string prefix, string extension)
    {
        Directory.CreateDirectory(LogsDir);
        return Path.Combine(LogsDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(roamingDir);
        Directory.CreateDirectory(localDir);
    }

    /// <summary>
    /// Older builds kept pad_settings.json next to the exe. Copy it (not move: the exe
    /// folder may be read-only) unless settings already exist in the new place.
    /// </summary>
    public bool MigrateLegacySettings()
    {
        var legacy = Path.Combine(exeDir, "pad_settings.json");
        if (!File.Exists(legacy) || File.Exists(PadSettingsFile)) return false;
        File.Copy(legacy, PadSettingsFile);
        return true;
    }
}
