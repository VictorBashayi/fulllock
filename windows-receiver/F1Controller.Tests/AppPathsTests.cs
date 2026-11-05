using System;
using System.IO;

namespace F1Controller.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fulllock-tests-" + Guid.NewGuid().ToString("N"));
    private string Roaming => Path.Combine(_root, "roaming");
    private string Local => Path.Combine(_root, "local");
    private string Exe => Path.Combine(_root, "exe");

    public AppPathsTests() => Directory.CreateDirectory(Exe);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private AppPaths Paths() => new(Roaming, Local, Exe);

    [Fact]
    public void PadSettingsFile_IsInRoamingDir() =>
        Assert.Equal(Path.Combine(Roaming, "pad_settings.json"), Paths().PadSettingsFile);

    [Fact]
    public void PairingFileInRoaming() =>
        Assert.Equal(Path.Combine(Roaming, "pairing.json"), Paths().PairingFile);

    [Fact]
    public void HidHideStateFile_IsInLocalDir() =>
        Assert.Equal(Path.Combine(Local, "hidhide-state.json"), Paths().HidHideStateFile);

    [Fact]
    public void LogFile_IsInLocalLogsDir_AndCreatesDir()
    {
        var file = Paths().LogFile("engineer_log", "txt");
        var logs = Path.Combine(Local, "logs");
        Assert.True(Directory.Exists(logs));
        Assert.Equal(logs, Path.GetDirectoryName(file));
        Assert.StartsWith("engineer_log_", Path.GetFileName(file));
        Assert.EndsWith(".txt", file);
    }

    [Fact]
    public void Migrate_CopiesLegacyFile_WhenNoNewFile()
    {
        var legacy = Path.Combine(Exe, "pad_settings.json");
        File.WriteAllText(legacy, "{\"LockDeg\":45}");
        var paths = Paths();
        paths.EnsureCreated();

        Assert.True(paths.MigrateLegacySettings());
        Assert.Equal("{\"LockDeg\":45}", File.ReadAllText(paths.PadSettingsFile));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void Migrate_DoesNotOverwrite_ExistingNewFile()
    {
        File.WriteAllText(Path.Combine(Exe, "pad_settings.json"), "{\"LockDeg\":45}");
        var paths = Paths();
        paths.EnsureCreated();
        File.WriteAllText(paths.PadSettingsFile, "{\"LockDeg\":30}");

        Assert.False(paths.MigrateLegacySettings());
        Assert.Equal("{\"LockDeg\":30}", File.ReadAllText(paths.PadSettingsFile));
    }

    [Fact]
    public void Migrate_NoLegacyFile_ReturnsFalse()
    {
        var paths = Paths();
        paths.EnsureCreated();
        Assert.False(paths.MigrateLegacySettings());
    }
}
