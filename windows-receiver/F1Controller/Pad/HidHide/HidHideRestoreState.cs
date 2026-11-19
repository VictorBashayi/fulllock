using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace F1Controller.Pad.HidHide;

/// <summary>What FullLock changed in HidHide, so it can be undone after a crash.</summary>
public sealed record HidHideRestoreState(List<string> HiddenByUs, bool CloakTurnedOnByUs)
{
    /// <returns>null when there is no file.</returns>
    /// <exception cref="InvalidDataException">The file is unreadable (e.g. half-written at power loss).</exception>
    public static HidHideRestoreState? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<HidHideRestoreState>(File.ReadAllText(path));
            return s?.HiddenByUs != null ? s : throw new InvalidDataException("empty HidHide state");
        }
        catch (JsonException ex) { throw new InvalidDataException(ex.Message, ex); }
    }

    /// <summary>Write-then-rename so a crash mid-save never leaves a half file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, path, overwrite: true);
    }
}
