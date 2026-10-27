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
        byte totalLaps = 50)
    {
        var timing = new RaceInfo(
            Position: position, CurrentLap: lap, Sector: 0, LapInvalid: lapInvalid,
            CurrentLapMs: 0, LastLapMs: 0, BestLapMs: bestLapMs, DeltaMs: null,
            S1: 0, S2: 0,
            S1Status: s1, S2Status: s2,
            GapAheadMs: gapAhead, GapBehindMs: gapBehind);
        var race = new RaceState(
            timing, TotalLaps: totalLaps, TyreCompound: 0, TyreAgeLaps: 0, FuelLaps: fuel,
            TyreWear: wear ?? new byte[] { 10, 10, 10, 10 },
            TyreTemp: temps ?? new byte[] { 90, 90, 90, 90 });
        return new RaceSnapshot(
            t, SessionUid: 1, race, isRace, Weather: 0, RainInMinutes: null,
            SafetyCar: sc, FiaFlag: 0, new Damage(0, 0, 0, 0, 0), Array.Empty<GameEvent>());
    }
}
