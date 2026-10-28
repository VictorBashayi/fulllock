using F1Controller.Engineer;
using F1Controller.Validation;

namespace F1Controller.Tests;

/// <summary>Builds race snapshots with neutral defaults so each test only states what it cares about.</summary>
internal static class Snapshots
{
    public static RaceSnapshot Make(
        float t = 100,
        byte lap = 5,
        byte[]? temps = null,
        byte[]? wear = null,
        float fuel = 1f,
        bool isRace = true,
        byte sc = 0,
        byte position = 1,
        int? gapAhead = null,
        int? gapBehind = null,
        bool lapInvalid = false,
        uint bestLapMs = 0,
        RaceTracker.SectorStatus s1 = RaceTracker.SectorStatus.None,
        RaceTracker.SectorStatus s2 = RaceTracker.SectorStatus.None,
        byte totalLaps = 50,
        byte weather = 0,
        int? rainIn = null,
        sbyte fiaFlag = 0,
        Damage damage = default,
        GameEvent[]? events = null,
        int carAhead = 7,
        int carBehind = 8,
        bool finished = false)
    {
        var timing = new RaceInfo(
            Position: position, CurrentLap: lap, Sector: 0, LapInvalid: lapInvalid,
            CurrentLapMs: 0, LastLapMs: 0, BestLapMs: bestLapMs, DeltaMs: null,
            S1: 0, S2: 0,
            S1Status: s1, S2Status: s2,
            GapAheadMs: gapAhead, GapBehindMs: gapBehind,
            CarAheadIdx: carAhead, CarBehindIdx: carBehind, Finished: finished);
        var race = new RaceState(
            timing, TotalLaps: totalLaps, TyreCompound: 0, TyreAgeLaps: 0, FuelLaps: fuel,
            TyreWear: wear ?? new byte[] { 10, 10, 10, 10 },
            TyreTemp: temps ?? new byte[] { 90, 90, 90, 90 });
        return new RaceSnapshot(
            t, SessionUid: 1, race, isRace, Weather: weather, RainInMinutes: rainIn,
            SafetyCar: sc, FiaFlag: fiaFlag, damage, events ?? Array.Empty<GameEvent>());
    }
}
