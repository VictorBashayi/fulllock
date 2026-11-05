using F1Controller.Security;
using Xunit;

namespace F1Controller.Tests;

public class PacketAuthTests
{
    static readonly byte[] Ctx = Convert.FromHexString("01020304050607081112131415161718");
    static readonly byte[] V1 = Convert.FromHexString("010000000000000000000000004080000010");
    static readonly byte[] Key = PacketAuth.DeriveKey("K7QD9MXP2A");

    static byte[] Seal(byte dir, byte[] ctx, byte[] payload, uint ctr)
    {
        var d = new byte[payload.Length + PacketAuth.TrailerSize];
        Assert.Equal(d.Length, PacketAuth.Seal(Key, dir, ctx, payload, ctr, d));
        return d;
    }

    [Fact]
    public void DeriveKeyMatchesVector() =>
        Assert.Equal("60a20da146d5592c8a6ad61091a0551ecf63753c657085b111d631a5dbdc99d9", Convert.ToHexString(PacketAuth.DeriveKey("K7QD9MXP2A")).ToLowerInvariant());

    [Fact]
    public void SealMatchesVector1() => Assert.Equal(
        "010000000000000000000000004080000010" + "07000000" + "f363ac24a73c7d68",
        Convert.ToHexString(Seal(1, Ctx, V1, 7)).ToLowerInvariant());

    [Fact]
    public void SealMatchesVector2() => Assert.Equal(
        "ffffffff010000000000000000000000021112131415161718" + "01000000" + "5826a84a01af4962",
        Convert.ToHexString(Seal(1, [], Convert.FromHexString("ffffffff010000000000000000000000021112131415161718"), 1)).ToLowerInvariant());

    [Fact]
    public void SealMatchesVector3() => Assert.Equal(
        "feffffff033200000000000000000000" + "05000000" + "1338ee307bedb447",
        Convert.ToHexString(Seal(2, Ctx, Convert.FromHexString("feffffff033200000000000000000000"), 5)).ToLowerInvariant());

    [Fact]
    public void SealRejectsBadArgs()
    {
        Assert.Throws<ArgumentException>(() => PacketAuth.Seal(Key, 1, Ctx, V1, 1, new byte[V1.Length + 11]));
        Assert.Throws<ArgumentException>(() => PacketAuth.Seal(Key, 1, new byte[5], V1, 1, new byte[V1.Length + 12]));
    }

    [Fact]
    public void TryOpenRoundTrips()
    {
        var s = Seal(1, Ctx, V1, 7);
        Assert.True(PacketAuth.TryOpen(Key, 1, Ctx, s, out var len, out var ctr));
        Assert.Equal(V1.Length, len);
        Assert.Equal(7u, ctr);
        Assert.Equal(V1, s[..len]);
    }

    [Fact]
    public void TryOpenRejectsAnySingleBitFlip()
    {
        var s = Seal(1, Ctx, V1, 7);
        for (int i = 0; i < s.Length * 8; i++)
        {
            var c = (byte[])s.Clone();
            c[i / 8] ^= (byte)(1 << (i % 8));
            Assert.False(PacketAuth.TryOpen(Key, 1, Ctx, c, out _, out _), $"bit {i}");
        }
    }

    [Fact]
    public void TryOpenRejectsWrongDirAndCtx()
    {
        var s = Seal(1, Ctx, V1, 7);
        Assert.False(PacketAuth.TryOpen(Key, 2, Ctx, s, out _, out _));
        Assert.False(PacketAuth.TryOpen(Key, 1, [], s, out _, out _));
        Assert.False(PacketAuth.TryOpen(Key, 1, new byte[5], s, out _, out _));
    }

    [Fact]
    public void TryOpenRejectsShorterThanTrailer()
    {
        for (int n = 0; n < PacketAuth.TrailerSize; n++)
            Assert.False(PacketAuth.TryOpen(Key, 1, Ctx, new byte[n], out _, out _));
    }
}
