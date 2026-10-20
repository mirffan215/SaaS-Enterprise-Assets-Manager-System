using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Infrastructure.Archives;

/// <summary>
/// One large file in a WinZip AES-256 (AE-2) zip, streamed: the content is deflated and encrypted into a temporary file first (so the sizes
/// are known for the header), then copied. Opens in 7-Zip, WinZip and Keka with the password. Up to 4 GB (no zip64).
/// </summary>
internal sealed class AesZipFile : IBackupArchive
{
    private const int SaltLength = 16, KeyLength = 32, Iterations = 1000, AuthLength = 10;
    private const int Buffer = 256 * 1024;

    /// <summary>AES in counter mode (WinZip variant: little-endian 128-bit counter starting at 1, no nonce), usable chunk by chunk.</summary>
    private sealed class Ctr : IDisposable
    {
        private readonly Aes _aes = Aes.Create();
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _stream = new byte[16];
        private ulong _block;
        private int _used = 16;

        public Ctr(byte[] key) => _aes.Key = key;

        public void Transform(Span<byte> data)
        {
            for (var i = 0; i < data.Length; i++)
            {
                if (_used == 16)
                {
                    _block++;
                    BinaryPrimitives.WriteUInt64LittleEndian(_counter, _block);
                    _aes.EncryptEcb(_counter, _stream, PaddingMode.None);
                    _used = 0;
                }
                data[i] ^= _stream[_used++];
            }
        }

        public void Dispose() => _aes.Dispose();
    }

    /// <summary>A write-only stream that encrypts what is written to it, authenticates the ciphertext and counts it.</summary>
    private sealed class EncryptingStream : Stream
    {
        private readonly Stream _target;
        private readonly Ctr _ctr;
        private readonly IncrementalHash _mac;
        public long Length_ { get; private set; }

        public EncryptingStream(Stream target, Ctr ctr, IncrementalHash mac) { _target = target; _ctr = ctr; _mac = mac; }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var copy = buffer.ToArray();
            _ctr.Transform(copy);
            _mac.AppendData(copy);
            _target.Write(copy);
            Length_ += copy.Length;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

#pragma warning disable CA5350, CA5379 // PBKDF2-HMAC-SHA1 and HMAC-SHA1 are what the WinZip AES format prescribes
    private static (byte[] EncryptionKey, byte[] AuthenticationKey, byte[] Verifier) Derive(string password, byte[] salt)
    {
        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA1, 2 * KeyLength + 2);
        return (keys[..KeyLength], keys[KeyLength..(2 * KeyLength)], keys[(2 * KeyLength)..]);
    }

