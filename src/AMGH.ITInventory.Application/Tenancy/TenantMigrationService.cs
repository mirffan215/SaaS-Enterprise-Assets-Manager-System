using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>Where one company's database stands against the migration scripts shipped with this version.</summary>
public sealed record TenantSchemaStatus(string Code, string Name, int Applied, int Pending, string? Problem)
{
    public bool UpToDate => Problem is null && Pending == 0;
}

/// <summary>Brings every company database up to the schema of the installed version.</summary>
public interface ITenantMigrationService
{
    /// <summary>Status of every company (does not change anything).</summary>
    Task<IReadOnlyList<TenantSchemaStatus>> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Upgrades one company; a backup is made first when there is something to apply.</summary>
    Task<Result<int>> UpgradeAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>Upgrades every company (one failing company does not stop the others); returns the status afterwards.</summary>
    Task<IReadOnlyList<TenantSchemaStatus>> UpgradeAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The scripts 01 to 19 build a new company; every later change is a numbered script in <c>SQLScripts\Migrations</c>
/// (<c>0001_Name.sql</c>, <c>0002_Name.sql</c>, ...) that must be safe to run twice. Applied scripts are recorded in each company's
/// <c>dbo.tblSchemaMigration</c> with a checksum, so an edited script that was already applied is reported instead of silently skipped.
/// New companies get the baseline and then every migration, so old and new companies end up identical.
/// </summary>
public sealed class TenantMigrationService : ITenantMigrationService
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private readonly ITenantCatalog _catalog;
    private readonly ITenantBackupService _backups;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantMigrationService> _logger;

    public TenantMigrationService(ITenantCatalog catalog, ITenantBackupService backups, IOptions<TenancyOptions> options, ILogger<TenantMigrationService> logger)
    {
        _catalog = catalog;
        _backups = backups;
        _options = options.Value;
        _logger = logger;
    }

    internal sealed record Migration(string Name, string Sql, string Checksum);

    internal static string MigrationsFolder(TenancyOptions options) =>
        Path.Combine(Path.IsPathRooted(options.ScriptsPath) ? options.ScriptsPath : Path.Combine(AppContext.BaseDirectory, options.ScriptsPath), "Migrations");

    internal static IReadOnlyList<Migration> Load(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<Migration>();
        return Directory.GetFiles(folder, "*.sql").Select(Path.GetFileName).OfType<string>()
            .Where(n => n.Length > 5 && n[..4].All(char.IsAsciiDigit) && n[4] == '_')
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                var sql = File.ReadAllText(Path.Combine(folder, n), Encoding.UTF8);
                return new Migration(n, sql, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n", StringComparison.Ordinal)))));
            }).ToList();
    }

    private const string EnsureTable = @"
