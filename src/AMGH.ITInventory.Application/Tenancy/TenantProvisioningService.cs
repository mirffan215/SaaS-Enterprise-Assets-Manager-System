using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>
/// Provisions a customer: <c>CREATE DATABASE [ITInv_Tenant_{code}]</c>, the baseline scripts (01 to 18, in the order of
/// Deploy-Database.ps1), standard roles and permissions (script 07), the tenant administrator, and the catalog entry.
/// A database created by a run that then fails is dropped again, so a retry starts clean.
/// </summary>
public sealed partial class TenantProvisioningService : ITenantProvisioningService
{
    /// <summary>Same order as deployment/PowerShell/Deploy-Database.ps1; 08 (application login) only runs when a login is configured.</summary>
    internal static readonly string[] ScriptOrder =
    {
        "01_CreateDatabase.sql", "02_Tables.sql", "10_MultiCompany.sql", "16_AssetConfiguration.sql", "17_TechnicianAssignment.sql",
        "03_Functions.sql", "04_Views.sql", "05_StoredProcedures.sql", "06_Triggers.sql", "07_SeedData.sql", "08_DatabaseSecurity.sql",
        "11_Workspaces.sql", "12_Reports.sql", "13_Approvals.sql", "14_Notifications.sql", "15_CompanyProfile.sql", "18_DocumentNumbering.sql"
    };

    internal const string DatabasePrefix = "ITInv_Tenant_";
    internal const string AdminUsername = "admin";
    private static readonly string[] ReservedCodes = { "www", "api", "admin", "platform", "catalog", "master", "tempdb", "model", "msdb", "system", "app", "mail" };

    [GeneratedRegex("^[A-Za-z0-9]{2,10}$")] private static partial Regex CodePattern();
    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailPattern();

    private readonly ITenantCatalog _catalog;
    private readonly ITenantAdminPasswordHasher _hasher;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(ITenantCatalog catalog, ITenantAdminPasswordHasher hasher, IOptions<TenancyOptions> options,
        ILogger<TenantProvisioningService> logger)
    {
        _catalog = catalog;
        _hasher = hasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<TenantInfo>> ProvisionTenantAsync(string companyCode, string companyName, string adminEmail, string adminPassword,
        bool isDemo = false, CancellationToken cancellationToken = default)
    {
        companyCode = (companyCode ?? string.Empty).Trim();
        companyName = (companyName ?? string.Empty).Trim();
        adminEmail = (adminEmail ?? string.Empty).Trim();
        if (!CodePattern().IsMatch(companyCode))
            return Result<TenantInfo>.Failure(Error.Validation("The company code must be 2 to 10 letters or digits.", "tenant.code"));
        if (ReservedCodes.Contains(companyCode, StringComparer.OrdinalIgnoreCase))
            return Result<TenantInfo>.Failure(Error.Validation("That company code is reserved.", "tenant.code"));
        if (companyName.Length is < 2 or > 100)
            return Result<TenantInfo>.Failure(Error.Validation("The company name must be 2 to 100 characters.", "tenant.name"));
        if (adminEmail.Length > 256 || !EmailPattern().IsMatch(adminEmail))
            return Result<TenantInfo>.Failure(Error.Validation("Enter a valid administrator e-mail address.", "tenant.email"));
        if (string.IsNullOrWhiteSpace(_options.ServerConnectionString))
            return Result<TenantInfo>.Failure(Error.BusinessRule("Tenant provisioning is not configured (Tenancy:ServerConnectionString).", "tenant.not_configured"));

        var hash = _hasher.HashNew(AdminUsername, adminEmail, adminPassword);
        if (hash.IsFailure) return Result<TenantInfo>.Failure(hash.Errors.ToArray());

        var scripts = ResolveScripts();
        if (scripts.IsFailure) return Result<TenantInfo>.Failure(scripts.Errors.ToArray());

        var lower = companyCode.ToLowerInvariant();
        if (await _catalog.ExistsAsync(lower, cancellationToken))
            return Result<TenantInfo>.Failure(Error.Conflict("A company with that code already exists.", "tenant.exists"));

        var database = DatabasePrefix + companyCode;
        var server = new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = "master" };
        var created = false;
        try
        {
            await using (var master = new SqlConnection(server.ConnectionString))
            {
                await master.OpenAsync(cancellationToken);
                if (await DatabaseExistsAsync(master, database, cancellationToken))
                    return Result<TenantInfo>.Failure(Error.Conflict("A database for that company code already exists.", "tenant.database_exists"));
                // The name is validated (letters, digits, underscore only), so bracket quoting is safe.
                await using (var create = master.CreateCommand())
                {
                    create.CommandText = $"CREATE DATABASE [{database}]";
                    create.CommandTimeout = 300;
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
                created = true;
                _logger.LogInformation("Tenant database {Database} created", database);

                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DatabaseName"] = database,
                    ["MasterKeyPassword"] = string.IsNullOrWhiteSpace(_options.MasterKeyPassword) ? RandomPassword() : _options.MasterKeyPassword,
                    ["AppLogin"] = _options.AppLogin ?? string.Empty,
                    ["AllowMultiCompany"] = "YES",
                    ["AdminUsername"] = AdminUsername,
                    ["AdminEmail"] = adminEmail,
                    ["AdminDisplayName"] = "Administrator",
                    ["AdminPasswordHash"] = hash.Value
                };
                foreach (var file in scripts.Value)
                {
                    if (file.EndsWith("08_DatabaseSecurity.sql", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(_options.AppLogin)) continue;
                    _logger.LogInformation("Tenant {Database}: running {Script}", database, Path.GetFileName(file));
                    await SqlScriptRunner.RunAsync(master, await File.ReadAllTextAsync(file, cancellationToken), variables, cancellationToken);
                }
            }

            await FinishCompanyAsync(server, database, companyCode, companyName, cancellationToken);

            var connectionString = TenantConnectionString(database);
            var record = await _catalog.AddAsync(lower, companyName, connectionString, isDemo, cancellationToken);
            _logger.LogInformation("Tenant {Code} provisioned in {Database}", lower, database);
            return Result<TenantInfo>.Success(record.ToInfo());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Provisioning tenant {Code} failed", lower);
            if (created) await TryDropAsync(server, database);
            return Result<TenantInfo>.Failure(Error.BusinessRule("The company could not be created. Nothing was kept; try again or contact support.", "tenant.failed"));
        }
    }

    /// <summary>The scripts present in the configured folder, in deployment order; fails when one is missing.</summary>
    private Result<IReadOnlyList<string>> ResolveScripts()
    {
        var folder = Path.IsPathRooted(_options.ScriptsPath) ? _options.ScriptsPath : Path.Combine(AppContext.BaseDirectory, _options.ScriptsPath);
        var files = new List<string>();
        foreach (var name in ScriptOrder)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
                return Result<IReadOnlyList<string>>.Failure(Error.BusinessRule($"Deployment script {name} was not found in {folder}.", "tenant.script_missing"));
            files.Add(path);
        }
        return Result<IReadOnlyList<string>>.Success(files);
    }

