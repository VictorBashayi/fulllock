using System;
using System.Buffers.Binary;

namespace F1Controller.Validation;

/// <summary>
/// Lap timing derived from F1 2021 Lap Data packets (ID 2), which carry every
/// car's position, lap times, current sector and distance. F1 2021 doesn't send
/// best laps, sector colours, live delta or gaps, so they're worked out here:
///   - sectors: a car's sector time is final when its current sector advances
///   - delta: the player's lap time is recorded every 10 m; the best valid
///     lap's trace is the reference
///   - gaps: each car's (session time, total distance) history; the gap to a
///     car ahead is how long ago it was where the player is now
/// </summary>
public sealed class RaceTracker
{
    private const int LapDataEntrySize = 43; // F1 2021
    private const int MaxCars = 22;
    private const float BinMetres = 10f;
    private const int MaxBins = 1000;        // 10 km, longer than any track
    private const int HistorySize = 2048;    // ~30 s at 60 Hz

    /// <summary>Sector colour, as on the F1 timing screens.</summary>
    public enum SectorStatus : byte { None = 0, Slower = 1, PersonalBest = 2, OverallBest = 3 }

    private sealed class Car
    {
        public bool Seen;
        public byte Sector;
        public byte LapNum;
        public bool LapInvalid;
        public ushort S1, S2;

        // Ring buffer of (session time, total distance) for gap calculation
        public readonly float[] Time = new float[HistorySize];
        public readonly float[] Dist = new float[HistorySize];
        public int Head, Count;

        public void Add(float t, float d)
        {
            Time[Head] = t;
            Dist[Head] = d;
            Head = (Head + 1) % HistorySize;
            if (Count < HistorySize) Count++;
        }

        /// <summary>Session time when this car was at <paramref name="dist"/>, interpolated; null if out of range.</summary>
        public float? TimeAt(float dist)
        {
            // Walk back from newest: find the first sample at or before dist
            int newer = -1;
            for (int n = 0; n < Count; n++)
            {
                int i = (Head - 1 - n + HistorySize) % HistorySize;
                if (Dist[i] <= dist)
                {
                    if (newer < 0) return null; // car hasn't reached dist yet
                    float span = Dist[newer] - Dist[i];
                    float f = span > 0 ? (dist - Dist[i]) / span : 0;
                    return Time[i] + f * (Time[newer] - Time[i]);
                }
                newer = i;
            }
            return null; // further back than the history goes
        }
    }

    private readonly Car[] _cars = new Car[MaxCars];
    private float _lastSessionTime = -1;

    // Overall (all cars) and player best sectors / lap, in ms; 0 = none yet
    private uint _bestS1All, _bestS2All, _bestS3All;
    private uint _myBestS1, _myBestS2, _myBestS3, _myBestLap;

    // Player lap-time traces indexed by 10 m bin; -1 = not recorded
    private int[] _curTrace = NewTrace();
    private int[] _bestTrace = NewTrace();

    public RaceTracker()
    {
        for (int i = 0; i < MaxCars; i++) _cars[i] = new Car();
    }

    /// <summary>Latest timing for the player car.</summary>
    public RaceInfo Info { get; private set; }

    public void Reset()
    {
        for (int i = 0; i < MaxCars; i++) _cars[i] = new Car();
        Array.Clear(_positions);
        _bestS1All = _bestS2All = _bestS3All = 0;
        _myBestS1 = _myBestS2 = _myBestS3 = _myBestLap = 0;
        _curTrace = NewTrace();
        _bestTrace = NewTrace();
        Info = default;
    }

    private const float RestartMaxSessionSeconds = 10f;
    private const float ReorderToleranceSeconds = 1f;

    private void Rewind()
    {
        for (int i = 0; i < MaxCars; i++) _cars[i] = new Car();
        _curTrace = NewTrace();
    }

