using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using AMGH.ITInventory.Application.CompanyAdmin;
using AMGH.ITInventory.Infrastructure.Archives;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class AesZipWriterTests
{
    private static byte[] Build(string password, params ArchiveEntry[] entries)
    {
        using var ms = new MemoryStream();
        new AesZipWriter().Write(ms, password, entries);
        return ms.ToArray();
    }

    [Fact]
    public void Archive_lists_every_entry_and_marks_them_encrypted()
    {
        var bytes = Build("pw-12345", new ArchiveEntry("a.csv", Encoding.UTF8.GetBytes("x,y\r\n1,2\r\n")), new ArchiveEntry("b.txt", new byte[0]));
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(new[] { "a.csv", "b.txt" }, zip.Entries.Select(e => e.FullName));
        Assert.Equal(0x0801, BitConverter.ToUInt16(bytes, 6)); // encrypted + UTF-8 names
        Assert.Equal(99, BitConverter.ToUInt16(bytes, 8));     // AES method
    }

    [Fact]
    public void Plain_text_does_not_appear_in_the_archive()
    {
        var bytes = Build("pw-12345", new ArchiveEntry("secret.csv", Encoding.UTF8.GetBytes(new string('S', 500) + "CONFIDENTIAL-MARKER")));
        Assert.DoesNotContain("CONFIDENTIAL-MARKER", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>Round trip through a real archiver (7-Zip) when it is installed: right password extracts, wrong password fails.</summary>
    [Fact]
    public void Seven_zip_opens_the_archive_with_the_password()
    {
        var sevenZip = new[] { "/usr/bin/7z", "/usr/bin/7zz", "/usr/local/bin/7z", @"C:\Program Files\7-Zip\7z.exe" }.FirstOrDefault(File.Exists);
        if (sevenZip is null) return; // nothing to compare with on this machine
        var dir = Path.Combine(Path.GetTempPath(), "aeszip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var content = string.Join("\r\n", Enumerable.Range(0, 5000).Select(i => $"{i},row {i},\"quoted, text\""));
            var file = Path.Combine(dir, "t.zip");
            File.WriteAllBytes(file, Build("Correct-Horse-9", new ArchiveEntry("data.csv", Encoding.UTF8.GetBytes(content)), new ArchiveEntry("empty.txt", new byte[0])));

            Assert.Equal(0, Run(sevenZip, $"x -y -pCorrect-Horse-9 -o\"{Path.Combine(dir, "out")}\" \"{file}\""));
            Assert.Equal(content, File.ReadAllText(Path.Combine(dir, "out", "data.csv")));
            Assert.True(new FileInfo(Path.Combine(dir, "out", "empty.txt")).Length == 0);
            Assert.NotEqual(0, Run(sevenZip, $"x -y -pWrong-Password -o\"{Path.Combine(dir, "bad")}\" \"{file}\""));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static int Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
