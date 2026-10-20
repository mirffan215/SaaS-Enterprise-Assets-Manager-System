using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence;

/// <summary>A customer in the central catalog. <see cref="ConnectionString"/> is stored protected ("enc:" prefix, see <c>TenantCatalog</c>).</summary>
public sealed class Tenant
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsDemo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>A company key (only its hash is stored).</summary>
public sealed class CompanyKeyRow
{
    public int Id { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public string KeyHint { get; set; } = string.Empty;
    public string? IssuedTo { get; set; }
    public string? Note { get; set; }
    public int? RequestId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string? UsedCompanyCode { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

/// <summary>Someone asked for a demo (public form on the front page).</summary>
public sealed class DemoRequestRow
{
    public int Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = "New";
    public int? KeyId { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>One line of the operator's activity log.</summary>
public sealed class TenantEventRow
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string? Code { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string Actor { get; set; } = string.Empty;
}

/// <summary>
/// The central tenant catalog: one small database that lists every customer and where its own database is.
/// It never holds customer data. Schema: deployment/SQLScripts/Catalog/C01_TenantCatalog.sql.
/// </summary>
public sealed class TenantCatalogDbContext : DbContext
{
    public TenantCatalogDbContext(DbContextOptions<TenantCatalogDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantEventRow> Events => Set<TenantEventRow>();
    public DbSet<CompanyKeyRow> CompanyKeys => Set<CompanyKeyRow>();
    public DbSet<DemoRequestRow> DemoRequests => Set<DemoRequestRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(t => t.Id);
            e.Property(t => t.Code).HasMaxLength(10).IsRequired();
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.Property(t => t.ConnectionString).HasMaxLength(2000).IsRequired();
            e.Property(t => t.CreatedAtUtc).HasColumnType("datetime2(3)");
            e.HasIndex(t => t.Code).IsUnique();
        });
        modelBuilder.Entity<CompanyKeyRow>(e =>
        {
            e.ToTable("CompanyKeys");
            e.HasKey(t => t.Id);
            e.Property(t => t.KeyHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
            e.Property(t => t.KeyHint).HasMaxLength(8).IsRequired();
            e.Property(t => t.IssuedTo).HasMaxLength(256);
            e.Property(t => t.Note).HasMaxLength(200);
            e.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
            e.Property(t => t.UsedCompanyCode).HasMaxLength(10);
            e.Property(t => t.CreatedAtUtc).HasColumnType("datetime2(3)");
            e.Property(t => t.ExpiresAtUtc).HasColumnType("datetime2(3)");
            e.Property(t => t.UsedAtUtc).HasColumnType("datetime2(3)");
            e.Property(t => t.RevokedAtUtc).HasColumnType("datetime2(3)");
            e.HasIndex(t => t.KeyHash).IsUnique();
        });
        modelBuilder.Entity<DemoRequestRow>(e =>
        {
            e.ToTable("DemoRequests");
            e.HasKey(t => t.Id);
            e.Property(t => t.FullName).HasMaxLength(150).IsRequired();
            e.Property(t => t.CompanyName).HasMaxLength(150).IsRequired();
            e.Property(t => t.Email).HasMaxLength(256).IsRequired();
            e.Property(t => t.Phone).HasMaxLength(50);
            e.Property(t => t.Message).HasMaxLength(1000);
            e.Property(t => t.Status).HasMaxLength(20).IsRequired();
            e.Property(t => t.IpAddress).HasMaxLength(45);
            e.Property(t => t.CreatedAtUtc).HasColumnType("datetime2(3)");
            e.Property(t => t.HandledAtUtc).HasColumnType("datetime2(3)");
        });
        modelBuilder.Entity<TenantEventRow>(e =>
        {
            e.ToTable("TenantEvents");
            e.HasKey(t => t.Id);
            e.Property(t => t.Code).HasMaxLength(10);
            e.Property(t => t.Action).HasMaxLength(60).IsRequired();
            e.Property(t => t.Detail).HasMaxLength(1000);
            e.Property(t => t.Actor).HasMaxLength(100).IsRequired();
            e.Property(t => t.AtUtc).HasColumnType("datetime2(3)");
        });
    }
}
