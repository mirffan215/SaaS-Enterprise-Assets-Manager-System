using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;

namespace AMGH.ITInventory.Application.Dashboard;

public sealed class DashboardFilter
{
    /// <summary>Acquisition date range (purchase date, or registration date when no purchase date).</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? DepartmentId { get; set; }
    public int? BuildingId { get; set; }
    public int? CategoryId { get; set; }
    public int? StatusId { get; set; }

    public bool IsEmpty => From is null && To is null && DepartmentId is null && BuildingId is null && CategoryId is null && StatusId is null;
}

/// <summary>A KPI card. <see cref="DrillQuery"/> is the asset-list query that shows exactly the counted assets.</summary>
public sealed record Kpi(string Key, string Title, int Value, string Tone, string Icon, string Definition, string? Note, string DrillQuery);

public sealed record ChartData(string Key, string Title, string Kind, IReadOnlyList<string> Labels, IReadOnlyList<decimal> Values,
    IReadOnlyList<string>? Colors = null, string? Note = null, IReadOnlyList<decimal>? SecondValues = null, string? SecondLabel = null,
    IReadOnlyList<string>? DrillQueries = null);

public sealed record AttentionItem(int AssetId, string AssetNumber, string AssetName, string Reason, int Severity);

public sealed record DashboardData(IReadOnlyList<Kpi> Kpis, IReadOnlyList<ChartData> Charts, IReadOnlyList<AttentionItem> Attention,
    DateTime GeneratedUtc, bool HasWarrantyData, string? ValueCurrency, int ExcludedFromValues);

