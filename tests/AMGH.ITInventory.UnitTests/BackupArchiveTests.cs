using System.Diagnostics;
using System.Security.Cryptography;
using AMGH.ITInventory.Infrastructure.Archives;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class BackupArchiveTests
{
    private static readonly AesZipFile Archive = new();

    private static byte[] Sample(int size)
    {
        // half incompressible, half repetitive: like a database backup
        var data = new byte[size];
        RandomNumberGenerator.Fill(data.AsSpan(0, size / 2));
        for (var i = size / 2; i < size; i++) data[i] = (byte)(i % 7);
        return data;
    }

    private static async Task<byte[]> Pack(byte[] content, string password)
    {
        using var output = new MemoryStream();
        await Archive.WriteAsync(output, password, "backup.bak", new MemoryStream(content));
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1_000_003)]
    [InlineData(6_000_000)]
    public async Task What_goes_in_comes_out_unchanged(int size)
    {
        var content = Sample(size);
        var zip = await Pack(content, "Correct-Horse-9");
        using var restored = new MemoryStream();
        var result = await Archive.ReadAsync(new MemoryStream(zip), "Correct-Horse-9", restored, long.MaxValue);
        Assert.True(result.IsSuccess, result.FirstError?.Message);
        Assert.Equal(content, restored.ToArray());
    }

    [Fact]
    public async Task A_wrong_password_is_reported_as_such()
    {
        var zip = await Pack(Sample(10_000), "Correct-Horse-9");
        var result = await Archive.ReadAsync(new MemoryStream(zip), "Wrong-Password-1", new MemoryStream(), long.MaxValue);
        Assert.True(result.IsFailure);
        Assert.Contains("password", result.FirstError!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_changed_byte_is_detected_before_anything_is_written()
    {
        var zip = await Pack(Sample(100_000), "Correct-Horse-9");
        zip[zip.Length / 2] ^= 0x01;
        using var output = new MemoryStream();
        var result = await Archive.ReadAsync(new MemoryStream(zip), "Correct-Horse-9", output, long.MaxValue);
        Assert.True(result.IsFailure);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public async Task Other_files_are_refused_politely(string text)
    {
        var result = await Archive.ReadAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), "x", new MemoryStream(), long.MaxValue);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task The_size_limit_stops_a_decompression_bomb()
    {
        var zip = await Pack(new byte[5_000_000], "Correct-Horse-9");   // all zeros: tiny zip, big content
        var result = await Archive.ReadAsync(new MemoryStream(zip), "Correct-Horse-9", new MemoryStream(), 1_000_000);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Seven_zip_opens_the_streamed_archive()
    {
        var sevenZip = new[] { "/usr/bin/7z", "/usr/bin/7zz", "/usr/local/bin/7z", @"C:\Program Files\7-Zip\7z.exe" }.FirstOrDefault(File.Exists);
        if (sevenZip is null) return;
        var dir = Path.Combine(Path.GetTempPath(), "bk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var content = Sample(3_000_000);
            await File.WriteAllBytesAsync(Path.Combine(dir, "t.zip"), await Pack(content, "Correct-Horse-9"));
            var p = Process.Start(new ProcessStartInfo(sevenZip, $"x -y -pCorrect-Horse-9 -o\"{Path.Combine(dir, "out")}\" \"{Path.Combine(dir, "t.zip")}\"") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(dir, "out", "backup.bak")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