    /// <param name="data">The whole Lap Data packet.</param>
    public void Process(ReadOnlySpan<byte> data, int headerSize, int playerIdx, float sessionTime)
    {
        // A new session or a restart starts the clock again near zero: forget everything. A
        // flashback rewinds it mid-session: keep the bests, but the cars' distance histories and
        // this lap's trace now run ahead of the clock, so drop those. A step back of a second or
        // less is a packet that arrived out of order.
        if (sessionTime < _lastSessionTime)
        {
            if (sessionTime < RestartMaxSessionSeconds) Reset();
            else if (_lastSessionTime - sessionTime <= ReorderToleranceSeconds) return;
            else Rewind();
        }
        bool timeAdvanced = sessionTime > _lastSessionTime; // false while paused
        _lastSessionTime = sessionTime;

        if (data.Length < headerSize + MaxCars * LapDataEntrySize) return;

        var info = Info;
        for (int idx = 0; idx < MaxCars; idx++)
        {
            var e = data.Slice(headerSize + idx * LapDataEntrySize, LapDataEntrySize);
            byte resultStatus = e[36];
            _positions[idx] = resultStatus >= 2 ? e[24] : (byte)0;
            _pitStatus[idx] = e[26];
            if (resultStatus < 2) continue; // invalid / inactive slot

            uint lastLap = BinaryPrimitives.ReadUInt32LittleEndian(e);
            uint curLap = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            ushort s1 = BinaryPrimitives.ReadUInt16LittleEndian(e[8..]);
            ushort s2 = BinaryPrimitives.ReadUInt16LittleEndian(e[10..]);
            float lapDist = BinaryPrimitives.ReadSingleLittleEndian(e[12..]);
            float totalDist = BinaryPrimitives.ReadSingleLittleEndian(e[16..]);
            byte lapNum = e[25];
            byte sector = e[28];
            bool invalid = e[29] != 0;
            bool isPlayer = idx == playerIdx;

            var car = _cars[idx];
            if (!car.Seen)
            {
                car.Seen = true;
                car.Sector = sector;
                car.LapNum = lapNum;
            }

            if (sector != car.Sector)
            {
                if (car.Sector == 0 && sector == 1)
                {
                    car.S1 = s1;
                    if (!car.LapInvalid)
                    {
                        Better(ref _bestS1All, s1);
                        if (isPlayer) Better(ref _myBestS1, s1);
                    }
                    if (isPlayer) info = info with { S1 = s1, S1Status = Status(s1, _bestS1All, _myBestS1, car.LapInvalid) };
                }
                else if (car.Sector == 1 && sector == 2)
                {
                    car.S2 = s2;
                    if (!car.LapInvalid)
                    {
                        Better(ref _bestS2All, s2);
                        if (isPlayer) Better(ref _myBestS2, s2);
                    }
                    if (isPlayer) info = info with { S2 = s2, S2Status = Status(s2, _bestS2All, _myBestS2, car.LapInvalid) };
                }
                car.Sector = sector;
            }

            if (lapNum != car.LapNum)
            {
                // Lap complete: lastLap is final now, S3 is what's left of it
                if (lastLap > 0 && car.S1 > 0 && car.S2 > 0 && !car.LapInvalid)
                {
                    uint s3 = lastLap - car.S1 - car.S2;
                    Better(ref _bestS3All, s3);
                    if (isPlayer) Better(ref _myBestS3, s3);
                }
                if (isPlayer)
                {
                    if (lastLap > 0 && !car.LapInvalid && (_myBestLap == 0 || lastLap < _myBestLap))
                    {
                        _myBestLap = lastLap;
                        (_bestTrace, _curTrace) = (_curTrace, _bestTrace);
                    }
                    Array.Fill(_curTrace, -1);
                    info = info with { S1 = 0, S2 = 0, S1Status = SectorStatus.None, S2Status = SectorStatus.None };
                }
                car.LapNum = lapNum;
                car.S1 = car.S2 = 0;
            }
            car.LapInvalid = invalid;

            if (timeAdvanced) car.Add(sessionTime, totalDist);

            if (isPlayer)
            {
                // Each bin holds the lap time when the car first entered it; the
                // reference time at lapDist is interpolated between two bins.
                int? delta = null;
                int bin = float.IsFinite(lapDist) && lapDist >= 0 ? (int)(lapDist / BinMetres) : -1;
                if (bin >= 0 && bin < MaxBins - 1)
                {
                    if (_curTrace[bin] < 0) _curTrace[bin] = (int)curLap;
                    int a = _bestTrace[bin], b = _bestTrace[bin + 1];
                    if (a >= 0 && b >= a)
                    {
                        float f = (lapDist - bin * BinMetres) / BinMetres;
                        delta = (int)(curLap - (a + f * (b - a)));
                    }
                }
                info = info with
                {
                    Position = e[24],
                    CurrentLap = lapNum,
                    Sector = sector,
                    LapInvalid = invalid,
                    CurrentLapMs = curLap,
                    LastLapMs = lastLap,
                    BestLapMs = _myBestLap,
                    DeltaMs = delta,
                    Finished = resultStatus >= 3, // 3 finished, 4 DNF, 5 DSQ, 6 not classified, 7 retired
                    PitStatus = e[26],
                    NumPitStops = e[27],
                };
            }
        }

        int aheadIdx = info.Position == 0 ? -1 : FindByPosition(info.Position - 1);
        int behindIdx = info.Position == 0 ? -1 : FindByPosition(info.Position + 1);
        info = info with
        {
            GapAheadMs = Gap(playerIdx, info.Position, ahead: true),
            GapBehindMs = Gap(playerIdx, info.Position, ahead: false),
            CarAheadIdx = aheadIdx,
            CarBehindIdx = behindIdx,
            CarAheadInPit = aheadIdx >= 0 && _pitStatus[aheadIdx] != 0,
            CarBehindInPit = behindIdx >= 0 && _pitStatus[behindIdx] != 0,
        };
        Info = info;
    }