IF OBJECT_ID(N'dbo.tblSchemaMigration', N'U') IS NULL
    CREATE TABLE dbo.tblSchemaMigration (
        Name         NVARCHAR(200) NOT NULL CONSTRAINT PK_tblSchemaMigration PRIMARY KEY,
        Checksum     CHAR(64)      NOT NULL,
        AppliedAtUtc DATETIME2(0)  NOT NULL CONSTRAINT DF_tblSchemaMigration_Applied DEFAULT SYSUTCDATETIME());";

    private static async Task<Dictionary<string, string>> AppliedAsync(SqlConnection db, bool create, CancellationToken ct)
    {
        if (create)
        {
            await using var ensure = db.CreateCommand();
            ensure.CommandText = EnsureTable;
            await ensure.ExecuteNonQueryAsync(ct);
        }
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var read = db.CreateCommand();
        read.CommandText = "IF OBJECT_ID(N'dbo.tblSchemaMigration', N'U') IS NOT NULL SELECT Name, Checksum FROM dbo.tblSchemaMigration";
        await using var reader = await read.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) applied[reader.GetString(0)] = reader.GetString(1);
        return applied;
    }

    /// <summary>Names of applied scripts whose file has changed or is gone.</summary>
    private static string? Drift(IReadOnlyList<Migration> all, Dictionary<string, string> applied)
    {
        foreach (var (name, checksum) in applied)
        {
            var file = all.FirstOrDefault(m => m.Name == name);
            if (file is null) return $"{name} was applied but is not part of this version (the database is newer than the program).";
            if (!string.Equals(file.Checksum, checksum, StringComparison.OrdinalIgnoreCase)) return $"{name} was changed after it was applied.";
        }
        return null;
    }

    /// <summary>Applies the pending scripts to an open connection on the company database; returns how many ran.</summary>
    internal static async Task<int> ApplyAsync(SqlConnection db, IReadOnlyList<Migration> all, ILogger logger, CancellationToken ct)
    {
        var applied = await AppliedAsync(db, create: true, ct);
        if (Drift(all, applied) is { } problem) throw new InvalidOperationException(problem);
        var count = 0;
        foreach (var migration in all.Where(m => !applied.ContainsKey(m.Name)))
        {
            logger.LogInformation("Applying migration {Migration} to {Database}", migration.Name, db.Database);
            await SqlScriptRunner.RunAsync(db, migration.Sql, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DatabaseName"] = db.Database }, ct);
            await using var record = db.CreateCommand();
            record.CommandText = "INSERT dbo.tblSchemaMigration (Name, Checksum) VALUES (@n, @c)";
            record.Parameters.AddWithValue("@n", migration.Name);
            record.Parameters.AddWithValue("@c", migration.Checksum);
            await record.ExecuteNonQueryAsync(ct);
            count++;
        }
        return count;
    }

    private SqlConnection Open(TenantRecord tenant)
    {
        // the application's run-time login has no right to change the schema: use the provisioning login on the company's database
        var database = new SqlConnectionStringBuilder(tenant.ConnectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(_options.ServerConnectionString)) throw new InvalidOperationException("Upgrades need Tenancy:ServerConnectionString.");
        return new SqlConnection(new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = database }.ConnectionString);
    }

    public async Task<IReadOnlyList<TenantSchemaStatus>> StatusAsync(CancellationToken cancellationToken = default)
    {
        var all = Load(MigrationsFolder(_options));
        var result = new List<TenantSchemaStatus>();
        foreach (var tenant in await _catalog.ListAsync(cancellationToken))
        {
            try
            {
                await using var db = Open(tenant);
                await db.OpenAsync(cancellationToken);
                var applied = await AppliedAsync(db, create: false, cancellationToken);
                result.Add(new TenantSchemaStatus(tenant.Code, tenant.Name, applied.Count, all.Count(m => !applied.ContainsKey(m.Name)), Drift(all, applied)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(new TenantSchemaStatus(tenant.Code, tenant.Name, 0, 0, "Cannot read the database: " + ex.Message.Split('\n')[0].Trim()));
            }
        }
        return result;
    }

    public async Task<Result<int>> UpgradeAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            var tenant = (await _catalog.ListAsync(cancellationToken)).FirstOrDefault(t => string.Equals(t.Code, tenantCode, StringComparison.OrdinalIgnoreCase));
            if (tenant is null) return Result<int>.Failure(Error.NotFound("Unknown company."));
            var all = Load(MigrationsFolder(_options));
            await using var db = Open(tenant);
            await db.OpenAsync(cancellationToken);
            var applied = await AppliedAsync(db, create: false, cancellationToken);
            if (Drift(all, applied) is { } problem) return Result<int>.Failure(Error.BusinessRule($"{tenant.Code}: {problem}", "upgrade.drift"));
            if (all.All(m => applied.ContainsKey(m.Name)))
            {
                await AppliedAsync(db, create: true, cancellationToken); // make sure the history table exists
                return Result<int>.Success(0);
            }
            if (!tenant.IsDemo && !string.IsNullOrWhiteSpace(_options.BackupFolder))
            {
                var backup = await _backups.BackupAsync(tenant.Code, scheduled: false, cancellationToken: cancellationToken);
                if (backup.IsFailure) return Result<int>.Failure(Error.BusinessRule($"{tenant.Code}: not upgraded, the safety backup failed. {backup.FirstError!.Message}", "upgrade.backup_failed"));
            }
            var count = await ApplyAsync(db, all, _logger, cancellationToken);
            _logger.LogInformation("Company {Code} upgraded: {Count} migration(s) applied", tenant.Code, count);
            return Result<int>.Success(count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upgrade of {Code} failed", tenantCode);
            return Result<int>.Failure(Error.BusinessRule($"{tenantCode}: the upgrade failed. Reason: " + ex.Message.Split('\n')[0].Trim(), "upgrade.failed"));
        }
        finally { OneAtATime.Release(); }
    }

    public async Task<IReadOnlyList<TenantSchemaStatus>> UpgradeAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var tenant in await _catalog.ListAsync(cancellationToken))
        {
            var result = await UpgradeAsync(tenant.Code, cancellationToken);
            if (result.IsFailure) _logger.LogError("Upgrade of {Code} did not complete: {Reason}", tenant.Code, result.FirstError!.Message);
        }
        return await StatusAsync(cancellationToken);
    }
}
