using System.Globalization;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

/// <param name="FileName">Backup file name.</param>
/// <param name="SizeBytes">Size on disk.</param>
/// <param name="CreatedUtc">When the file was written.</param>
/// <param name="VerifiedUtc">When a test restore last checked it; null when it never was.</param>
/// <param name="VerifyOk">Result of that check.</param>
/// <param name="VerifyNote">Short result text (first line of the error when it failed).</param>
public sealed record TenantBackupFile(string FileName, long SizeBytes, DateTime CreatedUtc, DateTime? VerifiedUtc = null, bool VerifyOk = false, string? VerifyNote = null);

/// <summary>Back up a company's database and restore it from one of its own backups (operator page).</summary>
public interface ITenantBackupService
{
    /// <summary>Full, compressed, checksummed backup of the company's database into the backup folder. Scheduled backups get an "_auto" name and are pruned by <see cref="DeleteOldScheduledAsync"/>.</summary>
    Task<Result<TenantBackupFile>> BackupAsync(string tenantCode, bool scheduled = false, CancellationToken cancellationToken = default);

    /// <summary>Opens one of the company's backup files for reading (to download it); null when it is not one of its backups.</summary>
    Task<Stream?> OpenAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores from a zip made by <see cref="IBackupArchive"/> (a downloaded backup): the zip is opened with the password, the backup inside must
    /// belong to this very company, then the normal restore steps follow (safety backup, offline, replace, online).
    /// </summary>
    Task<Result> RestoreFromZipAsync(string tenantCode, Stream zip, string password, CancellationToken cancellationToken = default);

    /// <summary>Deletes scheduled ("_auto") backups beyond the newest <paramref name="keep"/>; manual backups are never deleted.</summary>
    Task<int> DeleteOldScheduledAsync(string tenantCode, int keep, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves a backup is usable: restores it under a temporary name, runs DBCC CHECKDB and a sanity query on it, drops the copy and remembers the
    /// result next to the file. Needs disk space for a second copy of the database for a short time. Never touches the live company database.
    /// </summary>
    Task<Result<TenantBackupFile>> VerifyAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default);

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

    [GeneratedRegex(@"^(?<db>ITInv_Tenant_[A-Za-z0-9]{2,10})_(?<stamp>\d{8}_\d{6})(?<auto>_auto)?\.bak$")] private static partial Regex BackupName();
    [GeneratedRegex("^[A-Za-z0-9]{2,10}$")] private static partial Regex CodePattern();

    private const long MaxUploadBytes = 8L * 1024 * 1024 * 1024;

    private readonly TenancyOptions _options;
    private readonly ILogger<TenantBackupService> _logger;
    private readonly IBackupArchive _archive;

    public TenantBackupService(IOptions<TenancyOptions> options, ILogger<TenantBackupService> logger, IBackupArchive archive)
    {
        _options = options.Value;
        _logger = logger;
        _archive = archive;
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

    public async Task<Result<TenantBackupFile>> BackupAsync(string tenantCode, bool scheduled = false, CancellationToken cancellationToken = default)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try { return await BackupCoreAsync(tenantCode, scheduled, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Backup of {Code} failed", tenantCode);
            return Result<TenantBackupFile>.Failure(Error.BusinessRule("The backup failed. Reason: " + FirstLine(ex), "backup.failed"));
        }
        finally { OneAtATime.Release(); }
    }

    private async Task<Result<TenantBackupFile>> BackupCoreAsync(string tenantCode, bool scheduled, CancellationToken ct)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(ct);
        var database = await DatabaseOfAsync(master, tenantCode, ct);
        if (database is null) return Result<TenantBackupFile>.Failure(Error.NotFound("That company has no database here."));
        var folder = await FolderAsync(master, ct);
        // File names have second resolution: never reuse one (BACKUP ... FORMAT would overwrite the earlier backup, which may be the file being restored)
        var moment = DateTime.Now;
        string fileName, path;
        while (true)
        {
            fileName = string.Create(CultureInfo.InvariantCulture, $"{database}_{moment:yyyyMMdd_HHmmss}{(scheduled ? "_auto" : string.Empty)}.bak");
            path = Path.Combine(folder, fileName);
            if (!File.Exists(path) && !File.Exists(Path.Combine(folder, fileName.Replace("_auto.bak", ".bak", StringComparison.Ordinal)))) break;
            moment = moment.AddSeconds(1);
        }

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
            .Select(f => WithVerification(new TenantBackupFile(f.Name, f.Length, f.LastWriteTimeUtc), f.FullName))
            .ToList();
    }

    private static string VerifyFile(string backupPath) => backupPath + ".verify";

