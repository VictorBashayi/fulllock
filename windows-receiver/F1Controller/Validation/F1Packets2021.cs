using F1Controller.Engineer;

namespace F1Controller.Validation;

/// <summary>
/// Pure parsers for F1 2021 UDP packets. All bodies are the bytes AFTER the 24-byte header.
/// </summary>
public static class F1Packets2021
{
    public readonly record struct SessionInfo(byte Weather, byte TotalLaps, byte SessionType, byte SafetyCar, int? RainInMinutes, sbyte TrackId = -1);

    // Session body: weather @0, totalLaps @3, sessionType @6, trackId @7 (i8, -1 unknown), numMarshalZones @18,
    // marshalZones[21x5] @19, safetyCarStatus @124, numWeatherForecastSamples @126,
    // samples[56x8] @127, each { sessionType u8 @0, timeOffset u8 (min) @1, weather u8 @2, ... }
    public static SessionInfo? ParseSession(ReadOnlySpan<byte> body)
    {
        if (body.Length < 127) return null;
        byte sessionType = body[6];
        int count = Math.Min((int)body[126], 56);
        int? rain = null;
        for (int i = 0; i < count; i++)
        {
            int o = 127 + i * 8;
            if (o + 3 > body.Length) break;
            if (body[o] != sessionType || body[o + 2] < 3) continue;
            int offset = body[o + 1];
            if (rain is null || offset < rain) rain = offset;
        }
        return new SessionInfo(body[0], body[3], sessionType, body[124], rain, unchecked((sbyte)body[7]));
    }

    // Event body: code[4] ASCII @0. PENA: penaltyType @4, infringementType @5, vehicleIdx @6,
    // otherVehicleIdx @7, time @8.
    // Penalty types: 0 drive-through, 1 stop-go, 4 time penalty, 6 disqualified.
    public static GameEvent? ParseEvent(ReadOnlySpan<byte> body, byte playerIdx)
    {
        if (body.Length < 4) return null;
        var code = body[..4];
        if (code.SequenceEqual("DRSE"u8)) return new GameEvent(GameEventKind.DrsEnabled);
        if (code.SequenceEqual("PENA"u8) && body.Length >= 9)
        {
            byte type = body[4];
            if (body[6] != playerIdx) return null;
            if (type is 0 or 1 or 4 or 6) return new GameEvent(GameEventKind.Penalty, type, body[8]);
        }
        return null;
    }

    // Car damage entry (39 bytes): FL wing @24, FR wing @25, rear wing @26, floor @27,
    // diffuser @28, sidepod @29.
    public static Damage ParseDamage(ReadOnlySpan<byte> carEntry) =>
        new(Math.Max(carEntry[24], carEntry[25]), carEntry[26], carEntry[27], carEntry[28], carEntry[29]);

    // Car status entry: vehicleFiaFlags (int8) @28.
    public static sbyte FiaFlag(ReadOnlySpan<byte> carStatusEntry) => (sbyte)carStatusEntry[28];
}
