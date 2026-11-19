using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace F1Controller.Validation;

/// <summary>
/// Personal-best laps, one per track / session type / dry|wet, kept in a small JSON file.
/// A PB only ever gets faster. Save problems (locked file, missing folder) never throw: the
/// value stays in memory and is retried on the next PB.
/// </summary>
public sealed class PbStore
{
    private sealed record Entry(uint Ms, string Date);

    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, Entry> _pbs = new();
    private bool _saveFailureLogged;

    public PbStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        try
        {
            _pbs = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            _pbs = new();
        }
    }

    private static string Key(sbyte trackId, byte sessionType, bool wet) =>
        $"{trackId}/{sessionType}/{(wet ? "wet" : "dry")}";

    /// <summary>Time in ms of the stored PB, 0 = none.</summary>
    public uint Get(sbyte trackId, byte sessionType, bool wet)
    {
        lock (_lock) return _pbs.TryGetValue(Key(trackId, sessionType, wet), out var e) ? e.Ms : 0;
    }

    /// <summary>True if lapMs is a new PB (stored and saved). Unknown track or zero time: false.</summary>
    public bool TryRecord(sbyte trackId, byte sessionType, bool wet, uint lapMs)
    {
        if (trackId < 0 || lapMs == 0) return false;
        lock (_lock)
        {
            var key = Key(trackId, sessionType, wet);
            if (_pbs.TryGetValue(key, out var old) && lapMs >= old.Ms) return false;
            _pbs[key] = new Entry(lapMs, DateTime.Now.ToString("yyyy-MM-dd"));
            Save();
            return true;
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_pbs));
            File.Move(tmp, _path, overwrite: true);
            _saveFailureLogged = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!_saveFailureLogged) Console.Error.WriteLine($"PB save failed ({e.Message}); keeping PBs in memory");
            _saveFailureLogged = true;
        }
    }
}
