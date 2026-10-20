using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>Size and activity of each company database, for the operator console and the metrics endpoint.</summary>
public sealed class TenantUsageService : ITenantUsageService
{
    private readonly ITenantCatalog _catalog;
    private readonly TenancyOptions _options;

    public TenantUsageService(ITenantCatalog catalog, IOptions<TenancyOptions> options)
    {
        _catalog = catalog;
        _options = options.Value;
    }

    private const string Query = @"
SELECT (SELECT SUM(CAST(size AS BIGINT)) * 8192 FROM sys.database_files WHERE type = 0),
       (SELECT COUNT(*) FROM dbo.tblUser WHERE IsDeleted = 0),
       (SELECT COUNT(*) FROM dbo.tblUser WHERE IsDeleted = 0 AND IsActive = 1),
       (SELECT COUNT(*) FROM dbo.tblAsset WHERE IsDeleted = 0),
       (SELECT MAX(LastLoginDate) FROM dbo.tblUser),
       (SELECT MAX(EventDate) FROM dbo.tblAuditLog);";

    public async Task<IReadOnlyList<TenantUsage>> GetAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<TenantUsage>();
        foreach (var tenant in await _catalog.ListAsync(cancellationToken))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_options.ServerConnectionString)) throw new InvalidOperationException("Tenancy:ServerConnectionString is not set.");
                var database = new SqlConnectionStringBuilder(tenant.ConnectionString).InitialCatalog;
                await using var db = new SqlConnection(new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = database }.ConnectionString);
                await db.OpenAsync(cancellationToken);
                await using var command = db.CreateCommand();
                command.CommandText = Query;
                command.CommandTimeout = 30;
                await using var r = await command.ExecuteReaderAsync(cancellationToken);
                await r.ReadAsync(cancellationToken);
                result.Add(new TenantUsage(tenant.Code, r.IsDBNull(0) ? 0 : r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3),
                    r.IsDBNull(4) ? null : DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc), r.IsDBNull(5) ? null : DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(new TenantUsage(tenant.Code, 0, 0, 0, 0, null, null, ex.Message.Split('\n')[0].Trim()));
            }
        }
        return result;
    }
}