    public async Task WriteAsync(Stream output, string password, string entryName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(entryName);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var (encryptionKey, authenticationKey, verifier) = Derive(password, salt);
        var temp = Path.Combine(Path.GetTempPath(), "eams-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
        await using var payload = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, Buffer, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        long uncompressed = 0;
        using (var ctr = new Ctr(encryptionKey))
        using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authenticationKey))
        {
            await payload.WriteAsync(salt, cancellationToken);
            await payload.WriteAsync(verifier, cancellationToken);
            var encrypting = new EncryptingStream(payload, ctr, mac);
            await using (var deflate = new DeflateStream(encrypting, CompressionLevel.Fastest, leaveOpen: true))
            {
                var buffer = new byte[Buffer];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    uncompressed += read;
                    await deflate.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            await payload.WriteAsync(mac.GetHashAndReset().AsMemory(0, AuthLength), cancellationToken);
        }
        if (payload.Length > uint.MaxValue || uncompressed > uint.MaxValue)
            throw new InvalidOperationException("The backup is larger than 4 GB, which the zip format used here cannot hold.");

        var name = Encoding.UTF8.GetBytes(entryName);
        var extra = new byte[] { 0x01, 0x99, 0x07, 0x00, 0x02, 0x00, (byte)'A', (byte)'E', 0x03, 0x08, 0x00 };
        var now = DateTime.UtcNow;
        var time = (ushort)((now.Hour << 11) | (now.Minute << 5) | (now.Second / 2));
        var date = (ushort)(((Math.Max(now.Year, 1980) - 1980) << 9) | (now.Month << 5) | now.Day);
        const ushort flags = 0x0001 | 0x0800;

        var local = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(local, 0x04034B50);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 51);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), 99);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(10), time);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(12), date);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)uncompressed);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), (ushort)extra.Length);
        await output.WriteAsync(local, cancellationToken);
        await output.WriteAsync(name, cancellationToken);
        await output.WriteAsync(extra, cancellationToken);
        payload.Position = 0;
        await payload.CopyToAsync(output, Buffer, cancellationToken);

        var central = new byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(central, 0x02014B50);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 63);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 51);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), 99);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(12), time);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(14), date);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), (uint)uncompressed);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(30), (ushort)extra.Length);
        await output.WriteAsync(central, cancellationToken);
        await output.WriteAsync(name, cancellationToken);
        await output.WriteAsync(extra, cancellationToken);

        var directorySize = (uint)(central.Length + name.Length + extra.Length);
        var directoryOffset = (uint)(local.Length + name.Length + extra.Length + payload.Length);
        var end = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), directorySize);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), directoryOffset);
        await output.WriteAsync(end, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public async Task<Result> ReadAsync(Stream zip, string password, Stream output, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(output);
        if (!zip.CanSeek) return Result.Failure(Error.Validation("The file cannot be read."));
        var bad = Result.Failure(Error.Validation("This is not a backup file made here (or it is damaged)."));
        try
        {
            zip.Position = 0;
            var header = new byte[30];
            if (!await ReadExactAsync(zip, header, cancellationToken) || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x04034B50) return bad;
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
            long compressed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            if ((flags & 0x0001) == 0 || method != 99) return bad;
            var skip = new byte[nameLength + extraLength];
            if (!await ReadExactAsync(zip, skip, cancellationToken)) return bad;
            var dataStart = zip.Position;
            if (compressed < SaltLength + 2 + AuthLength || dataStart + compressed > zip.Length) return bad;

            var salt = new byte[SaltLength];
            var verifier = new byte[2];
            if (!await ReadExactAsync(zip, salt, cancellationToken) || !await ReadExactAsync(zip, verifier, cancellationToken)) return bad;
            var (encryptionKey, authenticationKey, expectedVerifier) = Derive(password, salt);
            if (!CryptographicOperations.FixedTimeEquals(verifier, expectedVerifier)) return Result.Failure(Error.Validation("The password is not correct."));

            // integrity first: the authentication code covers the whole ciphertext
            var cipherLength = compressed - SaltLength - 2 - AuthLength;
            using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authenticationKey))
            {
                var buffer = new byte[Buffer];
                for (var left = cipherLength; left > 0;)
                {
                    var n = await zip.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), cancellationToken);
                    if (n == 0) return bad;
                    mac.AppendData(buffer.AsSpan(0, n));
                    left -= n;
                }
                var stored = new byte[AuthLength];
                if (!await ReadExactAsync(zip, stored, cancellationToken)) return bad;
                if (!CryptographicOperations.FixedTimeEquals(stored, mac.GetHashAndReset().AsSpan(0, AuthLength)))
                    return Result.Failure(Error.Validation("The file is damaged or was changed: its integrity check failed."));
            }

            // then decrypt and inflate
            zip.Position = dataStart + SaltLength + 2;
            await using var decrypting = new DecryptingReadStream(zip, cipherLength, new Ctr(encryptionKey));
            await using var inflate = new DeflateStream(decrypting, CompressionMode.Decompress);
            var chunk = new byte[Buffer];
            long total = 0;
            int read;
            while ((read = await inflate.ReadAsync(chunk, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes) return Result.Failure(Error.Validation("The backup is larger than allowed."));
                await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or NotSupportedException)
        {
            return bad;
        }
    }
#pragma warning restore CA5350, CA5379

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(done), ct);
            if (n == 0) return false;
            done += n;
        }
        return true;
    }

    /// <summary>Reads at most <c>length</c> bytes of ciphertext from the source and returns them decrypted.</summary>
    private sealed class DecryptingReadStream : Stream
    {
        private readonly Stream _source;
        private readonly Ctr _ctr;
        private long _left;

        public DecryptingReadStream(Stream source, long length, Ctr ctr) { _source = source; _left = length; _ctr = ctr; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0) return 0;
            var n = _source.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            _ctr.Transform(buffer[..n]);
            _left -= n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_left <= 0) return 0;
            var n = await _source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _left)], cancellationToken);
            _ctr.Transform(buffer.Span[..n]);
            _left -= n;
            return n;
        }

        protected override void Dispose(bool disposing) { if (disposing) _ctr.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
