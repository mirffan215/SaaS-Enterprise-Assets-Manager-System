using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>Settings for database-per-tenant operation (appsettings.json "Tenancy").</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Turns multi-tenant mode on. Needs the connection string "TenantCatalog".</summary>
    public bool Enabled { get; set; }

    /// <summary>Host suffix for sub-domain resolution: with "example.com", acme.example.com is tenant "acme".</summary>
    public string? BaseDomain { get; set; }

    /// <summary>Allows <c>?tenant=code</c> to pick the tenant (local development only).</summary>
    public bool AllowQueryParameter { get; set; }

    /// <summary>Privileged SQL Server connection (CREATE DATABASE rights), used only for provisioning.</summary>
    public string? ServerConnectionString { get; set; }

    /// <summary>
    /// Connection string the application uses at run time for each tenant database; "{database}" is replaced by the
    /// database name. Empty: the provisioning connection string with the database name replaced.
    /// </summary>
    public string? TenantConnectionTemplate { get; set; }

    /// <summary>Folder with the deployment scripts (relative to the application folder).</summary>
    public string ScriptsPath { get; set; } = "SQLScripts";

    /// <summary>Master-key password for the tenant's column encryption; random per tenant when empty.</summary>
    public string? MasterKeyPassword { get; set; }

    /// <summary>Application database login granted access by script 08; the script is skipped when empty.</summary>
    public string? AppLogin { get; set; }

    /// <summary>Code of the sandbox tenant.</summary>
    public const string DemoCode = "demo";

    /// <summary>Creates the sandbox tenant "demo" (with sample data) at start-up when it is missing, and offers "Explore Demo Environment" on the sign-in page.</summary>
    public bool DemoEnabled { get; set; }

    /// <summary>Password of the demo administrator ("admin"); the demo tenant is not created without it.</summary>
    public string? DemoAdminPassword { get; set; }

    /// <summary>Shows the demo user name and password on the demo sign-in page (they only open fictitious data).</summary>
    public bool ShowDemoCredentials { get; set; } = true;
}

/// <summary>A row of the central tenant catalog.</summary>
public sealed record TenantRecord(int Id, string Code, string Name, string ConnectionString, bool IsDemo, bool IsActive, DateTime CreatedAtUtc)
{
    public TenantInfo ToInfo() => new(Id, Code, Name, ConnectionString, IsDemo);
}

/// <summary>Reads and writes the central tenant catalog (one database listing all customers).</summary>
public interface ITenantCatalog
{
    /// <summary>The active tenant with this code (case-insensitive), or null.</summary>
    Task<TenantInfo?> FindActiveAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task<TenantRecord> AddAsync(string code, string name, string connectionString, bool isDemo, CancellationToken cancellationToken = default);
}

/// <summary>Hashes the first administrator's password in the application's own format, after the password policy.</summary>
public interface ITenantAdminPasswordHasher
{
    /// <summary>The hash, or the unmet password requirements.</summary>
    Result<string> HashNew(string username, string email, string password);
}

/// <summary>Creates a tenant: its own database, schema, seed data, administrator and catalog entry.</summary>
public interface ITenantProvisioningService
{
    Task<Result<TenantInfo>> ProvisionTenantAsync(string companyCode, string companyName, string adminEmail, string adminPassword,
        bool isDemo = false, CancellationToken cancellationToken = default);
}
