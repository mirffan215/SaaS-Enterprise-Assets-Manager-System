namespace AMGH.ITInventory.Application.Abstractions.Persistence;

/// <summary>
/// Stores software license keys encrypted in SQL Server (AES-256). Every read is written to
/// the audit log by the database, so callers must check the Licenses.ViewKeys permission first.
/// </summary>
public interface ILicenseKeyStore
{
    Task SetKeyAsync(int softwareLicenseId, string? licenseKey, CancellationToken cancellationToken = default);

    Task<string?> GetKeyAsync(int softwareLicenseId, CancellationToken cancellationToken = default);
}
