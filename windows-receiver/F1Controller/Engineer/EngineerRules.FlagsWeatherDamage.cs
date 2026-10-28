using System.Collections.Generic;

namespace F1Controller.Engineer;

public sealed partial class EngineerRules
{
    private static readonly string[] DamageKeys =
        { "damage-front-wing", "damage-rear-wing", "damage-floor", "damage-diffuser", "damage-sidepod" };
    private static readonly string[] DamageNames = { "Front wing", "Rear wing", "Floor", "Diffuser", "Sidepod" };

    // Damage % last announced (or repaired down to) per part, in DamageKeys order
    private readonly byte[] _damageBaseline = new byte[5];

    private void ResetFlagsWeatherDamage() => System.Array.Clear(_damageBaseline);

    private IEnumerable<RadioCall> FlagsWeatherDamage(RaceSnapshot? previous, RaceSnapshot current)
    {
        var calls = new List<RadioCall>();
        var parts = DamageParts(current.Damage);
        if (previous == null)
        {
            parts.CopyTo(_damageBaseline, 0);
            return calls;
        }

        SafetyCar(previous.SafetyCar, current.SafetyCar, calls);
        FiaFlag(previous.FiaFlag, current.FiaFlag, calls);

        if (current.Weather < 3 && current.RainInMinutes is int minutes && minutes <= _t.RainForecastMinutes)
            calls.Add(new RadioCall("rain-forecast", RainForecastText(minutes), RadioPriority.Important, RadioCategory.Weather));
        if (previous.Weather < 3 && current.Weather >= 3)
            calls.Add(new RadioCall("rain-now", "It's raining.", RadioPriority.Important, RadioCategory.Weather));

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] < _damageBaseline[i])
                _damageBaseline[i] = parts[i];  // repaired
            else if (parts[i] >= _damageBaseline[i] + _t.DamageStepPct)
            {
                _damageBaseline[i] = parts[i];
                calls.Add(new RadioCall(DamageKeys[i], $"{DamageNames[i]} damage, {parts[i]} percent.",
                    RadioPriority.Critical, RadioCategory.Damage));
            }
        }

        foreach (var e in current.Events)
            if (EventCall(e) is RadioCall call) calls.Add(call);

        return calls;
    }

    private static string RainForecastText(int minutes) => minutes switch
    {
        <= 0 => "Rain expected any minute now.",
        1 => "Rain expected in 1 minute.",
        _ => $"Rain expected in {minutes} minutes.",
    };

    private static byte[] DamageParts(Damage d) => new[] { d.FrontWing, d.RearWing, d.Floor, d.Diffuser, d.Sidepod };

    // Safety car status: 0 none, 1 full, 2 virtual, 3 formation
    private static void SafetyCar(byte prev, byte cur, List<RadioCall> calls)
    {
        if (cur == prev) return;
        if (cur == 1)
            calls.Add(new RadioCall("sc-deployed", "Safety car deployed.", RadioPriority.Critical, RadioCategory.Flags));
        else if (cur == 2)
            calls.Add(new RadioCall("sc-virtual", "Virtual safety car.", RadioPriority.Critical, RadioCategory.Flags));
        else if (cur == 0 && prev is 1 or 2)
            calls.Add(new RadioCall("sc-in", "Safety car is in, get ready.", RadioPriority.Critical, RadioCategory.Flags));
    }

    // FIA flag: -1 unknown, 0 none, 1 green, 2 blue, 3 yellow, 4 red
    private static void FiaFlag(sbyte prev, sbyte cur, List<RadioCall> calls)
    {
        if (cur == prev) return;
        switch (cur)
        {
            case 4: calls.Add(new RadioCall("flag-red", "Red flag.", RadioPriority.Critical, RadioCategory.Flags)); break;
            case 3: calls.Add(new RadioCall("flag-yellow", "Yellow flag, careful.", RadioPriority.Important, RadioCategory.Flags)); break;
            case 2: calls.Add(new RadioCall("flag-blue", "Blue flags, let them by.", RadioPriority.Important, RadioCategory.Flags)); break;
        }
    }

    private static RadioCall? EventCall(GameEvent e)
    {
        if (e.Kind == GameEventKind.DrsEnabled)
            return new RadioCall("drs", "DRS enabled.", RadioPriority.Info, RadioCategory.Other);

        // Penalty types: 0 drive through, 1 stop-go, 4 time penalty, 6 disqualified
        string? text = e.PenaltyType switch
        {
            4 => $"{e.Seconds} second penalty.",
            1 => $"Stop-go penalty, {e.Seconds} seconds.",
            0 => "Drive through penalty.",
            6 => "We've been disqualified.",
            _ => null,
        };
        return text == null ? null : new RadioCall("penalty", text, RadioPriority.Important, RadioCategory.Other);
    }
}
