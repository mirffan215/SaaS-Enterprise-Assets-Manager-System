using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

public sealed record SingleDatabaseSetupResult(string AdminUsername, string? GeneratedMasterKeyPassword);

/// <summary>
/// Sets up (and later upgrades) the ONE database of a single-database installation, for hosting where you can only use a database that already
/// exists (Plesk and similar control panels): the program builds the schema itself over the connection string you were given; no sqlcmd, no
/// CREATE DATABASE, no server-level rights. Needs a login that owns the database (db_owner).
/// </summary>
public interface ISingleDatabaseSetup
{
    /// <summary>True when the database already holds EAMS tables (a company exists).</summary>
    Task<bool> IsInitializedAsync(string connectionString, CancellationToken cancellationToken = default);

    /// <summary>Builds the tables, security objects and seed data, makes the first company and its administrator ("admin").</summary>
    Task<Result<SingleDatabaseSetupResult>> InitializeAsync(string connectionString, string companyCode, string companyName, string adminEmail, string adminPassword,
        string? masterKeyPassword, CancellationToken cancellationToken = default);

    /// <summary>Applies the migration scripts that are still missing; returns how many ran (0 when up to date or not set up yet).</summary>
    Task<int> UpgradeAsync(string connectionString, CancellationToken cancellationToken = default);
}

internal sealed partial class SingleDatabaseSetup : ISingleDatabaseSetup
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    [GeneratedRegex("^[A-Za-z0-9]{2,10}$")] private static partial Regex CodePattern();
    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailPattern();

    private readonly ITenantAdminPasswordHasher _hasher;
    private readonly TenancyOptions _options;
    private readonly ILogger<SingleDatabaseSetup> _logger;

    public SingleDatabaseSetup(ITenantAdminPasswordHasher hasher, IOptions<TenancyOptions> options, ILogger<SingleDatabaseSetup> logger)
    {
        _hasher = hasher;
        _options = options.Value;
        _logger = logger;
    }

    private string ScriptsFolder => Path.IsPathRooted(_options.ScriptsPath) ? _options.ScriptsPath : Path.Combine(AppContext.BaseDirectory, _options.ScriptsPath);

    public async Task<bool> IsInitializedAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync(cancellationToken);
        await using var command = db.CreateCommand();
        // dynamic SQL: the batch must compile while the table does not exist yet
        command.CommandText = "IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NULL SELECT 0 ELSE EXEC(N'SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.tblCompany) THEN 1 ELSE 0 END')";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<Result<SingleDatabaseSetupResult>> InitializeAsync(string connectionString, string companyCode, string companyName, string adminEmail, string adminPassword,
        string? masterKeyPassword, CancellationToken cancellationToken = default)
    {
        companyCode = (companyCode ?? string.Empty).Trim();
        companyName = (companyName ?? string.Empty).Trim();
        adminEmail = (adminEmail ?? string.Empty).Trim();
        if (!CodePattern().IsMatch(companyCode)) return Result<SingleDatabaseSetupResult>.Failure(Error.Validation("The company code must be 2 to 10 letters or digits.", "code"));
        if (companyName.Length is < 2 or > 100) return Result<SingleDatabaseSetupResult>.Failure(Error.Validation("The company name must be 2 to 100 characters.", "name"));
        if (adminEmail.Length > 256 || !EmailPattern().IsMatch(adminEmail)) return Result<SingleDatabaseSetupResult>.Failure(Error.Validation("Enter a valid administrator e-mail address.", "email"));
        var hash = _hasher.HashNew(TenantProvisioningService.AdminUsername, adminEmail, adminPassword);
        if (hash.IsFailure) return Result<SingleDatabaseSetupResult>.Failure(hash.Errors.ToArray());

        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database) || !DatabaseName().IsMatch(database))
            return Result<SingleDatabaseSetupResult>.Failure(Error.BusinessRule("The connection string must name the database (Database=...), using letters, digits, '_', '-' or '.'.", "setup.database"));

        var scripts = new List<string>();
        foreach (var name in TenantProvisioningService.ScriptOrder.Where(n => !n.StartsWith("08_", StringComparison.Ordinal)))
        {
            var path = Path.Combine(ScriptsFolder, name);
            if (!File.Exists(path)) return Result<SingleDatabaseSetupResult>.Failure(Error.BusinessRule($"Deployment script {name} was not found in {ScriptsFolder}.", "setup.script_missing"));
            scripts.Add(path);
        }

        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            if (await IsInitializedAsync(connectionString, cancellationToken))
                return Result<SingleDatabaseSetupResult>.Failure(Error.Conflict("This database is already set up.", "setup.done"));
            var generated = string.IsNullOrWhiteSpace(masterKeyPassword);
            var master = generated ? RandomPassword() : masterKeyPassword!;
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabaseName"] = database,
                ["MasterKeyPassword"] = master,
                ["AppLogin"] = string.Empty,
                ["AllowMultiCompany"] = "YES",
                ["AdminUsername"] = TenantProvisioningService.AdminUsername,
                ["AdminEmail"] = adminEmail,
                ["AdminDisplayName"] = "Administrator",
                ["AdminPasswordHash"] = hash.Value
            };
            await using (var db = new SqlConnection(connectionString))
            {
                await db.OpenAsync(cancellationToken);
                foreach (var file in scripts)
                {
                    _logger.LogInformation("Setup {Database}: running {Script}", database, Path.GetFileName(file));
                    var text = await File.ReadAllTextAsync(file, cancellationToken);
                    var tolerant = Path.GetFileName(file).StartsWith("01_", StringComparison.Ordinal);
                    foreach (var batch in SqlScriptRunner.ToBatches(text, variables))
                    {
                        try
                        {
                            await using var command = db.CreateCommand();
                            command.CommandText = batch;
                            command.CommandTimeout = 600;
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }
                        // a hosting provider may keep database options (recovery model, snapshot isolation) to itself: that is not a reason to stop
                        catch (SqlException ex) when (tolerant && batch.Contains("ALTER DATABASE", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("Setup {Database}: a database option could not be set ({Message})", database, ex.Message.Split('\n')[0].Trim());
                        }
                    }
                }
                await TenantMigrationService.ApplyAsync(db, TenantMigrationService.Load(TenantMigrationService.MigrationsFolder(_options)), _logger, cancellationToken);
            }
            await TenantProvisioningService.FinishCompanyAsync(builder, database, companyCode, companyName, cancellationToken);
            _logger.LogInformation("Database {Database} set up for company {Code}", database, companyCode);
            return Result<SingleDatabaseSetupResult>.Success(new SingleDatabaseSetupResult(TenantProvisioningService.AdminUsername, generated ? master : null));
        }
        catch (Exception ex) when (ex is SqlException or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Setting up database {Database} failed", database);
            return Result<SingleDatabaseSetupResult>.Failure(Error.BusinessRule("The database could not be set up. " + ex.Message.Split('\n')[0].Trim() +
                " (The database user needs full rights on this database: it must be its owner.) The setup can be started again; if tables were already made, ask the hosting provider to empty the database first.", "setup.failed"));
        }
        finally { OneAtATime.Release(); }
    }

    public async Task<int> UpgradeAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (!await IsInitializedAsync(connectionString, cancellationToken)) return 0;
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync(cancellationToken);
        return await TenantMigrationService.ApplyAsync(db, TenantMigrationService.Load(TenantMigrationService.MigrationsFolder(_options)), _logger, cancellationToken);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,128}$")] private static partial Regex DatabaseName();

    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).Replace('/', '_').Replace('+', '-') + "aA1!";
}
