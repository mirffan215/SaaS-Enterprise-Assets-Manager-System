using AMGH.ITInventory.Domain.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Persistence.Auditing;
using AMGH.ITInventory.Persistence.Extensions;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AMGH.ITInventory.Persistence;

/// <summary>
/// EF Core context for the AMGH_ITInventory database. On every save it:
/// <list type="number">
/// <item>stamps CreatedBy/CreatedDate/ModifiedBy/ModifiedDate from the current user and UTC clock;</item>
/// <item>converts deletes into soft deletes (IsDeleted/DeletedBy/DeletedDate);</item>
/// <item>writes an audit row per changed entity to dbo.tblAuditLog in the same transaction;</item>
/// <item>sets CompanyId on new company-owned rows from the current workspace and refuses any change to it.</item>
/// </list>
/// Every query on company-owned data is filtered by <see cref="CurrentCompanyId"/>; SQL Server Row-Level Security
/// enforces the same rule on the connection (see <see cref="CompanySessionInterceptor"/>).
/// The SQL scripts in deployment/SQLScripts are the source of truth for the schema; this model mirrors them.
/// </summary>
public sealed partial class AMGHDbContext : DbContext
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICompanyContext _company;
    private readonly ITenantAccessor _tenants;

    public AMGHDbContext(DbContextOptions<AMGHDbContext> options, ICurrentUserService currentUser, IDateTimeProvider clock,
        ICompanyContext company, ITenantAccessor? tenants = null)
        : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
        _company = company;
        _tenants = tenants ?? new TenantAccessor();
    }

    /// <summary>
    /// Database per tenant: when a tenant is active its dedicated database replaces the default connection string.
    /// In multi-tenant mode a context without a tenant is refused, so data can never fall back to another customer's database.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_tenants.CurrentTenant is { } tenant)
        {
            optionsBuilder.UseSqlServer(tenant.ConnectionString, sql =>
            {
                sql.CommandTimeout(60);
                sql.MigrationsAssembly(typeof(AMGHDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
            });
        }
        else if (_tenants.IsMultiTenant)
            throw new InvalidOperationException("No tenant is active: the request or job must run inside a tenant (ITenantAccessor).");
    }

    /// <summary>Company used by the query filters; -1 (matches nothing) when no company is active.</summary>
    public int CurrentCompanyId => _company.CompanyId ?? -1;

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AMGHDbContext).Assembly);
        modelBuilder.ApplyQueryFilters(UnfilteredLookupTypes, this);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = PrepareForSave(acceptAllChangesOnSuccess);
        if (pending.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);

        IDbContextTransaction? ownTransaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        try
        {
            var affected = base.SaveChanges(true);
            AuditLogs.AddRange(ToAuditLogs(pending));
            base.SaveChanges(true);
            ownTransaction?.Commit();
            return affected;
        }
        catch
        {
            ownTransaction?.Rollback();
            throw;
        }
        finally
        {
            ownTransaction?.Dispose();
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = PrepareForSave(acceptAllChangesOnSuccess);
        if (pending.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        IDbContextTransaction? ownTransaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var affected = await base.SaveChangesAsync(true, cancellationToken);
            AuditLogs.AddRange(ToAuditLogs(pending));
            await base.SaveChangesAsync(true, cancellationToken);
            if (ownTransaction is not null) await ownTransaction.CommitAsync(cancellationToken);
            return affected;
        }
        catch
        {
            if (ownTransaction is not null) await ownTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownTransaction is not null) await ownTransaction.DisposeAsync();
        }
    }

    /// <summary>Captures the audit trail, then applies audit stamps and soft-delete conversion.</summary>
    private List<PendingAuditEntry> PrepareForSave(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        var now = _clock.UtcNow;
        var user = _currentUser.UserName;
        StampCompany();

        // Capture first: the original state (e.g. Deleted) determines the audit action.
        var pending = AuditTrailBuilder.Capture(ChangeTracker, _currentUser, now);
        if (pending.Count > 0 && !acceptAllChangesOnSuccess)
            throw new NotSupportedException("SaveChanges(acceptAllChangesOnSuccess: false) is not supported for audited entities.");

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedBy = user;
                    entry.Entity.CreatedDate = now;
                    entry.Entity.ModifiedBy = null;
                    entry.Entity.ModifiedDate = null;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedBy = user;
                    entry.Entity.ModifiedDate = now;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    entry.Property(e => e.CreatedDate).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // The database denies DELETE to the application: convert to a soft delete.
                    entry.State = EntityState.Modified;
                    entry.Entity.SoftDelete(user, now);
                    entry.Entity.ModifiedBy = user;
                    entry.Entity.ModifiedDate = now;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    entry.Property(e => e.CreatedDate).IsModified = false;
                    break;
            }
        }
        return pending;
    }

    /// <summary>
    /// New company-owned rows take the current company; a row for another company, or a change of CompanyId,
    /// is refused before it reaches the database (where Row-Level Security would refuse it too).
    /// </summary>
    private void StampCompany()
    {
        foreach (var entry in ChangeTracker.Entries<ICompanyOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    var company = _company.CompanyId
                        ?? throw new InvalidOperationException($"Cannot create {entry.Metadata.ClrType.Name}: no company is active.");
                    if (entry.Entity.CompanyId == 0) entry.Entity.CompanyId = company;
                    else if (entry.Entity.CompanyId != company)
                        throw new InvalidOperationException($"Cannot create {entry.Metadata.ClrType.Name} for another company.");
                    break;
                case EntityState.Modified or EntityState.Deleted:
                    var property = entry.Property(nameof(ICompanyOwned.CompanyId));
                    if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                        throw new InvalidOperationException($"The company of a {entry.Metadata.ClrType.Name} cannot be changed.");
                    property.IsModified = false;
                    break;
            }
        }
        foreach (var entry in ChangeTracker.Entries<ICompanyScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CompanyId is null)
                entry.Entity.CompanyId = _company.CompanyId;
        }
    }

    private List<Domain.Entities.AuditLog> ToAuditLogs(List<PendingAuditEntry> pending)
    {
        var logs = pending.Select(p => p.ToAuditLog()).ToList();
        foreach (var log in logs) log.CompanyId = _company.CompanyId;
        return logs;
    }
}
