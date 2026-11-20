using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace F1Controller.Validation;

/// <summary>
/// Personal-best laps, one per track / session type / dry|wet, kept in a small JSON file.
/// A PB only ever gets faster. Save problems (locked file, missing folder) never throw: the
/// value stays in memory and is retried on the next PB.
/// </summary>
public sealed class PbStore
{
    /// <summary>F1 2021 session type for Time Trial (seen in real telemetry).</summary>
    public const byte TimeTrialSession = 13;

    /// <summary>A PB lap; S1/S2 in ms, 0 = not recorded (older entries). Default = no PB.</summary>
    public readonly record struct PbLap(uint Ms, ushort S1, ushort S2);
    public readonly record struct TrackPbs(sbyte TrackId, PbLap Dry, PbLap Wet);

    private sealed record Entry(uint Ms, string Date, ushort S1 = 0, ushort S2 = 0);

    /// <summary>Raised after a new PB is stored (outside the lock).</summary>
    public event Action? Changed;

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
    public bool TryRecord(sbyte trackId, byte sessionType, bool wet, uint lapMs, ushort s1 = 0, ushort s2 = 0)
    {
        if (trackId < 0 || lapMs == 0) return false;
        lock (_lock)
        {
            var key = Key(trackId, sessionType, wet);
            if (_pbs.TryGetValue(key, out var old) && lapMs >= old.Ms) return false;
            _pbs[key] = new Entry(lapMs, DateTime.Now.ToString("yyyy-MM-dd"), s1, s2);
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Every track with a PB for <paramref name="sessionType"/>, by track id.</summary>
    public IReadOnlyList<TrackPbs> List(byte sessionType)
    {
        var tracks = new SortedDictionary<sbyte, TrackPbs>();
        lock (_lock)
        {
            foreach (var (key, e) in _pbs)
            {
                var parts = key.Split('/');
                if (parts.Length != 3 || !sbyte.TryParse(parts[0], out var track)
                    || !byte.TryParse(parts[1], out var type) || type != sessionType) continue;
                var lap = new PbLap(e.Ms, e.S1, e.S2);
                var t = tracks.GetValueOrDefault(track, new TrackPbs(track, default, default));
                tracks[track] = parts[2] == "wet" ? t with { Wet = lap } : t with { Dry = lap };
            }
        }
        return tracks.Values.ToList();
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
