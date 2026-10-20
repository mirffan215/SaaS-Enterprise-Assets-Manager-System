using System.Globalization;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

public sealed record TenantBackupFile(string FileName, long SizeBytes, DateTime CreatedUtc);

/// <summary>Back up a company's database and restore it from one of its own backups (operator page).</summary>
public interface ITenantBackupService
{
    /// <summary>Full, compressed, checksummed backup of the company's database into the backup folder.</summary>
    Task<Result<TenantBackupFile>> BackupAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>The company's backups in the backup folder, newest first.</summary>
    Task<IReadOnlyList<TenantBackupFile>> ListAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the company's current data with the backup: the backup is verified first, a safety backup of the current state is taken,
    /// the company is taken offline for the restore (people are signed out of its database) and brought back afterwards.
    /// </summary>
    Task<Result> RestoreAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the provisioning connection (Tenancy:ServerConnectionString), the same login that created the company databases and owns them.
/// Only backups of the company itself can be restored into it: the file name must be <c>ITInv_Tenant_&lt;code&gt;_yyyyMMdd_HHmmss.bak</c> inside the backup folder.
/// One backup or restore runs at a time.
/// </summary>
internal sealed partial class TenantBackupService : ITenantBackupService
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    [GeneratedRegex(@"^(?<db>ITInv_Tenant_[A-Za-z0-9]{2,10})_(?<stamp>\d{8}_\d{6})\.bak$")] private static partial Regex BackupName();
    [GeneratedRegex("^[A-Za-z0-9]{2,10}$")] private static partial Regex CodePattern();

    private readonly TenancyOptions _options;
    private readonly ILogger<TenantBackupService> _logger;

    public TenantBackupService(IOptions<TenancyOptions> options, ILogger<TenantBackupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True for a backup file name of the given database (case-insensitive on the database part).</summary>
    internal static bool IsBackupOf(string database, string fileName) =>
        BackupName().Match(fileName ?? string.Empty) is { Success: true } m && string.Equals(m.Groups["db"].Value, database, StringComparison.OrdinalIgnoreCase);

    private SqlConnectionStringBuilder Server() =>
        string.IsNullOrWhiteSpace(_options.ServerConnectionString)
            ? throw new InvalidOperationException("Backups need Tenancy:ServerConnectionString.")
            : new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = "master" };

