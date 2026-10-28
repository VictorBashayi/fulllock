using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace F1Controller.Engineer;

public sealed partial class EngineerRules
{
    // Sector times of the fastest valid lap that wasn't an in- or out-lap; null until there is one
    private (int S1, int S2, int S3, uint Lap)? _bestSectors;

    // Pit activity of the lap being driven: did it start in the pit lane, has it been in it at all
    private bool _lapStartedInPit;
    private bool _lapHadPit;

    private void ResetLapReport()
    {
        _bestSectors = null;
        _lapStartedInPit = false;
        _lapHadPit = false;
    }

    /// <summary>The lap number went up by exactly one: a lap was completed (not a flashback or a gap in the data).</summary>
    private static bool IsRollover(RaceSnapshot previous, RaceSnapshot current) =>
        current.Race.Timing.CurrentLap == previous.Race.Timing.CurrentLap + 1;

    /// <summary>
    /// Tracks pit activity per lap and, at a rollover, reports the completed lap when the
    /// chattiness says one is due: Quiet every 3 laps, Balanced every 2, Talkative every lap.
    /// </summary>
    private RadioCall? LapReport(RaceSnapshot? previous, RaceSnapshot current, int level, bool personalBest)
    {
        bool inPit = current.Race.Timing.PitStatus != 0;
        if (previous == null || current.Race.Timing.CurrentLap != previous.Race.Timing.CurrentLap)
        {
            RadioCall? report = previous != null && IsRollover(previous, current)
                ? CompleteLap(previous, current, level, personalBest)
                : null;
            _lapStartedInPit = inPit;
            _lapHadPit = inPit;
            return report;
        }
        _lapHadPit |= inPit;
        return null;
    }

    private RadioCall? CompleteLap(RaceSnapshot previous, RaceSnapshot current, int level, bool personalBest)
    {
        var last = previous.Race.Timing;
        int lapNumber = last.CurrentLap;
        uint lapMs = current.Race.Timing.LastLapMs;
        int s1 = last.S1, s2 = last.S2, s3 = (int)lapMs - s1 - s2;
        bool sectorsKnown = s1 > 0 && s2 > 0 && s3 > 0;
        bool pitLap = _lapHadPit;
        bool valid = !last.LapInvalid;

        int period = level switch { 1 => 3, 2 => 2, 3 => 1, _ => 0 };
        RadioCall? report = null;
        if (period > 0 && lapNumber >= 1 && lapNumber % period == 0)
        {
            var text = new StringBuilder();
            if (pitLap)
                text.Append(_lapStartedInPit ? "Out lap." : "In lap.");
            else if (!valid)
                text.Append($"Lap {lapNumber} deleted.");
            else if (lapMs == 0)
                text.Append($"Lap {lapNumber}.");  // the game hasn't filled in the time yet
            else
            {
                if (personalBest) text.Append("Personal best. ");
                text.Append($"Lap {lapNumber}, {FormatTenths(lapMs)}.");
                if (sectorsKnown && _bestSectors is { } best)
                    text.Append($" Sector 1 {SectorDelta(s1 - best.S1)}, sector 2 {SectorDelta(s2 - best.S2)}, sector 3 {SectorDelta(s3 - best.S3)}.");
            }
            if (current.IsRace) AppendGaps(text, current.Race.Timing.GapAheadMs, current.Race.Timing.GapBehindMs);
            report = new RadioCall("lap-report", text.ToString(), RadioPriority.Important, RadioCategory.Lap);
        }

        if (valid && !pitLap && sectorsKnown && (_bestSectors is not { } b || lapMs < b.Lap))
            _bestSectors = (s1, s2, s3, lapMs);
        return report;
    }

    private string SectorDelta(int deltaMs)
    {
        if (Math.Abs(deltaMs) < _t.SectorMatchedMs) return "matched";
        return (deltaMs < 0 ? "up " : "down ") + Seconds(Math.Abs(deltaMs));
    }

    private static void AppendGaps(StringBuilder text, int? aheadMs, int? behindMs)
    {
        if (aheadMs is int a && behindMs is int b)
            text.Append($" Gap ahead {Seconds(a)}, behind {Seconds(b)}.");
        else if (aheadMs is int a2)
            text.Append($" Gap ahead {Seconds(a2)}.");
        else if (behindMs is int b2)
            text.Append($" Gap behind {Seconds(b2)}.");
    }

    /// <summary>Lap time to the tenth, e.g. 79930 ms → "1:19.9".</summary>
    private static string FormatTenths(uint ms)
    {
        uint tenths = (ms + 50) / 100;
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 600}:{tenths % 600 / 10:00}.{tenths % 10}");
    }
}