public interface IDashboardService
{
    Task<DashboardData> GetAsync(DashboardFilter filter, bool canViewFinancials, bool canViewSecurity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executive dashboard computed from the current company's records (company isolation applies to every query).
/// Lifecycle states never overlap (one status per asset), so the status-based cards do not double count.
/// </summary>
internal sealed class DashboardService : IDashboardService
{
    private static readonly string[] Palette =
        { "#13707A", "#E8604C", "#0E3B43", "#F2A541", "#5B8C5A", "#7A5C99", "#2E86AB", "#C44536", "#8D99AE", "#3D5A80", "#98C1D9", "#B5838D" };

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ICompanyContext _company;

    public DashboardService(IUnitOfWork uow, IDateTimeProvider clock, ICompanyContext company)
    {
        _uow = uow;
        _clock = clock;
        _company = company;
    }

    public async Task<DashboardData> GetAsync(DashboardFilter filter, bool canViewFinancials, bool canViewSecurity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var ct = cancellationToken;
        var today = _clock.UtcToday;
        var assets = _uow.Repository<Asset>();

        // Base set: filters applied, disposed included (the "Disposed" card); "active" excludes disposed.
        IQueryable<Asset> Base(IQueryable<Asset> q) => ApplyFilter(q, filter);
        IQueryable<Asset> Active(IQueryable<Asset> q) => Base(q).Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed);
        var drill = DrillBase(filter);

        var byStatus = await assets.QueryAsync(q => Base(q).GroupBy(a => new { a.AssetStatus.StatusCode, a.AssetStatus.StatusName, a.AssetStatus.ColorHex, a.AssetStatus.SortOrder })
            .Select(g => new { g.Key.StatusCode, g.Key.StatusName, g.Key.ColorHex, g.Key.SortOrder, Count = g.Count() }), ct);
        int Status(params string[] codes) => byStatus.Where(s => codes.Contains(s.StatusCode)).Sum(s => s.Count);

        async Task<int> Count(string focus) => await assets.QueryCountAsync(q => AssetQueries.ApplyFocus(Active(q), focus, today), ct);
        var missingQr = await Count(AssetFocus.MissingQr);
        var expired = await Count(AssetFocus.WarrantyExpired);
        var expiring = await Count(AssetFocus.WarrantyExpiring);
        var highRisk = await Count(AssetFocus.HighRisk);
        var critical = await Count(AssetFocus.Critical);
        var repair = await Count(AssetFocus.Repair);
        var newThisMonth = await assets.QueryCountAsync(q => Active(q).Where(a => a.CreatedDate >= today.AddDays(-30).ToDateTime(TimeOnly.MinValue)), ct);
        var anyWarranty = await _uow.Repository<Warranty>().AnyAsync(w => !w.IsDeleted, ct);

        var total = byStatus.Where(s => s.StatusCode != AssetStatusCodes.Disposed).Sum(s => s.Count);
        string? WarrantyNote() => anyWarranty ? null : "No warranties recorded yet: add them from an asset (Actions > Add warranty).";
        var kpis = new List<Kpi>
        {
            new("total", "Total assets", total, "primary", "bi-box-seam", "All assets except disposed ones.",
                newThisMonth > 0 ? $"+{newThisMonth} registered in the last 30 days" : null, drill),
            new("assigned", "Assigned", Status(AssetStatusCodes.Assigned), "success", "bi-person-check", "Status Assigned: in use by a person or department.", null,
                drill + StatusToken + AssetStatusCodes.Assigned),
            new("unassigned", "Unassigned", Status(AssetStatusCodes.InStock, "RESERVED"), "info", "bi-box", "Status In Stock or Reserved: available to assign.", null,
                drill + "&focus=" + AssetFocus.Unassigned),
            new("maintenance", "Under maintenance", Status(AssetStatusCodes.UnderMaintenance), "warning", "bi-tools", "Status Under Maintenance.", null,
                drill + "&focus=" + AssetFocus.Maintenance),
            new("repair", "Open repairs", repair, "warning", "bi-wrench-adjustable", "Assets with a corrective maintenance job scheduled or in progress.",
                null, drill + "&focus=" + AssetFocus.Repair),
            new("missingQr", "Missing QR code", missingQr, missingQr > 0 ? "danger" : "success", "bi-qr-code", "Active assets for which no QR label has been generated yet.", null,
                drill + "&focus=" + AssetFocus.MissingQr),
            new("warrantyExpired", "Warranty expired", expired, expired > 0 ? "danger" : "secondary", "bi-shield-x", "Active assets whose latest warranty has ended.",
                WarrantyNote(), drill + "&focus=" + AssetFocus.WarrantyExpired),
            new("warrantyExpiring", "Warranty expiring", expiring, expiring > 0 ? "warning" : "secondary", "bi-hourglass-split",
                $"Active assets whose latest warranty ends within {AssetFocus.ExpiringDays} days.", WarrantyNote(), drill + "&focus=" + AssetFocus.WarrantyExpiring),
            new("highRisk", "High-risk assets", highRisk, highRisk > 0 ? "danger" : "secondary", "bi-exclamation-triangle", "Risk level High or Critical.", null,
                drill + "&focus=" + AssetFocus.HighRisk),
            new("critical", "Critical assets", critical, "dark", "bi-lightning-charge", "Business criticality Critical (covered by the continuity plan).", null,
                drill + "&focus=" + AssetFocus.Critical),
            new("disposed", "Disposed", Status(AssetStatusCodes.Disposed), "secondary", "bi-recycle", "Status Disposed (kept for audit, excluded from all other cards).", null,
                drill + StatusToken + AssetStatusCodes.Disposed),
            new("lost", "Missing or stolen", Status("MISSING", "STOLEN"), Status("MISSING", "STOLEN") > 0 ? "danger" : "secondary", "bi-question-octagon",
                "Status Missing or Stolen.", null, drill + "&focus=" + AssetFocus.Lost)
        };
        // status ids for drill links
        var statusIds = (await _uow.Repository<AssetStatus>().QueryAsync(q => q.Select(s => new { s.StatusCode, s.AssetStatusId }), ct))
            .ToDictionary(s => s.StatusCode, s => s.AssetStatusId);
        kpis = kpis.Select(k => k with { DrillQuery = ResolveStatus(k.DrillQuery, statusIds) }).ToList();

        // ---------------------------------------------------------------- charts
        var charts = new List<ChartData>();
        var byCategory = await assets.QueryAsync(q => Active(q).GroupBy(a => new { a.AssetCategoryId, a.AssetCategory.CategoryName })
            .Select(g => new { g.Key.AssetCategoryId, g.Key.CategoryName, Count = g.Count() }).OrderByDescending(x => x.Count), ct);
        charts.Add(new ChartData("category", "Assets by category", "doughnut", byCategory.Select(x => x.CategoryName).ToList(),
            byCategory.Select(x => (decimal)x.Count).ToList(), Colors(byCategory.Count),
            DrillQueries: byCategory.Select(x => drill + "&categoryId=" + x.AssetCategoryId).ToList()));

        var byStatusChart = byStatus.OrderBy(s => s.SortOrder).ToList();
        charts.Add(new ChartData("status", "Asset status", "doughnut", byStatusChart.Select(s => s.StatusName).ToList(),
            byStatusChart.Select(s => (decimal)s.Count).ToList(), byStatusChart.Select(s => s.ColorHex ?? "#8D99AE").ToList(),
            DrillQueries: byStatusChart.Select(s => drill + (statusIds.TryGetValue(s.StatusCode, out var id) ? "&statusId=" + id : "") + "&includeDisposed=true").ToList()));

        var byDepartment = await assets.QueryAsync(q => Active(q).GroupBy(a => new { a.DepartmentId, Name = a.Department == null ? null : a.Department.DepartmentName })
            .Select(g => new { g.Key.DepartmentId, g.Key.Name, Count = g.Count() }).OrderByDescending(x => x.Count), ct);
        charts.Add(new ChartData("department", "Assets by department", "barH", byDepartment.Select(x => x.Name ?? "(no department)").ToList(),
            byDepartment.Select(x => (decimal)x.Count).ToList(),
            DrillQueries: byDepartment.Select(x => x.DepartmentId is null ? drill : drill + "&departmentId=" + x.DepartmentId).ToList()));

        var byBuilding = await assets.QueryAsync(q => Active(q).GroupBy(a => new { a.BuildingId, Name = a.Building == null ? null : a.Building.BuildingName })
            .Select(g => new { g.Key.BuildingId, g.Key.Name, Count = g.Count() }).OrderByDescending(x => x.Count), ct);
        charts.Add(new ChartData("building", "Assets by building", "bar", byBuilding.Select(x => x.Name ?? "(no building)").ToList(),
            byBuilding.Select(x => (decimal)x.Count).ToList(),
            DrillQueries: byBuilding.Select(x => x.BuildingId is null ? drill : drill + "&buildingId=" + x.BuildingId).ToList()));

        var byRisk = await assets.QueryAsync(q => Active(q).GroupBy(a => new
            {
                a.RiskLevelId, Name = a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
                Color = a.RiskLevel == null ? null : a.RiskLevel.ColorHex, Order = a.RiskLevel == null ? 999 : a.RiskLevel.SortOrder
            })
            .Select(g => new { g.Key.RiskLevelId, g.Key.Name, g.Key.Color, g.Key.Order, Count = g.Count() }), ct);
        var riskOrdered = byRisk.OrderByDescending(r => r.Order == 999 ? -1 : r.Order).ToList();
        charts.Add(new ChartData("risk", "Assets by risk level", "bar", riskOrdered.Select(r => r.Name ?? "Not assessed").ToList(),
            riskOrdered.Select(r => (decimal)r.Count).ToList(), riskOrdered.Select(r => r.Color ?? "#B0BEC5").ToList(),
            DrillQueries: riskOrdered.Select(r => r.RiskLevelId is null ? drill : drill + "&riskLevelId=" + r.RiskLevelId).ToList()));

        // growth: acquisitions per month (last 12 months) and inventory size at each month end
        var acquired = await assets.QueryAsync(q => Base(q).Select(a => new { a.PurchaseDate, a.CreatedDate, Disposed = a.AssetStatus.StatusCode == AssetStatusCodes.Disposed, a.DecommissionDate }), ct);
        var months = Enumerable.Range(0, 12).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i - 11)).ToList();
        DateOnly Acq(DateOnly? p, DateTime c) => p ?? DateOnly.FromDateTime(c);
        var perMonth = months.Select(m => (decimal)acquired.Count(x => { var d = Acq(x.PurchaseDate, x.CreatedDate); return d.Year == m.Year && d.Month == m.Month; })).ToList();
        var inventory = months.Select(m => (decimal)acquired.Count(x => Acq(x.PurchaseDate, x.CreatedDate) < m.AddMonths(1)
            && !(x.Disposed && x.DecommissionDate is { } dd && dd < m.AddMonths(1)))).ToList();
        charts.Add(new ChartData("growth", "Asset growth (last 12 months)", "line",
            months.Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)).ToList(), inventory,
            Note: "Line: assets in inventory at month end. Bars: assets acquired in the month (purchase date, or registration date).",
            SecondValues: perMonth, SecondLabel: "Acquired"));

        // warranty expiry timeline: next 12 months, by latest warranty end per active asset
        var ends = await assets.QueryAsync(q => Active(q).Where(a => a.Warranties.Any(w => !w.IsDeleted))
            .Select(a => a.Warranties.Where(w => !w.IsDeleted).Max(w => w.EndDate)), ct);
        var nextMonths = Enumerable.Range(0, 12).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i)).ToList();
        charts.Add(new ChartData("warranty", "Warranty expiry (next 12 months)", "bar",
            nextMonths.Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)).ToList(),
            nextMonths.Select(m => (decimal)ends.Count(e => e >= today && e.Year == m.Year && e.Month == m.Month)).ToList(),
            Note: anyWarranty ? null : "No warranties recorded yet."));

        // age distribution (purchase date)
        var purchase = await assets.QueryAsync(q => Active(q).Select(a => a.PurchaseDate), ct);
        var buckets = new[] { ("< 1 year", 0, 12), ("1-2 years", 12, 24), ("2-3 years", 24, 36), ("3-5 years", 36, 60), ("5+ years", 60, int.MaxValue) };
        int AgeMonths(DateOnly d) => (today.Year - d.Year) * 12 + today.Month - d.Month;
        var ageValues = buckets.Select(b => (decimal)purchase.Count(p => p is { } d && AgeMonths(d) >= b.Item2 && AgeMonths(d) < b.Item3)).ToList();
        var unknownAge = purchase.Count(p => p is null);
        charts.Add(new ChartData("age", "Asset age", "bar", buckets.Select(b => b.Item1).ToList(), ageValues,
            Note: unknownAge > 0 ? $"{unknownAge} asset(s) without a purchase date are not shown." : null));

        // values: only for users who may see financial data, only in the base currency (no silent currency mixing)
        string? currency = null;
        var excluded = 0;
        if (canViewFinancials)
        {
            var baseCurrencyId = await BaseCurrencyIdAsync(ct);
            currency = baseCurrencyId is null ? null
                : (await _uow.Repository<Currency>().QueryAsync(q => q.Where(c => c.CurrencyId == baseCurrencyId).Select(c => c.CurrencyCode), ct)).FirstOrDefault();
            var valued = await assets.QueryAsync(q => Active(q).Where(a => a.PurchaseCost != null)
                .Select(a => new { a.CurrencyId, Cost = a.PurchaseCost!.Value, Category = a.AssetCategory.CategoryName,
                    Department = a.Department == null ? null : a.Department.DepartmentName }), ct);
            var inBase = valued.Where(v => v.CurrencyId is null || v.CurrencyId == baseCurrencyId).ToList();
            excluded = valued.Count - inBase.Count;
            var byCatValue = inBase.GroupBy(v => v.Category).Select(g => (g.Key, Sum: g.Sum(x => x.Cost))).OrderByDescending(x => x.Sum).ToList();
            charts.Add(new ChartData("valueCategory", $"Purchase value by category{(currency is null ? "" : " (" + currency + ")")}", "barH",
                byCatValue.Select(x => x.Key).ToList(), byCatValue.Select(x => x.Sum).ToList(),
                Note: excluded > 0 ? $"{excluded} asset(s) bought in other currencies are not included." : null));
            var byDeptValue = inBase.GroupBy(v => v.Department ?? "(no department)").Select(g => (g.Key, Sum: g.Sum(x => x.Cost)))
                .OrderByDescending(x => x.Sum).Take(10).ToList();
            charts.Add(new ChartData("valueDepartment", $"Top departments by asset value{(currency is null ? "" : " (" + currency + ")")}", "barH",
                byDeptValue.Select(x => x.Key).ToList(), byDeptValue.Select(x => x.Sum).ToList()));
        }

        // ---------------------------------------------------------------- needs attention
        var attention = new List<AttentionItem>();
        async Task AddAsync(string focus, string reason, int severity)
        {
            var rows = await assets.QueryAsync(q => AssetQueries.ApplyFocus(Active(q), focus, today)
                .OrderBy(a => a.AssetNumber).Take(10).Select(a => new { a.AssetId, a.AssetNumber, a.AssetName }), ct);
            attention.AddRange(rows.Select(r => new AttentionItem(r.AssetId, r.AssetNumber, r.AssetName, reason, severity)));
        }
        await AddAsync(AssetFocus.Lost, "Missing or stolen", 3);
        var criticalExpired = await assets.QueryAsync(q => AssetQueries.ApplyFocus(AssetQueries.ApplyFocus(Active(q), AssetFocus.Critical, today), AssetFocus.WarrantyExpired, today)
            .OrderBy(a => a.AssetNumber).Take(10).Select(a => new { a.AssetId, a.AssetNumber, a.AssetName }), ct);
        attention.AddRange(criticalExpired.Select(r => new AttentionItem(r.AssetId, r.AssetNumber, r.AssetName, "Critical asset out of warranty", 3)));
        if (canViewSecurity)
        {
            var unencrypted = await assets.QueryAsync(q => Active(q).Where(a => a.StoresSensitiveData && !a.IsDiskEncrypted)
                .OrderBy(a => a.AssetNumber).Take(10).Select(a => new { a.AssetId, a.AssetNumber, a.AssetName }), ct);
            attention.AddRange(unencrypted.Select(r => new AttentionItem(r.AssetId, r.AssetNumber, r.AssetName, "Holds sensitive data but the disk is not encrypted", 2)));
        }
        await AddAsync(AssetFocus.VerificationOverdue, "Physical verification overdue", 1);
        var ordered = attention.GroupBy(a => (a.AssetId, a.Reason)).Select(g => g.First())
            .OrderByDescending(a => a.Severity).ThenBy(a => a.AssetNumber, StringComparer.Ordinal).Take(15).ToList();

        return new DashboardData(kpis, charts, ordered, _clock.UtcNow, anyWarranty, currency, excluded);
    }

    // ------------------------------------------------------------------ helpers

    private static IQueryable<Asset> ApplyFilter(IQueryable<Asset> q, DashboardFilter f)
    {
        if (f.DepartmentId is { } d) q = q.Where(a => a.DepartmentId == d);
        if (f.BuildingId is { } b) q = q.Where(a => a.BuildingId == b);
        if (f.CategoryId is { } c) q = q.Where(a => a.AssetCategoryId == c);
        if (f.StatusId is { } s) q = q.Where(a => a.AssetStatusId == s);
        if (f.From is { } from)
        {
            var fromDt = from.ToDateTime(TimeOnly.MinValue);
            q = q.Where(a => (a.PurchaseDate != null && a.PurchaseDate >= from) || (a.PurchaseDate == null && a.CreatedDate >= fromDt));
        }
        if (f.To is { } to)
        {
            var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(a => (a.PurchaseDate != null && a.PurchaseDate <= to) || (a.PurchaseDate == null && a.CreatedDate < toDt));
        }
        return q;
    }

    /// <summary>Asset-list query carrying the dashboard's own filters (date range is not an asset-list filter).</summary>
    private static string DrillBase(DashboardFilter f)
    {
        var parts = new List<string> { "?page=1" };
        if (f.DepartmentId is { } d) parts.Add("departmentId=" + d.ToString(CultureInfo.InvariantCulture));
        if (f.BuildingId is { } b) parts.Add("buildingId=" + b.ToString(CultureInfo.InvariantCulture));
        if (f.CategoryId is { } c) parts.Add("categoryId=" + c.ToString(CultureInfo.InvariantCulture));
        if (f.StatusId is { } s) parts.Add("statusId=" + s.ToString(CultureInfo.InvariantCulture));
        return string.Join("&", parts);
    }

    private const string StatusToken = "&status-code=";

    private static string ResolveStatus(string query, Dictionary<string, int> ids)
    {
        var i = query.IndexOf(StatusToken, StringComparison.Ordinal);
        if (i < 0) return query;
        var code = query[(i + StatusToken.Length)..];
        var rest = query[..i];
        return ids.TryGetValue(code, out var id)
            ? rest + "&statusId=" + id.ToString(CultureInfo.InvariantCulture) + (code == AssetStatusCodes.Disposed ? "&includeDisposed=true" : "")
            : rest;
    }

    private async Task<int?> BaseCurrencyIdAsync(CancellationToken ct)
    {
        if (_company.CompanyId is { } id)
        {
            var c = await _uow.Repository<Company>().QueryAsync(q => q.Where(x => x.CompanyId == id).Select(x => x.BaseCurrencyId), ct);
            if (c.Count == 1 && c[0] is { } cur) return cur;
        }
        var b = await _uow.Repository<Currency>().QueryAsync(q => q.Where(x => x.IsBaseCurrency).Select(x => (int?)x.CurrencyId), ct);
        return b.FirstOrDefault();
    }

    private static List<string> Colors(int n) => Enumerable.Range(0, n).Select(i => Palette[i % Palette.Length]).ToList();
}