    /// <summary>The scripts create the first company as a placeholder; make it the customer's company and let the administrator keep the chosen password.</summary>
    private static async Task FinishCompanyAsync(SqlConnectionStringBuilder server, string database, string code, string name, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = database };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            @"UPDATE dbo.tblCompany SET CompanyCode = @code, LegalName = @name, DisplayName = @name, Phone = NULL, City = NULL,
                     Country = N'Not set', AssetNumberPrefix = @prefix, ModifiedBy = N'PROVISIONING', ModifiedDate = SYSUTCDATETIME()
              WHERE CompanyId = 1;
              UPDATE dbo.tblUser SET MustChangePassword = 0 WHERE Username = @admin;";
        command.Parameters.AddWithValue("@code", code.ToUpperInvariant());
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@prefix", code.ToUpperInvariant() + "-IT");
        command.Parameters.AddWithValue("@admin", AdminUsername);
        await command.ExecuteNonQueryAsync(ct);
    }

    private string TenantConnectionString(string database)
    {
        if (!string.IsNullOrWhiteSpace(_options.TenantConnectionTemplate))
            return _options.TenantConnectionTemplate.Replace("{database}", database, StringComparison.Ordinal);
        return new SqlConnectionStringBuilder(_options.ServerConnectionString) { InitialCatalog = database }.ConnectionString;
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection master, string database, CancellationToken ct)
    {
        await using var command = master.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", database);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    }

    private async Task TryDropAsync(SqlConnectionStringBuilder server, string database)
    {
        try
        {
            await using var master = new SqlConnection(server.ConnectionString);
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
            await command.ExecuteNonQueryAsync();
            _logger.LogWarning("Tenant database {Database} dropped after a failed provisioning run", database);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not drop {Database} after a failed provisioning run; remove it manually", database);
        }
    }

    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).Replace('/', '_').Replace('+', '-') + "aA1!";
}
