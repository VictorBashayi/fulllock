namespace F1Controller.Engineer;

public enum RadioPriority : byte { Chatter = 0, Info = 1, Important = 2, Critical = 3 }

public enum RadioCategory : byte { Tyres = 0, Fuel = 1, Gaps = 2, Lap = 3, Flags = 4, Weather = 5, Damage = 6, Other = 7 }

public sealed record RadioCall(string Key, string Text, RadioPriority Priority, RadioCategory Category);
