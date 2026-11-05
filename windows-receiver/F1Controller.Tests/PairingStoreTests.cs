using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using F1Controller.Security;

namespace F1Controller.Tests;

public sealed class PairingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fulllock-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "pairing.json");
    private static byte[] Id(byte[] b) => b;
    private static byte[] Xor(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void CreatesFileOnFirstRun()
    {
        var s = PairingStore.LoadOrCreate(FilePath, Id, Id);
        Assert.True(s.WasCreated);
        Assert.False(s.WasReset);
        Assert.True(File.Exists(FilePath));
        Assert.Equal(s.Code, PairingCode.Normalize(s.Code));
    }

    [Fact]
    public void LoadsSameCodeSecondTime()
    {
        var a = PairingStore.LoadOrCreate(FilePath, Id, Id);
        var b = PairingStore.LoadOrCreate(FilePath, Id, Id);
        Assert.False(b.WasCreated);
        Assert.False(b.WasReset);
        Assert.Equal(a.Code, b.Code);
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void CorruptFileMakesNewCode()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{not json");
        var s = PairingStore.LoadOrCreate(FilePath, Id, Id);
        Assert.True(s.WasCreated);
        Assert.True(s.WasReset);
        Assert.NotNull(PairingCode.Normalize(s.Code));
        Assert.Equal(s.Code, PairingStore.LoadOrCreate(FilePath, Id, Id).Code);
    }

    [Fact]
    public void UnprotectFailureMakesNewCode()
    {
        PairingStore.LoadOrCreate(FilePath, Id, Id);
        var s = PairingStore.LoadOrCreate(FilePath, Id, _ => throw new CryptographicException());
        Assert.True(s.WasCreated);
        Assert.True(s.WasReset);
    }

    [Fact]
    public void WrongVersionCountsAsCorrupt()
    {
        PairingStore.LoadOrCreate(FilePath, Id, Id);
        File.WriteAllText(FilePath, File.ReadAllText(FilePath).Replace("\"version\":1", "\"version\":2"));
        Assert.True(PairingStore.LoadOrCreate(FilePath, Id, Id).WasReset);
    }

    [Fact]
    public void CreateNewReplacesCode()
    {
        var a = PairingStore.LoadOrCreate(FilePath, Id, Id);
        var b = PairingStore.CreateNew(FilePath, Id, Id);
        Assert.True(b.WasCreated);
        Assert.False(b.WasReset);
        Assert.NotEqual(a.Code, b.Code);
        Assert.Equal(b.Code, PairingStore.LoadOrCreate(FilePath, Id, Id).Code);
    }

    [Fact]
    public void FileDoesNotContainPlainCode()
    {
        var s = PairingStore.LoadOrCreate(FilePath, Xor, Xor);
        Assert.DoesNotContain(s.Code, File.ReadAllText(FilePath));
        Assert.Equal(s.Code, PairingStore.LoadOrCreate(FilePath, Xor, Xor).Code);
    }
}