    private static TenantBackupFile WithVerification(TenantBackupFile file, string backupPath)
    {
        try
        {
            var path = VerifyFile(backupPath);
            if (!File.Exists(path)) return file;
            var parts = File.ReadAllText(path).Split('|', 3);
            return parts.Length == 3 && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                ? file with { VerifiedUtc = when, VerifyOk = parts[0] == "OK", VerifyNote = parts[2].Trim() }
                : file;
        }
        catch (IOException) { return file; }
        catch (UnauthorizedAccessException) { return file; }
    }

    public async Task<Result<TenantBackupFile>> VerifyAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try { return await VerifyCoreAsync(tenantCode, fileName, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Verification of {File} failed", fileName);
            return Result<TenantBackupFile>.Failure(Error.BusinessRule("The check could not run. Reason: " + FirstLine(ex), "backup.verify_failed"));
        }
        finally { OneAtATime.Release(); }
    }

    private async Task<Result<TenantBackupFile>> VerifyCoreAsync(string tenantCode, string fileName, CancellationToken ct)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(ct);
        var database = await DatabaseOfAsync(master, tenantCode, ct);
        if (database is null) return Result<TenantBackupFile>.Failure(Error.NotFound("That company has no database here."));
        if (!IsBackupOf(database, fileName)) return Result<TenantBackupFile>.Failure(Error.Validation("That is not a backup of this company."));
        var path = Path.Combine(await FolderAsync(master, ct), fileName);
        var scratch = "ITInv_Verify_" + Guid.NewGuid().ToString("N")[..12];
        string outcome;
        try
        {
            var moves = new List<string>();
            await using (var list = master.CreateCommand())
            {
                list.CommandText = "RESTORE FILELISTONLY FROM DISK = @path";
                list.Parameters.AddWithValue("@path", path);
                await using var reader = await list.ExecuteReaderAsync(ct);
                var i = 0;
                while (await reader.ReadAsync(ct))
                {
                    var logical = reader.GetString(reader.GetOrdinal("LogicalName"));
                    var isLog = string.Equals(reader.GetString(reader.GetOrdinal("Type")), "L", StringComparison.OrdinalIgnoreCase);
                    moves.Add($"MOVE N'{logical.Replace("'", "''", StringComparison.Ordinal)}' TO N'@@{(isLog ? "LOG" : "DATA")}@@{scratch}_{i++}{(isLog ? ".ldf" : ".mdf")}'");
                }
            }
            string dataDir, logDir;
            await using (var dirs = master.CreateCommand())
            {
                dirs.CommandText = @"SELECT COALESCE(CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(400)), LEFT(physical_name, LEN(physical_name) - CHARINDEX(N'\', REVERSE(physical_name)) + 1)),
                                            COALESCE(CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(400)), CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(400)))
                                     FROM master.sys.database_files WHERE file_id = 1";
                await using var r = await dirs.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                dataDir = r.GetString(0);
                logDir = r.IsDBNull(1) ? dataDir : r.GetString(1);
            }
            string Dir(string d) => d.EndsWith('/') || d.EndsWith('\\') ? d : d + (d.Contains('\\', StringComparison.Ordinal) ? "\\" : "/");
            var withMoves = string.Join(", ", moves).Replace("@@DATA@@", Dir(dataDir), StringComparison.Ordinal).Replace("@@LOG@@", Dir(logDir), StringComparison.Ordinal);

