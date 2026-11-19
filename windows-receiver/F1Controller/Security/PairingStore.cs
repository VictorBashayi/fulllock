using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace F1Controller.Security;

/// <summary>Persists the PC's pair code, DPAPI-protected, so the console can show it again.</summary>
public sealed class PairingStore
{
    private static readonly byte[] Entropy = "FullLock-pair-v1"u8.ToArray();

    private sealed record FileModel(int version, string code);

    public string Code { get; }
    public byte[] Key { get; }
    public bool WasCreated { get; }
    public bool WasReset { get; }
    /// <summary>True when a phone completed pairing with this same code on an earlier run (the code need not be shown again).</summary>
    public bool PairedBefore { get; }

    private readonly string _markerPath;

    private PairingStore(string path, string code, bool created, bool reset)
    {
        _markerPath = path + ".paired";
        PairedBefore = !created && File.Exists(_markerPath);
        Code = code;
        Key = PacketAuth.DeriveKey(code);
        WasCreated = created;
        WasReset = reset;
    }

    /// <summary>Remembers that a phone paired with this code, so later runs keep it off-screen.</summary>
    public void MarkPaired()
    {
        try { File.WriteAllBytes(_markerPath, []); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [SupportedOSPlatform("windows")]
    public static PairingStore LoadOrCreate(string path) => LoadOrCreate(path, DpapiProtect, DpapiUnprotect);
    [SupportedOSPlatform("windows")]
    public static PairingStore CreateNew(string path) => CreateNew(path, DpapiProtect, DpapiUnprotect);

    public static PairingStore LoadOrCreate(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
    {
        var existed = File.Exists(path);
        if (existed)
        {
            try
            {
                var m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path));
                if (m is { version: 1, code: not null })
                {
                    var code = PairingCode.Normalize(Encoding.ASCII.GetString(unprotect(Convert.FromBase64String(m.code))));
                    if (code != null) return new PairingStore(path, code, false, false);
                }
            }
            catch (Exception) { } // any read/parse/unprotect failure -> fresh code below
        }
        return Create(path, protect, reset: existed);
    }

    public static PairingStore CreateNew(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect) =>
        Create(path, protect, reset: false);

    private static PairingStore Create(string path, Func<byte[], byte[]> protect, bool reset)
    {
        var code = PairingCode.Generate();
        var json = JsonSerializer.Serialize(new FileModel(1, Convert.ToBase64String(protect(Encoding.ASCII.GetBytes(code)))));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, overwrite: true);
        File.Delete(path + ".paired"); // a new code has never been paired
        return new PairingStore(path, code, true, reset);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] DpapiProtect(byte[] d) => ProtectedData.Protect(d, Entropy, DataProtectionScope.CurrentUser);
    [SupportedOSPlatform("windows")]
    private static byte[] DpapiUnprotect(byte[] d) => ProtectedData.Unprotect(d, Entropy, DataProtectionScope.CurrentUser);
}
