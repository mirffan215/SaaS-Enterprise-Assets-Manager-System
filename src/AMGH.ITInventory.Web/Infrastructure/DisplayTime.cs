using System.Collections.Concurrent;
using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Security.Claims;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Shows times in the signed-in company's time zone and date format (UTC is stored).</summary>
public interface IDisplayTime
{
    string ZoneLabel { get; }
    DateTime ToLocal(DateTime utc);
    /// <summary>Without a format: the company's date format plus HH:mm.</summary>
    string Format(DateTime? utc, string? format = null);
    DateTime? LocalDateToUtc(DateTime? localDate);
    /// <summary>The company's date format (e.g. "dd MMM yyyy").</summary>
    string DateFormat { get; }
    /// <summary>A calendar date in the company's format (empty when null).</summary>
    string Date(DateOnly? date);
    /// <summary>The local date of a UTC moment in the company's format (empty when null).</summary>
    string Date(DateTime? utc);
    void Invalidate(int companyId);
}

/// <summary>
/// Release 2.8: per-company time zone and date format (Company settings), cached for 5 minutes per company and refreshed
/// at once when the company is saved. Without a signed-in company (background jobs) the server setting Display:TimeZone applies.
/// </summary>
internal sealed class DisplayTime : IDisplayTime
{
    private readonly IHttpContextAccessor _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly (TimeZoneInfo Zone, string DateFormat) _default;
    private static readonly ConcurrentDictionary<int, (TimeZoneInfo Zone, string DateFormat, DateTime Expires)> Cache = new();

    public DisplayTime(IConfiguration configuration, ILogger<DisplayTime> logger, IHttpContextAccessor http, IServiceScopeFactory scopes)
    {
        _http = http;
        _scopes = scopes;
        _default = (Find(configuration["Display:TimeZone"] ?? "Arab Standard Time") ?? TimeZoneInfo.Utc, "dd MMM yyyy");
    }

    private static TimeZoneInfo? Find(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : null;

    private (TimeZoneInfo Zone, string DateFormat) Current
    {
        get
        {
            if (_http.HttpContext?.User.GetCompanyId() is not { } companyId) return _default;
            if (Cache.TryGetValue(companyId, out var c) && c.Expires > DateTime.UtcNow) return (c.Zone, c.DateFormat);
            using var scope = _scopes.CreateScope();
            var row = scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Repository<Company>()
                .QueryAsync(q => q.Where(x => x.CompanyId == companyId).Select(x => new { x.TimeZone, x.DateFormat })).GetAwaiter().GetResult().FirstOrDefault();
            var settings = (row is null ? null : Find(row.TimeZone)) is { } zone ? (zone, row!.DateFormat) : _default;
            Cache[companyId] = (settings.Item1, settings.Item2, DateTime.UtcNow.AddMinutes(5));
            return settings;
        }
    }

    public string ZoneLabel
    {
        get
        {
            var offset = Current.Zone.BaseUtcOffset;
            return offset == TimeSpan.Zero ? "UTC" : string.Create(CultureInfo.InvariantCulture, $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}");
        }
    }

    public string DateFormat => Current.DateFormat;

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Current.Zone);

    public string Format(DateTime? utc, string? format = null) =>
        utc is null ? "—" : ToLocal(utc.Value).ToString(format ?? Current.DateFormat + " HH:mm", CultureInfo.InvariantCulture);

    public DateTime? LocalDateToUtc(DateTime? localDate) =>
        localDate is null ? null : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified), Current.Zone);

    public string Date(DateOnly? date) => date?.ToString(Current.DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    public string Date(DateTime? utc) => utc is null ? string.Empty : ToLocal(utc.Value).ToString(Current.DateFormat, CultureInfo.InvariantCulture);

    public void Invalidate(int companyId) => Cache.TryRemove(companyId, out _);
}