            await using (var restore = master.CreateCommand())
            {
                restore.CommandText = $"RESTORE DATABASE [{scratch}] FROM DISK = @path WITH CHECKSUM, RECOVERY, {withMoves}";
                restore.Parameters.AddWithValue("@path", path);
                restore.CommandTimeout = 7200;
                await restore.ExecuteNonQueryAsync(ct);
            }
            await using (var check = master.CreateCommand())
            {
                check.CommandText = $"DBCC CHECKDB ([{scratch}]) WITH NO_INFOMSGS, ALL_ERRORMSGS";
                check.CommandTimeout = 7200;
                await check.ExecuteNonQueryAsync(ct);
            }
            await using (var sanity = master.CreateCommand())
            {
                sanity.CommandText = $"SELECT COUNT(*) FROM [{scratch}].dbo.tblCompany";
                if (Convert.ToInt32(await sanity.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) < 1) throw new InvalidOperationException("the restored database holds no company");
            }
            outcome = "OK|" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|Test restore and consistency check passed";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Backup {File} failed its test restore", fileName);
            outcome = "FAILED|" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" + FirstLine(ex);
        }
        finally
        {
            try
            {
                await using var drop = master.CreateCommand();
                drop.CommandText = $"IF DB_ID(N'{scratch}') IS NOT NULL BEGIN ALTER DATABASE [{scratch}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{scratch}]; END";
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception ex) { _logger.LogError(ex, "Could not drop the temporary database {Database}; remove it manually", scratch); }
        }
        try { await File.WriteAllTextAsync(VerifyFile(path), outcome, CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _logger.LogWarning(ex, "Could not save the check result next to {File}", fileName); }
        var info = new FileInfo(path);
        return Result<TenantBackupFile>.Success(WithVerification(new TenantBackupFile(fileName, info.Exists ? info.Length : 0, info.CreationTimeUtc), path));
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
        var path = Path.Combine(await FolderAsync(master, ct), fileName);
        master.Close();
        return await ReplaceAsync(tenantCode, database, path, fileName, ct);
    }

    /// <summary>The restore steps for a backup file at <paramref name="path"/> (already known to belong to <paramref name="database"/>).</summary>
    private async Task<Result> ReplaceAsync(string tenantCode, string database, string path, string label, CancellationToken ct)
    {
        // 1. the file must be a sound backup before anything is replaced
        await using (var master = new SqlConnection(Server().ConnectionString))
        {
            await master.OpenAsync(ct);
            await using var verify = master.CreateCommand();
            verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
            verify.Parameters.AddWithValue("@path", path);
            verify.CommandTimeout = 3600;
            await verify.ExecuteNonQueryAsync(ct);
        }

        // 2. keep the current state, so a wrong restore can itself be undone
        var safety = await BackupCoreAsync(tenantCode, false, ct);
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
        _logger.LogWarning("Company {Database} restored from {File} (safety backup {Safety})", database, label, safety.Value.FileName);
        return Result.Success();
    }

    public async Task<Stream?> OpenAsync(string tenantCode, string fileName, CancellationToken cancellationToken = default)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(cancellationToken);
        var database = await DatabaseOfAsync(master, tenantCode, cancellationToken);
        if (database is null || !IsBackupOf(database, fileName)) return null;
        var path = Path.Combine(await FolderAsync(master, cancellationToken), fileName);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan) : null;
    }

    public async Task<Result> RestoreFromZipAsync(string tenantCode, Stream zip, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zip);
        await OneAtATime.WaitAsync(cancellationToken);
        string? temp = null;
        try
        {
            string database, folder;
            await using (var master = new SqlConnection(Server().ConnectionString))
            {
                await master.OpenAsync(cancellationToken);
                database = await DatabaseOfAsync(master, tenantCode, cancellationToken) ?? string.Empty;
                if (database.Length == 0) return Result.Failure(Error.NotFound("That company has no database here."));
                folder = await FolderAsync(master, cancellationToken);
            }

            // the opened backup is written next to the other backups (where SQL Server can read it) and removed again afterwards
            var restoreFolder = Path.Combine(folder, "Restore");
            Directory.CreateDirectory(restoreFolder);
            temp = Path.Combine(restoreFolder, "upload_" + Guid.NewGuid().ToString("N") + ".bak");
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, FileOptions.Asynchronous))
            {
                var opened = await _archive.ReadAsync(zip, password, file, MaxUploadBytes, cancellationToken);
                if (opened.IsFailure) return opened;
            }

            // it must be a backup of THIS company's database: another company's data is never loaded into it
            await using (var master = new SqlConnection(Server().ConnectionString))
            {
                await master.OpenAsync(cancellationToken);
                await using var header = master.CreateCommand();
                header.CommandText = "RESTORE HEADERONLY FROM DISK = @path";
                header.Parameters.AddWithValue("@path", temp);
                header.CommandTimeout = 600;
                await using var reader = await header.ExecuteReaderAsync(cancellationToken);
                var name = await reader.ReadAsync(cancellationToken) ? Convert.ToString(reader["DatabaseName"], CultureInfo.InvariantCulture) : null;
                if (!string.Equals(name, database, StringComparison.OrdinalIgnoreCase))
                    return Result.Failure(Error.Validation("This backup belongs to another company and cannot be restored here."));
            }
            return await ReplaceAsync(tenantCode, database, temp, "uploaded backup", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Restore of {Code} from an uploaded backup failed", tenantCode);
            return Result.Failure(Error.BusinessRule("The restore failed. Reason: " + FirstLine(ex), "restore.failed"));
        }
        finally
        {
            if (temp is not null) { try { File.Delete(temp); } catch (IOException) { } }
            OneAtATime.Release();
        }
    }

    public async Task<int> DeleteOldScheduledAsync(string tenantCode, int keep, CancellationToken cancellationToken = default)
    {
        await using var master = new SqlConnection(Server().ConnectionString);
        await master.OpenAsync(cancellationToken);
        var database = await DatabaseOfAsync(master, tenantCode, cancellationToken);
        if (database is null) return 0;
        var folder = await FolderAsync(master, cancellationToken);
        if (!Directory.Exists(folder)) return 0;
        var old = new DirectoryInfo(folder).EnumerateFiles(database + "_*_auto.bak")
            .Where(f => IsBackupOf(database, f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Math.Max(1, keep)).ToList();
        var deleted = 0;
        foreach (var f in old) { try { f.Delete(); File.Delete(VerifyFile(f.FullName)); deleted++; } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return deleted;
    }

    private static string FirstLine(Exception ex) => ex.Message.Split('\n')[0].Trim();
}
