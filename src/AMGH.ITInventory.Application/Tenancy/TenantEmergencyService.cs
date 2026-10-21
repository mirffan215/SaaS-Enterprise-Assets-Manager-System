using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>Uses the provisioning login on the company's own database (the application's run-time login is not asked to do this).</summary>
public sealed class TenantEmergencyService : ITenantEmergencyService
{
    private readonly ITenantCatalog _catalog;
    private readonly TenancyOptions _options;

    public TenantEmergencyService(ITenantCatalog catalog, IOptions<TenancyOptions> options)
    {
        _catalog = catalog;
        _options = options.Value;
    }

    private const string Sql = @"
UPDATE dbo.tblCompany SET AllowedIpRanges = NULL, MfaPolicy = 0, SsoEnabled = 0;
DELETE rc FROM dbo.tblUserRecoveryCode rc JOIN dbo.tblUser u ON u.UserId = rc.UserId WHERE u.Username = @admin;
UPDATE dbo.tblUser SET TotpSecret = NULL, TotpEnabledDate = NULL, TotpLastStep = NULL, AccessFailedCount = 0, LockoutEndUtc = NULL, SecurityStamp = NEWID()
WHERE Username = @admin;";

    public async Task<bool> RestoreAccessAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        var tenant = (await _catalog.ListAsync(cancellationToken)).FirstOrDefault(t => string.Equals(t.Code, tenantCode, StringComparison.OrdinalIgnoreCase));
        if (tenant is null) return false;
        if (string.IsNullOrWhiteSpace(_options.ServerConnectionString)) throw new InvalidOperationException("Tenancy:ServerConnectionString is not set.");
        var database = new SqlConnectionStringBuilder(tenant.ConnectionString).InitialCatalog;
        await using var db = new SqlConnection(new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = database }.ConnectionString);
        await db.OpenAsync(cancellationToken);
        await using var command = db.CreateCommand();
        command.CommandText = Sql;
        command.Parameters.AddWithValue("@admin", TenantProvisioningService.AdminUsername);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