    private int? Gap(int playerIdx, byte position, bool ahead)
    {
        if (playerIdx < 0 || playerIdx >= MaxCars || position == 0) return null;
        int other = FindByPosition(ahead ? position - 1 : position + 1);
        if (other < 0) return null;

        var me = _cars[playerIdx];
        var them = _cars[other];
        if (me.Count == 0 || them.Count == 0) return null;
        float now = _lastSessionTime;

        // Ahead: how long ago they were where I am. Behind: how long ago I was where they are.
        float? then = ahead
            ? them.TimeAt(me.Dist[(me.Head - 1 + HistorySize) % HistorySize])
            : me.TimeAt(them.Dist[(them.Head - 1 + HistorySize) % HistorySize]);
        return then is float t ? (int)((now - t) * 1000) : null;
    }

    // Race position per car index from the latest packet; 0 = inactive
    private readonly byte[] _positions = new byte[MaxCars];
    // Pit status per car index from the latest packet: 0 none, 1 pitting, 2 in the pit area
    private readonly byte[] _pitStatus = new byte[MaxCars];

    private int FindByPosition(int position)
    {
        if (position < 1) return -1;
        for (int i = 0; i < MaxCars; i++)
            if (_cars[i].Seen && _positions[i] == position) return i;
        return -1;
    }

    private static SectorStatus Status(uint time, uint overallBest, uint myBest, bool invalid)
    {
        if (invalid) return SectorStatus.Slower;
        if (time <= overallBest) return SectorStatus.OverallBest;
        if (time <= myBest) return SectorStatus.PersonalBest;
        return SectorStatus.Slower;
    }

    private static void Better(ref uint best, uint time)
    {
        if (time > 0 && (best == 0 || time < best)) best = time;
    }

    private static int[] NewTrace()
    {
        var t = new int[MaxBins];
        Array.Fill(t, -1);
        return t;
    }
}

/// <summary>
/// Player timing. Times in ms; 0 / null = not available yet. CarAheadIdx / CarBehindIdx are
/// the car indexes one place up / down (-1 = none). Finished: the player has a result
/// (finished, DNF, DSQ, not classified or retired). PitStatus: 0 none, 1 pitting, 2 in the
/// pit area; CarAheadInPit / CarBehindInPit: that car's pit status isn't 0.
/// </summary>
public readonly record struct RaceInfo(
    byte Position,
    byte CurrentLap,
    byte Sector,
    bool LapInvalid,
    uint CurrentLapMs,
    uint LastLapMs,
    uint BestLapMs,
    int? DeltaMs,
    ushort S1,
    ushort S2,
    RaceTracker.SectorStatus S1Status,
    RaceTracker.SectorStatus S2Status,
    int? GapAheadMs,
    int? GapBehindMs,
    int CarAheadIdx = -1,
    int CarBehindIdx = -1,
    bool Finished = false,
    byte PitStatus = 0,
    byte NumPitStops = 0,
    bool CarAheadInPit = false,
    bool CarBehindInPit = false);
