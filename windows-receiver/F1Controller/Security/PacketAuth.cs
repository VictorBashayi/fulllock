using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace F1Controller.Security;

public static class PacketAuth
{
    public const int TagSize = 8, CtrSize = 4, TrailerSize = 12, ContextSize = 16;
    public const byte DirPhoneToPc = 0x01, DirPcToPhone = 0x02;

    public static byte[] DeriveKey(string normalizedCode) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(normalizedCode), Encoding.ASCII.GetBytes("FullLock-pair-v1"),
            200_000, HashAlgorithmName.SHA256, 32);

    static void Tag(byte[] key, byte dir, ReadOnlySpan<byte> ctx, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> ctrLe, Span<byte> tag8)
    {
        using var h = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        h.AppendData([dir]);
        h.AppendData(ctx);
        h.AppendData(payload);
        h.AppendData(ctrLe);
        Span<byte> full = stackalloc byte[32];
        h.GetHashAndReset(full);
        full[..TagSize].CopyTo(tag8);
    }

    public static int Seal(byte[] key, byte dir, ReadOnlySpan<byte> ctx, ReadOnlySpan<byte> payload, uint ctr, Span<byte> dest)
    {
        if (ctx.Length != 0 && ctx.Length != ContextSize) throw new ArgumentException("ctx must be 0 or 16 bytes", nameof(ctx));
        int total = payload.Length + TrailerSize;
        if (dest.Length < total) throw new ArgumentException("dest too small", nameof(dest));
        payload.CopyTo(dest);
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(payload.Length, CtrSize), ctr);
        Tag(key, dir, ctx, payload, dest.Slice(payload.Length, CtrSize), dest.Slice(payload.Length + CtrSize, TagSize));
        return total;
    }

    public static bool TryOpen(byte[] key, byte dir, ReadOnlySpan<byte> ctx, ReadOnlySpan<byte> datagram, out int payloadLength, out uint ctr)
    {
        payloadLength = 0; ctr = 0;
        if (datagram.Length < TrailerSize || (ctx.Length != 0 && ctx.Length != ContextSize)) return false;
        int n = datagram.Length - TrailerSize;
        Span<byte> expected = stackalloc byte[TagSize];
        Tag(key, dir, ctx, datagram[..n], datagram.Slice(n, CtrSize), expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, datagram.Slice(n + CtrSize, TagSize))) return false;
        payloadLength = n;
        ctr = BinaryPrimitives.ReadUInt32LittleEndian(datagram.Slice(n, CtrSize));
        return true;
    }
}
