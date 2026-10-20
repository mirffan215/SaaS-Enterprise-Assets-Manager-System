using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>
/// A password-protected zip (AES-256) that holds one big file, such as a database backup. Works on streams and temporary files, never
/// holding the whole file in memory. Implemented in Infrastructure.
/// </summary>
public interface IBackupArchive
{
    /// <summary>Writes <paramref name="content"/> as the single entry <paramref name="entryName"/> of a zip to <paramref name="output"/>.</summary>
    Task WriteAsync(Stream output, string password, string entryName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a zip made by <see cref="WriteAsync"/>, checks the password and the integrity (authentication code) and writes the single entry to
    /// <paramref name="output"/>. A wrong password, a damaged file or something that is not such a zip is a failed result, never an exception.
    /// </summary>
    Task<Result> ReadAsync(Stream zip, string password, Stream output, long maxBytes, CancellationToken cancellationToken = default);
}
