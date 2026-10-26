namespace F1Controller.Engineer;

/// <summary>Worst-case car damage percentages. FrontWing = max of the FL/FR wings.</summary>
public readonly record struct Damage(byte FrontWing, byte RearWing, byte Floor, byte Diffuser, byte Sidepod);

public enum GameEventKind { DrsEnabled, Penalty }

public readonly record struct GameEvent(GameEventKind Kind, byte PenaltyType = 0, byte Seconds = 0);
