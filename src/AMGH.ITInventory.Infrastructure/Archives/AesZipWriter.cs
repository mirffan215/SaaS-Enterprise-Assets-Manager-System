using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.CompanyAdmin;

namespace AMGH.ITInventory.Infrastructure.Archives;

/// <summary>
/// Writes a .zip archive whose entries are encrypted with WinZip AES-256 (AE-2): PBKDF2-HMAC-SHA1 key derivation, AES in
/// counter mode and an HMAC-SHA1 authentication code per entry. 7-Zip, WinZip, Keka and most archivers open it with the
/// password. (Windows Explorer cannot open AES zips; use one of those tools.) No third-party packages.
/// Each entry is held in memory while it is compressed and encrypted; the archive itself is streamed to <c>output</c>.
/// </summary>
internal sealed class AesZipWriter : IEncryptedArchiveWriter
{
    private const int SaltLength = 16;          // AES-256
    private const int KeyLength = 32;
    private const int Iterations = 1000;        // fixed by the WinZip AES specification
    private const ushort MethodAes = 99;
    private const ushort MethodDeflate = 8;
    private const ushort Flags = 0x0001 | 0x0800; // encrypted, UTF-8 names

    private sealed record Written(byte[] Name, uint Offset, uint CompressedSize, uint UncompressedSize, ushort Time, ushort Date);

    public void Write(Stream output, string password, IReadOnlyList<ArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (entries.Count > ushort.MaxValue) throw new InvalidOperationException("Too many files for a zip archive.");

        var (time, date) = DosTime(DateTime.UtcNow);
        var written = new List<Written>(entries.Count);
        long position = 0;
        foreach (var entry in entries)
        {
            var name = Encoding.UTF8.GetBytes(entry.Name);
            var payload = Encrypt(Compress(entry.Content), password);
            if (position > uint.MaxValue || (long)payload.Length > uint.MaxValue || (long)entry.Content.Length > uint.MaxValue)
                throw new InvalidOperationException("The archive is too large for the zip format (4 GB).");
            var offset = (uint)position;
            position += WriteLocalHeader(output, name, (uint)payload.Length, (uint)entry.Content.Length, time, date);
            output.Write(payload);
            position += payload.Length;
            written.Add(new Written(name, offset, (uint)payload.Length, (uint)entry.Content.Length, time, date));
        }

        var directoryStart = position;
        foreach (var w in written) position += WriteCentralHeader(output, w);
        WriteEnd(output, (ushort)written.Count, (uint)(position - directoryStart), (uint)directoryStart);
        output.Flush();
    }

    private static byte[] Compress(byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(content);
        return buffer.ToArray();
    }

    /// <summary>
    /// salt + 2-byte password verifier + AES-CTR ciphertext + 10-byte HMAC-SHA1 authentication code.
    /// PBKDF2-HMAC-SHA1 (1000 rounds) and HMAC-SHA1 are what the WinZip AES format prescribes so that archivers can open the file;
    /// the archive password is a random 24-character value, not a human-chosen one.
    /// </summary>
#pragma warning disable CA5350, CA5379
    internal static byte[] Encrypt(byte[] data, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA1, 2 * KeyLength + 2);
        var encryptionKey = keys.AsSpan(0, KeyLength).ToArray();
        var authenticationKey = keys.AsSpan(KeyLength, KeyLength).ToArray();

        var result = new byte[SaltLength + 2 + data.Length + 10];
        salt.CopyTo(result, 0);
        keys[(2 * KeyLength)..].CopyTo(result, SaltLength);
        var cipher = result.AsSpan(SaltLength + 2, data.Length);
        Ctr(encryptionKey, data, cipher);
        var mac = HMACSHA1.HashData(authenticationKey, cipher);
        mac.AsSpan(0, 10).CopyTo(result.AsSpan(SaltLength + 2 + data.Length));
        return result;
    }
#pragma warning restore CA5350, CA5379

    /// <summary>AES in counter mode as WinZip defines it: a 128-bit little-endian counter that starts at 1, no nonce.</summary>
    internal static void Ctr(byte[] key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        Span<byte> counter = stackalloc byte[16];
        Span<byte> stream = stackalloc byte[16];
        ulong block = 0;
        for (var i = 0; i < input.Length; i += 16)
        {
            block++;
            BinaryPrimitives.WriteUInt64LittleEndian(counter, block);
            aes.EncryptEcb(counter, stream, PaddingMode.None);
            var n = Math.Min(16, input.Length - i);
            for (var j = 0; j < n; j++) output[i + j] = (byte)(input[i + j] ^ stream[j]);
        }
    }

    private static byte[] AesExtra() => new byte[]
    {
        0x01, 0x99,             // header id 0x9901
        0x07, 0x00,             // data size
        0x02, 0x00,             // AE-2
        (byte)'A', (byte)'E',   // vendor
        0x03,                   // AES-256
        MethodDeflate & 0xFF, MethodDeflate >> 8 // the real compression method
    };

    private static int WriteLocalHeader(Stream s, byte[] name, uint compressed, uint uncompressed, ushort time, ushort date)
    {
        var extra = AesExtra();
        Span<byte> h = stackalloc byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x04034B50);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], 51);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], MethodAes);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], date);
        BinaryPrimitives.WriteUInt32LittleEndian(h[14..], 0);          // AE-2: no CRC
        BinaryPrimitives.WriteUInt32LittleEndian(h[18..], compressed);
        BinaryPrimitives.WriteUInt32LittleEndian(h[22..], uncompressed);
        BinaryPrimitives.WriteUInt16LittleEndian(h[26..], (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)extra.Length);
        s.Write(h);
        s.Write(name);
        s.Write(extra);
        return 30 + name.Length + extra.Length;
    }

    private static int WriteCentralHeader(Stream s, Written w)
    {
        var extra = AesExtra();
        Span<byte> h = stackalloc byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x02014B50);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], 63);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], 51);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], MethodAes);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], w.Time);
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], w.Date);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], w.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[24..], w.UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)w.Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h[30..], (ushort)extra.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h[32..], 0);          // comment
        BinaryPrimitives.WriteUInt16LittleEndian(h[34..], 0);          // disk
        BinaryPrimitives.WriteUInt16LittleEndian(h[36..], 0);          // internal attributes
        BinaryPrimitives.WriteUInt32LittleEndian(h[38..], 0);          // external attributes
        BinaryPrimitives.WriteUInt32LittleEndian(h[42..], w.Offset);
        s.Write(h);
        s.Write(w.Name);
        s.Write(extra);
        return 46 + w.Name.Length + extra.Length;
    }

    private static void WriteEnd(Stream s, ushort count, uint size, uint offset)
    {
        Span<byte> h = stackalloc byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], count);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], size);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], offset);
        BinaryPrimitives.WriteUInt16LittleEndian(h[20..], 0);
        s.Write(h);
    }

    private static (ushort Time, ushort Date) DosTime(DateTime utc)
    {
        var t = utc.Year < 1980 ? new DateTime(1980, 1, 1) : utc;
        return ((ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2)), (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day));
    }
}
