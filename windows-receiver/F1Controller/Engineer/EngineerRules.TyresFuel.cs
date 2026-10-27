using System.Collections.Generic;
using System.Linq;

namespace F1Controller.Engineer;

public sealed partial class EngineerRules
{
    private static readonly string[] TyreKeys = { "tyre-hot-FL", "tyre-hot-FR", "tyre-hot-RL", "tyre-hot-RR" };
    private static readonly string[] TyreNames = { "Front left", "Front right", "Rear left", "Rear right" };

    // Session time at which each tyre first went over the hot limit in its current spell; null while it is not hot.
    private readonly float?[] _hotSince = new float?[4];
    private readonly bool[] _hotFired = new bool[4];
    // Session time at which the average tyre temperature went under the cold limit; null while it isn't
    private float? _coldSince;
    private bool _wearWarnArmed = true;
    private bool _wearBoxArmed = true;
    private bool _fuelTightArmed = true;
    private bool _fuelCriticalArmed = true;

    private void ResetTyresFuel()
    {
        System.Array.Clear(_hotSince);
        System.Array.Clear(_hotFired);
        _coldSince = null;
        _wearWarnArmed = true;
        _wearBoxArmed = true;
        _fuelTightArmed = true;
        _fuelCriticalArmed = true;
    }

    private IEnumerable<RadioCall> TyresFuel(RaceSnapshot? previous, RaceSnapshot current)
    {
        var calls = new List<RadioCall>();
        HotTyres(current, calls);
        ColdTyres(current, calls);
        TyreWear(previous, current, calls);
        Fuel(previous, current, calls);
        return calls;
    }

    /// <summary>
    /// A tyre is called once it has been over the limit for <see cref="EngineerThresholds.TyreHotSeconds"/>.
    /// Every other tyre that is over the limit at that moment joins the same call (a burnout heats
    /// both rears within seconds), and isn't called again until it has cooled down.
    /// </summary>
    private void HotTyres(RaceSnapshot current, List<RadioCall> calls)
    {
        var temps = current.Race.TyreTemp;
        bool anyDue = false;
        for (int i = 0; i < 4 && i < temps.Length; i++)
        {
            if (temps[i] <= _t.TyreHotC)
            {
                _hotSince[i] = null;
                _hotFired[i] = false;
                continue;
            }

            _hotSince[i] ??= current.SessionTime;
            if (!_hotFired[i] && current.SessionTime - _hotSince[i]!.Value >= _t.TyreHotSeconds)
                anyDue = true;
        }
        if (!anyDue) return;

        var hot = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            if (_hotSince[i] != null && !_hotFired[i])
            {
                _hotFired[i] = true;
                hot.Add(i);
            }
        }
        calls.Add(HotTyreCall(hot));
    }

    private static RadioCall HotTyreCall(List<int> hot)
    {
        var (key, text) = hot switch
        {
            [var i] => (TyreKeys[i], $"{TyreNames[i]} is overheating, manage it."),
            [0, 1] => ("tyre-hot-fronts", "Fronts are overheating, manage them."),
            [2, 3] => ("tyre-hot-rears", "Rears are overheating, manage them."),
            [0, 1, 2, 3] => ("tyre-hot-all", "All four tyres are overheating, manage them."),
            _ => ("tyre-hot-multi", $"{JoinNames(hot)} are overheating, manage them."),
        };
        return new RadioCall(key, text, RadioPriority.Important, RadioCategory.Tyres);
    }

    /// <summary>"Front right, rear left and rear right".</summary>
    private static string JoinNames(List<int> tyres)
    {
        var names = tyres.Select((t, n) => n == 0 ? TyreNames[t] : TyreNames[t].ToLowerInvariant()).ToList();
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    private void ColdTyres(RaceSnapshot current, List<RadioCall> calls)
    {
        var temps = current.Race.TyreTemp;
        if (temps.Length < 4 || current.Race.Timing.CurrentLap < 2 || current.SafetyCar != 0)
        {
            _coldSince = null;
            return;
        }
        double avg = (temps[0] + temps[1] + temps[2] + temps[3]) / 4.0;
        if (avg >= _t.TyreColdC)
        {
            _coldSince = null;
            return;
        }

        // A slow stretch after a push lap cools them briefly; only call it if it lasts
        _coldSince ??= current.SessionTime;
        if (current.SessionTime - _coldSince.Value >= _t.TyreColdSeconds)
            calls.Add(new RadioCall("tyre-cold", "Tyres are cold, push to get heat in.",
                RadioPriority.Important, RadioCategory.Tyres));
    }

    private void TyreWear(RaceSnapshot? previous, RaceSnapshot current, List<RadioCall> calls)
    {
        int worst = WorstWear(current, out int worstIndex);
        if (worst < _t.WearRearmPct)
        {
            _wearWarnArmed = true;
            _wearBoxArmed = true;
        }
        if (previous == null) return;

        int before = WorstWear(previous, out _);
        if (_wearWarnArmed && Crossed(before, worst, _t.WearWarnPct))
        {
            _wearWarnArmed = false;
            calls.Add(new RadioCall("tyre-wear-50", "Tyre wear at 50 percent.",
                RadioPriority.Info, RadioCategory.Tyres));
        }
        if (_wearBoxArmed && Crossed(before, worst, _t.WearBoxPct))
        {
            _wearBoxArmed = false;
            string axle = worstIndex < 2 ? "Fronts" : "Rears";
            calls.Add(new RadioCall("tyre-wear-70", $"{axle} at 70 percent, box soon.",
                RadioPriority.Important, RadioCategory.Tyres));
        }
    }

    private static int WorstWear(RaceSnapshot s, out int index)
    {
        int worst = 0;
        index = 0;
        var wear = s.Race.TyreWear;
        for (int i = 0; i < wear.Length; i++)
        {
            if (wear[i] > worst) { worst = wear[i]; index = i; }
        }
        return worst;
    }

    /// <summary>True when the value went from below the threshold to at-or-above it.</summary>
    private static bool Crossed(int before, int now, int threshold) => before < threshold && now >= threshold;

    private void Fuel(RaceSnapshot? previous, RaceSnapshot current, List<RadioCall> calls)
    {
        if (!current.IsRace) return;
        float now = current.Race.FuelLaps;
        if (now > 0f)
        {
            _fuelTightArmed = true;
            _fuelCriticalArmed = true;
        }
        if (previous == null) return;

        float before = previous.Race.FuelLaps;
        if (_fuelTightArmed && before >= _t.FuelTightLaps && now < _t.FuelTightLaps)
        {
            _fuelTightArmed = false;
            calls.Add(new RadioCall("fuel-tight", "Fuel's tight, start lifting and coasting.",
                RadioPriority.Important, RadioCategory.Fuel));
        }
        if (_fuelCriticalArmed && before >= _t.FuelCriticalLaps && now < _t.FuelCriticalLaps)
        {
            _fuelCriticalArmed = false;
            calls.Add(new RadioCall("fuel-critical", "We won't make it on fuel, lift and coast now.",
                RadioPriority.Critical, RadioCategory.Fuel));
        }
    }
}
