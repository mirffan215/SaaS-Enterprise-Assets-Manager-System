using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence.Tenancy;

/// <summary>Company keys and demo requests in the central catalog.</summary>
internal sealed class CompanyKeyStore : ICompanyKeyStore
{
    private readonly TenantCatalogDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CompanyKeyStore(TenantCatalogDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    private static CompanyKeyInfo Map(CompanyKeyRow r) =>
        new(r.Id, r.KeyHint, r.IssuedTo, r.Note, r.RequestId, r.CreatedAtUtc, r.CreatedBy, r.ExpiresAtUtc, r.UsedAtUtc, r.UsedCompanyCode, r.RevokedAtUtc);

    private static DemoRequestInfo Map(DemoRequestRow r) =>
        new(r.Id, r.CreatedAtUtc, r.FullName, r.CompanyName, r.Email, r.Phone, r.Message, r.Status, r.KeyId, r.HandledAtUtc);

    public async Task<(string Key, CompanyKeyInfo Info)> IssueAsync(string? issuedTo, string? note, int? requestId, int validDays, string createdBy, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var key = CompanyKeyFormat.Generate();
        var row = new CompanyKeyRow
        {
            KeyHash = CompanyKeyFormat.Hash(key), KeyHint = CompanyKeyFormat.Hint(key), IssuedTo = Trim(issuedTo, 256), Note = Trim(note, 200), RequestId = requestId,
            CreatedAtUtc = now, CreatedBy = Trim(createdBy, 100) ?? "operator", ExpiresAtUtc = now.AddDays(Math.Clamp(validDays, 1, 365))
        };
        _db.CompanyKeys.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return (key, Map(row));
    }

    public async Task<bool> IsValidAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CompanyKeyFormat.IsWellFormed(key)) return false;
        var hash = CompanyKeyFormat.Hash(key);
        var now = _clock.UtcNow;
        return await _db.CompanyKeys.AsNoTracking().AnyAsync(k => k.KeyHash == hash && k.UsedAtUtc == null && k.RevokedAtUtc == null && k.ExpiresAtUtc > now, cancellationToken);
    }

    public async Task<bool> TryReserveAsync(string key, string companyCode, CancellationToken cancellationToken = default)
    {
        if (!CompanyKeyFormat.IsWellFormed(key)) return false;
        var hash = CompanyKeyFormat.Hash(key);
        var now = _clock.UtcNow;
        var code = companyCode.Trim().ToLowerInvariant();
        // one UPDATE with the conditions in its WHERE: of two simultaneous attempts only one changes a row
        var changed = await _db.CompanyKeys.Where(k => k.KeyHash == hash && k.UsedAtUtc == null && k.RevokedAtUtc == null && k.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.UsedAtUtc, now).SetProperty(k => k.UsedCompanyCode, code), cancellationToken);
        return changed == 1;
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        var hash = CompanyKeyFormat.Hash(key);
        await _db.CompanyKeys.Where(k => k.KeyHash == hash && k.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.UsedAtUtc, (DateTime?)null).SetProperty(k => k.UsedCompanyCode, (string?)null), cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyKeyInfo>> ListAsync(int take, CancellationToken cancellationToken = default) =>
        (await _db.CompanyKeys.AsNoTracking().OrderByDescending(k => k.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<bool> RevokeAsync(int keyId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await _db.CompanyKeys.Where(k => k.Id == keyId && k.UsedAtUtc == null && k.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAtUtc, now), cancellationToken) == 1;
    }

    public async Task<int> AddRequestAsync(string fullName, string companyName, string email, string? phone, string? message, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var row = new DemoRequestRow
        {
            CreatedAtUtc = _clock.UtcNow, FullName = Trim(fullName, 150) ?? string.Empty, CompanyName = Trim(companyName, 150) ?? string.Empty, Email = Trim(email, 256) ?? string.Empty,
            Phone = Trim(phone, 50), Message = Trim(message, 1000), Status = DemoRequestStatus.New, IpAddress = Trim(ipAddress, 45)
        };
        _db.DemoRequests.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<DemoRequestInfo>> ListRequestsAsync(int take, CancellationToken cancellationToken = default) =>
        (await _db.DemoRequests.AsNoTracking().OrderBy(r => r.Status == DemoRequestStatus.New ? 0 : 1).ThenByDescending(r => r.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<DemoRequestInfo?> GetRequestAsync(int requestId, CancellationToken cancellationToken = default) =>
        await _db.DemoRequests.AsNoTracking().Where(r => r.Id == requestId).Select(r => Map(r)).FirstOrDefaultAsync(cancellationToken);

    public async Task SetRequestStatusAsync(int requestId, string status, int? keyId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await _db.DemoRequests.Where(r => r.Id == requestId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.KeyId, keyId).SetProperty(r => r.HandledAtUtc, now), cancellationToken);
    }

    private static string? Trim(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}
