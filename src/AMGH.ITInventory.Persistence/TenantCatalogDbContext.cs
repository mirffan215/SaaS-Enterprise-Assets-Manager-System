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
