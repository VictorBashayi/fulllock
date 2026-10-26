using System.Collections.Generic;
using F1Controller.Validation;

namespace F1Controller.Engineer;

/// <summary>Worst-case car damage percentages. FrontWing = max of the FL/FR wings.</summary>
public readonly record struct Damage(byte FrontWing, byte RearWing, byte Floor, byte Diffuser, byte Sidepod);

public enum GameEventKind { DrsEnabled, Penalty }

public readonly record struct GameEvent(GameEventKind Kind, byte PenaltyType = 0, byte Seconds = 0);

/// <summary>Everything the engineer needs from one Lap Data tick. Events are those received since the previous snapshot.</summary>
public sealed record RaceSnapshot(
    float SessionTime,
    ulong SessionUid,
    RaceState Race,
    bool IsRace,
    byte Weather,
    int? RainInMinutes,
    byte SafetyCar,
    sbyte FiaFlag,
    Damage Damage,
    IReadOnlyList<GameEvent> Events);
