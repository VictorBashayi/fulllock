using System.Security.Cryptography;

namespace F1Controller.Security;

public static class PairingCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 10;

    public static string Generate() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    public static string? Normalize(string? input)
    {
        if (input is null) return null;
        var sb = new System.Text.StringBuilder(Length);
        foreach (var raw in input)
        {
            if (raw > 127) return null; // ToUpperInvariant folds some non-ASCII into the alphabet
            var c = char.ToUpperInvariant(raw);
            if (c is ' ' or '-') continue;
            c = c switch { 'I' or 'L' => '1', 'O' => '0', _ => c };
            if (!Alphabet.Contains(c)) return null;
            sb.Append(c);
        }
        return sb.Length == Length ? sb.ToString() : null;
    }

    public static string Format(string n) => $"{n[..4]}-{n[4..8]}-{n[8..]}";

    public static string ToQrText(string n) => "fulllock://pair?code=" + n;
}