    /// <summary>The database of the company, spelled as SQL Server has it; null when it does not exist.</summary>
    private static async Task<string?> DatabaseOfAsync(SqlConnection master, string tenantCode, CancellationToken ct)
    {
        if (!CodePattern().IsMatch(tenantCode ?? string.Empty)) return null;
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT name FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", TenantProvisioningService.DatabasePrefix + tenantCode);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    private async Task<string> FolderAsync(SqlConnection master, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.BackupFolder)) return _options.BackupFolder.TrimEnd('\\', '/');
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(400))";
        var path = await command.ExecuteScalarAsync(ct) as string;
        return string.IsNullOrWhiteSpace(path) ? throw new InvalidOperationException("Set Tenancy:BackupFolder: the SQL Server default backup folder is not known.") : path.TrimEnd('\\', '/');
    }

    public async Task<Result<TenantBackupFile>> BackupAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try { return await BackupCoreAsync(tenantCode, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Backup of {Code} failed", tenantCode);
            return Result<TenantBackupFile>.Failure(Error.BusinessRule("The backup failed. Reason: " + FirstLine(ex), "backup.failed"));
        }
        finally { OneAtATime.Release(); }
    }

    private async Task<Result<TenantBackupFile>> BackupCoreAsync(string tenantCode, CancellationToken ct)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(ct);
        var database = await DatabaseOfAsync(master, tenantCode, ct);
        if (database is null) return Result<TenantBackupFile>.Failure(Error.NotFound("That company has no database here."));
        var folder = await FolderAsync(master, ct);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"{database}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        var path = Path.Combine(folder, fileName);

        // the database name comes from sys.databases and matches ITInv_Tenant_<letters and digits>: safe to bracket-quote
        await using (var backup = master.CreateCommand())
        {
            backup.CommandText = $"BACKUP DATABASE [{database}] TO DISK = @path WITH COMPRESSION, CHECKSUM, INIT, FORMAT";
            backup.Parameters.AddWithValue("@path", path);
            backup.CommandTimeout = 3600;
            await backup.ExecuteNonQueryAsync(ct);
        }
        await using (var verify = master.CreateCommand())
        {
            verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
            verify.Parameters.AddWithValue("@path", path);
            verify.CommandTimeout = 3600;
            await verify.ExecuteNonQueryAsync(ct);
        }
        _logger.LogInformation("Backup of {Database} written to {Path}", database, path);
        var info = new FileInfo(path);
        return Result<TenantBackupFile>.Success(new TenantBackupFile(fileName, info.Exists ? info.Length : 0, info.Exists ? info.CreationTimeUtc : DateTime.UtcNow));
    }

    public async Task<IReadOnlyList<TenantBackupFile>> ListAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(cancellationToken);
        var database = await DatabaseOfAsync(master, tenantCode, cancellationToken);
        if (database is null) return Array.Empty<TenantBackupFile>();
        var folder = await FolderAsync(master, cancellationToken);
        if (!Directory.Exists(folder)) return Array.Empty<TenantBackupFile>();
        return new DirectoryInfo(folder).EnumerateFiles(database + "_*.bak")
            .Where(f => IsBackupOf(database, f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new TenantBackupFile(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    public async Task<Result> RestoreAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try { return await RestoreCoreAsync(tenantCode, fileName, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Restore of {Code} from {File} failed", tenantCode, fileName);
            return Result.Failure(Error.BusinessRule("The restore failed. Reason: " + FirstLine(ex), "restore.failed"));
        }
        finally { OneAtATime.Release(); }
    }

    private async Task<Result> RestoreCoreAsync(string tenantCode, string fileName, CancellationToken ct)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(ct);
        var database = await DatabaseOfAsync(master, tenantCode, ct);
        if (database is null) return Result.Failure(Error.NotFound("That company has no database here."));
        if (!IsBackupOf(database, fileName)) return Result.Failure(Error.Validation("That is not a backup of this company."));
        var folder = await FolderAsync(master, ct);
        var path = Path.Combine(folder, fileName);

        // 1. the file must be a sound backup before anything is replaced
        await using (var verify = master.CreateCommand())
        {
            verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
            verify.Parameters.AddWithValue("@path", path);
            verify.CommandTimeout = 3600;
            await verify.ExecuteNonQueryAsync(ct);
        }
        master.Close();

        // 2. keep the current state, so a wrong restore can itself be undone
        var safety = await BackupCoreAsync(tenantCode, ct);
        if (safety.IsFailure) return Result.Failure(Error.BusinessRule("The restore was not started: the safety backup of the current data failed. " + safety.FirstError!.Message));

        // 3. offline, replace, online
        await using var connection = new SqlConnection(Server().ConnectionString);
        await connection.OpenAsync(ct);
        var singleUser = false;
        try
        {
            await using (var offline = connection.CreateCommand())
            {
                offline.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
                offline.CommandTimeout = 300;
                await offline.ExecuteNonQueryAsync(ct);
                singleUser = true;
            }
            await using var restore = connection.CreateCommand();
            restore.CommandText = $"RESTORE DATABASE [{database}] FROM DISK = @path WITH REPLACE, RECOVERY";
            restore.Parameters.AddWithValue("@path", path);
            restore.CommandTimeout = 7200;
            await restore.ExecuteNonQueryAsync(CancellationToken.None); // never half-way: the browser closing must not stop it
        }
        finally
        {
            if (singleUser)
            {
                await using var online = connection.CreateCommand();
                online.CommandText = $"ALTER DATABASE [{database}] SET MULTI_USER";
                online.CommandTimeout = 300;
                await online.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        _logger.LogWarning("Company {Database} restored from {File} (safety backup {Safety})", database, fileName, safety.Value.FileName);
        return Result.Success();
    }

    private static string FirstLine(Exception ex) => ex.Message.Split('\n')[0].Trim();
}
