using System.Data;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Shared.Abstractions;

namespace AMGH.ITInventory.Persistence.Procedures;

internal sealed class LicenseKeyStore : ILicenseKeyStore
{
    private readonly StoredProcedureExecutor _executor;
    private readonly ICurrentUserService _user;

    public LicenseKeyStore(StoredProcedureExecutor executor, ICurrentUserService user)
    {
        _executor = executor;
        _user = user;
    }

    public Task SetKeyAsync(int softwareLicenseId, string? licenseKey, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.LicenseSetKey,
        [
            StoredProcedureExecutor.In("@SoftwareLicenseId", SqlDbType.Int, softwareLicenseId),
            StoredProcedureExecutor.In("@LicenseKey", SqlDbType.NVarChar, string.IsNullOrWhiteSpace(licenseKey) ? null : licenseKey.Trim(), 1000),
            StoredProcedureExecutor.In("@PerformedBy", SqlDbType.NVarChar, _user.UserName, 100)
        ], cancellationToken);

    public async Task<string?> GetKeyAsync(int softwareLicenseId, CancellationToken cancellationToken = default)
    {
        var value = await _executor.ExecuteScalarAsync(ProcedureNames.LicenseGetKey,
        [
            StoredProcedureExecutor.In("@SoftwareLicenseId", SqlDbType.Int, softwareLicenseId),
            StoredProcedureExecutor.In("@RequestedBy", SqlDbType.NVarChar, _user.UserName, 100),
            StoredProcedureExecutor.In("@IpAddress", SqlDbType.NVarChar, _user.IpAddress, 45)
        ], cancellationToken);
        return value as string;
    }
}
