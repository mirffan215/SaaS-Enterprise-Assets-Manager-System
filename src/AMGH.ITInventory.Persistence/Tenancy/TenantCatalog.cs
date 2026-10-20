using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AMGH.ITInventory.Persistence.Tenancy;

/// <summary>
/// Catalog access. Connection strings are encrypted at rest (ASP.NET Data Protection, "enc:" prefix); a plain
/// connection string written by an operator with SQL is accepted as well. Lookups are cached for a minute.
/// </summary>
internal sealed class TenantCatalog : ITenantCatalog, ITenantAdminStore
{
    private const string Prefix = "enc:";
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(1);

    private readonly TenantCatalogDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly IDateTimeProvider _clock;

    public TenantCatalog(TenantCatalogDbContext db, ISecretProtector protector, IMemoryCache cache, IDateTimeProvider clock)
    {
        _db = db;
        _protector = protector;
        _cache = cache;
        _clock = clock;
    }

    private static string Key(string code) => "tenant:" + code.Trim().ToLowerInvariant();

    public async Task<TenantInfo?> FindActiveAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 10) return null;
        var key = Key(code);
        if (_cache.TryGetValue(key, out TenantInfo? cached)) return cached;
        var lower = code.Trim().ToLowerInvariant();
        var row = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Code == lower && t.IsActive, cancellationToken);
        var info = row is null ? null : ToRecord(row).ToInfo();
        _cache.Set(key, info, CacheTime);
        return info;
    }

    public Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var lower = code.Trim().ToLowerInvariant();
        return _db.Tenants.AnyAsync(t => t.Code == lower, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        (await _db.Tenants.AsNoTracking().OrderBy(t => t.Code).ToListAsync(cancellationToken)).Select(ToRecord).ToList();

    public async Task<TenantRecord> AddAsync(string code, string name, string connectionString, bool isDemo, CancellationToken cancellationToken = default)
    {
        var row = new Tenant
        {
            Code = code.Trim().ToLowerInvariant(),
            Name = name,
            ConnectionString = Prefix + _protector.Protect(connectionString),
            IsDemo = isDemo,
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow
        };
        _db.Tenants.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(Key(row.Code));
        return ToRecord(row);
    }

    public async Task<bool> SetActiveAsync(string code, bool active, CancellationToken cancellationToken = default)
    {
        var lower = code.Trim().ToLowerInvariant();
        var row = await _db.Tenants.FirstOrDefaultAsync(t => t.Code == lower, cancellationToken);
        if (row is null) return false;
        row.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(Key(lower));
        return true;
    }

    public Task<bool> IsSuspendedAsync(string code, CancellationToken cancellationToken = default)
    {
        var lower = code.Trim().ToLowerInvariant();
        return _db.Tenants.AsNoTracking().AnyAsync(t => t.Code == lower && !t.IsActive, cancellationToken);
    }

    public async Task RecordEventAsync(string? code, string action, string? detail, string actor, CancellationToken cancellationToken = default)
    {
        _db.Events.Add(new TenantEventRow
        {
            AtUtc = _clock.UtcNow,
            Code = code?.ToLowerInvariant(),
            Action = action.Length > 60 ? action[..60] : action,
            Detail = detail is { Length: > 1000 } ? detail[..1000] : detail,
            Actor = actor.Length > 100 ? actor[..100] : actor
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantEvent>> RecentEventsAsync(int take, CancellationToken cancellationToken = default) =>
        (await _db.Events.AsNoTracking().OrderByDescending(e => e.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken))
            .Select(e => new TenantEvent(e.Id, DateTime.SpecifyKind(e.AtUtc, DateTimeKind.Utc), e.Code, e.Action, e.Detail, e.Actor)).ToList();

    private TenantRecord ToRecord(Tenant t)
    {
        var connection = t.ConnectionString.StartsWith(Prefix, StringComparison.Ordinal)
            ? _protector.Unprotect(t.ConnectionString[Prefix.Length..]) ?? throw new InvalidOperationException($"The connection string of tenant {t.Code} cannot be decrypted (data-protection keys changed?).")
            : t.ConnectionString;
        return new TenantRecord(t.Id, t.Code, t.Name, connection, t.IsDemo, t.IsActive, t.CreatedAtUtc);
    }
}
