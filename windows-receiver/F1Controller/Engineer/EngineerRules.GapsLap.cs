using System.Collections.Generic;
using System.Globalization;
using F1Controller.Validation;

namespace F1Controller.Engineer;

public sealed partial class EngineerRules
{
    // Gap to the car ahead / behind at the start of each recent lap, oldest first.
    // Cleared when the position changes or a different car is ahead / behind (pit stops,
    // retirements), since the history is then about another car.
    private readonly List<int> _gapsAhead = new();
    private readonly List<int> _gapsBehind = new();
    private byte _gapPosition;
    private int _gapAheadCar = -1;
    private int _gapBehindCar = -1;

    private void ResetGapsLap()
    {
        _gapsAhead.Clear();
        _gapsBehind.Clear();
        _gapPosition = 0;
        _gapAheadCar = -1;
        _gapBehindCar = -1;
    }

    private IEnumerable<RadioCall> GapsLap(RaceSnapshot? previous, RaceSnapshot current)
    {
        var calls = new List<RadioCall>();
        var cur = current.Race.Timing;

        // The first snapshot counts as a lap start for gap sampling, so a trend
        // needs no extra lap to get going.
        if (current.IsRace)
            Gaps(cur, previous == null || cur.CurrentLap != previous.Race.Timing.CurrentLap, calls);
        if (previous == null) return calls;

        var prev = previous.Race.Timing;
        bool newLap = cur.CurrentLap != prev.CurrentLap;

        if (!prev.LapInvalid && cur.LapInvalid && !newLap)
            calls.Add(new RadioCall("lap-invalid", "Lap deleted, track limits.", RadioPriority.Important, RadioCategory.Lap));

        if (prev.BestLapMs != 0 && cur.BestLapMs != 0 && cur.BestLapMs < prev.BestLapMs)
            calls.Add(new RadioCall("pb", $"Personal best, {FormatLap(cur.BestLapMs)}.", RadioPriority.Info, RadioCategory.Lap));

        Sector("sector-1", prev.S1Status, cur.S1Status, calls);
        Sector("sector-2", prev.S2Status, cur.S2Status, calls);

        byte totalLaps = current.Race.TotalLaps;
        if (current.IsRace && totalLaps > 0 && newLap && cur.CurrentLap == totalLaps)
            calls.Add(new RadioCall("last-lap", "Last lap, bring it home.", RadioPriority.Info, RadioCategory.Lap));

        return calls;
    }

    private void Gaps(RaceInfo cur, bool newLap, List<RadioCall> calls)
    {
        bool moved = cur.Position != _gapPosition;
        if (moved || cur.CarAheadIdx != _gapAheadCar) _gapsAhead.Clear();
        if (moved || cur.CarBehindIdx != _gapBehindCar) _gapsBehind.Clear();
        _gapPosition = cur.Position;
        _gapAheadCar = cur.CarAheadIdx;
        _gapBehindCar = cur.CarBehindIdx;
        if (!newLap) return;

        if (Trend(_gapsAhead, cur.GapAheadMs, _t.CatchMaxGapMs) is float aheadRate)
            calls.Add(new RadioCall("gap-ahead",
                $"You're catching the car ahead, {Seconds(aheadRate)} a lap. Gap {Seconds(cur.GapAheadMs!.Value)}.",
                RadioPriority.Info, RadioCategory.Gaps));

        if (Trend(_gapsBehind, cur.GapBehindMs, _t.ClosingMaxGapMs) is float behindRate)
            calls.Add(new RadioCall("gap-behind",
                $"Car behind is closing, {Seconds(behindRate)} a lap.",
                RadioPriority.Info, RadioCategory.Gaps));
    }

    /// <summary>
    /// Records this lap's gap and returns the average shrink per lap (ms) when the gap shrank by
    /// at least <see cref="EngineerThresholds.CatchMinShrinkMs"/> on each of the last
    /// <see cref="EngineerThresholds.CatchMinLaps"/> laps and is under
    /// <paramref name="maxGapMs"/>; otherwise null.
    /// </summary>
    private float? Trend(List<int> history, int? gapMs, int maxGapMs)
    {
        if (gapMs is not int gap)
        {
            history.Clear();
            return null;
        }

        history.Add(gap);
        int needed = _t.CatchMinLaps + 1;
        if (history.Count > needed) history.RemoveAt(0);
        if (history.Count < needed || gap >= maxGapMs) return null;

        for (int i = 1; i < history.Count; i++)
            if (history[i - 1] - history[i] < _t.CatchMinShrinkMs) return null;

        return (history[0] - gap) / (float)(history.Count - 1);
    }

    private static void Sector(string key, RaceTracker.SectorStatus prev, RaceTracker.SectorStatus cur, List<RadioCall> calls)
    {
        if (cur == prev) return;
        if (cur == RaceTracker.SectorStatus.OverallBest)
            calls.Add(new RadioCall(key, "Purple sector.", RadioPriority.Chatter, RadioCategory.Lap));
        else if (cur == RaceTracker.SectorStatus.PersonalBest)
            calls.Add(new RadioCall(key, "Green sector.", RadioPriority.Chatter, RadioCategory.Lap));
    }

    private static string Seconds(float ms) => (ms / 1000f).ToString("0.0", CultureInfo.InvariantCulture);

    private static string FormatLap(uint ms) =>
        $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}";
}
