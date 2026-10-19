using System.Text.RegularExpressions;
using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Storage;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder for uploaded documents, outside the web site folder. Include it in backups.</summary>
    public string RootPath { get; set; } = @"C:\AMGH\Documents";
}

/// <summary>
/// Stores files on the server's disk under <c>&lt;root&gt;\&lt;CompanyId&gt;\&lt;area&gt;\</c>: each company's files
/// live in their own folder and a request can only reach its own company's folder. Only application-generated
/// names are accepted (no path traversal).
/// </summary>
internal sealed partial class LocalFileStorage : IFileStorage
{
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    private readonly string _root;
    private readonly ICompanyContext _company;

    public LocalFileStorage(IOptions<StorageOptions> options, ICompanyContext company)
    {
        // Trailing separator, so "C:\AMGH\Documents2" can never pass as inside "C:\AMGH\Documents".
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.RootPath)) + Path.DirectorySeparatorChar;
        _company = company;
    }

    private string PathFor(string area, string name)
    {
        if (!SafeName().IsMatch(area) || !SafeName().IsMatch(name) || name.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage name.");
        var companyId = _company.CompanyId ?? throw new InvalidOperationException("File storage needs an active company.");
        var companyRoot = Path.Combine(_root, companyId.ToString(CultureInfo.InvariantCulture)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(companyRoot, area, name));
        if (!full.StartsWith(companyRoot, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid storage path.");
        return full;
    }

    public async Task SaveAsync(string area, string storedFileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(area, storedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".partial";
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await content.CopyToAsync(file, cancellationToken);
        File.Move(temp, path, overwrite: false);
    }

    public Task<Stream?> OpenReadAsync(string area, string storedFileName, CancellationToken cancellationToken = default)
    {
        var path = PathFor(area, storedFileName);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string area, string storedFileName, CancellationToken cancellationToken = default)
    {
        var path = PathFor(area, storedFileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
