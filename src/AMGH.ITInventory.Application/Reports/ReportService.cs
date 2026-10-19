using System.Globalization;
using System.Text;
using AMGH.ITInventory.Application.Abstractions.Documents;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Reports;

/// <summary>Presentation details supplied by the web layer for exports (company branding, local time).</summary>
public sealed record ReportContext(string CompanyName, byte[]? LogoImage, string? LogoContentType, string GeneratedAtLocal, string UserName);

public interface IReportService
{
    const int MaxRows = 50_000;

    IReadOnlyList<ReportDescriptor> Available(ReportAccess access);
    ReportDescriptor? Find(string code);

    Task<Result<ReportResult>> RunAsync(string code, ReportRequest request, ReportAccess access, bool allRows = false, CancellationToken cancellationToken = default);
    Task<Result<ReportFile>> ExportAsync(string code, ReportRequest request, ReportAccess access, ReportFormat format, ReportContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Records a print / "Save as PDF" of a report in the generation history.</summary>
    Task LogAsync(string code, ReportFormat format, int rowCount, IReadOnlyList<KeyValuePair<string, string>> filters, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportRunEntry>> HistoryAsync(int maxRows = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedReportItem>> SavedAsync(string? code, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(string code, string name, ReportRequest request, bool shared, CancellationToken cancellationToken = default);
    Task<Result> DeleteSavedAsync(int savedReportId, CancellationToken cancellationToken = default);

    /// <summary>The request as a query string (page excluded) - used for saved configurations and links.</summary>
    string ToQueryString(ReportRequest request);
}

internal sealed class ReportService : IReportService
{
    private readonly Dictionary<string, IReportSource> _sources;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _user;
    private readonly ISpreadsheetWriter _excel;
    private readonly IWordReportWriter _word;

    public ReportService(IEnumerable<IReportSource> sources, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService user,
        ISpreadsheetWriter excel, IWordReportWriter word)
    {
        _sources = sources.ToDictionary(s => s.Descriptor.Code, StringComparer.OrdinalIgnoreCase);
        _uow = uow;
        _clock = clock;
        _user = user;
        _excel = excel;
        _word = word;
    }

    public IReadOnlyList<ReportDescriptor> Available(ReportAccess access) =>
        _sources.Values.Select(s => s.Descriptor).Where(d => !d.RequiresAuditPermission || access.CanViewAudit).ToList();

    public ReportDescriptor? Find(string code) => _sources.TryGetValue(code ?? string.Empty, out var s) ? s.Descriptor : null;

    // ------------------------------------------------------------------ run

    public async Task<Result<ReportResult>> RunAsync(string code, ReportRequest request, ReportAccess access, bool allRows = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(access);
        if (!_sources.TryGetValue(code ?? string.Empty, out var source)) return Result<ReportResult>.Failure(Error.NotFound("Report not found."));
        var d = source.Descriptor;
        if (d.RequiresAuditPermission && !access.CanViewAudit)
            return Result<ReportResult>.Failure(Error.Forbidden("This report needs the 'View audit logs' permission."));
        if (request.From is { } f && request.To is { } t && f > t)
            return Result<ReportResult>.Failure(Error.Validation("The 'from' date is after the 'to' date.", "From"));

        var rows = await source.LoadAsync(_uow, request, access, _clock.UtcToday, IReportService.MaxRows, cancellationToken);
        var truncated = rows.Count > IReportService.MaxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        // columns the user may see; chosen columns in the user's order, or the report's defaults
        var allowed = d.Columns.Select((c, i) => (c, i)).Where(x => access.Allows(x.c.Access)).ToList();
        var chosen = request.Columns.Count > 0
            ? request.Columns.Select(k => allowed.FirstOrDefault(a => a.c.Key == k)).Where(a => a.c is not null).Distinct().ToList()
            : allowed.Where(a => a.c.DefaultVisible).ToList();
        if (chosen.Count == 0) chosen = allowed.Where(a => a.c.DefaultVisible).ToList();

        // search across every column the user may see (not only the visible ones)
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            rows = rows.Where(r => allowed.Any(a => Format(r[a.i], a.c.Kind)?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)).ToList();
        }

        // sort, then group (group column first, stable)
        var sortCol = allowed.FirstOrDefault(a => a.c.Key == request.Sort);
        if (sortCol.c is null) sortCol = allowed.FirstOrDefault(a => a.c.Key == d.DefaultSort);
        IEnumerable<object?[]> ordered = rows;
        if (sortCol.c is not null)
        {
            var idx = sortCol.i;
            var desc = request.Sort is null ? DefaultDescending(sortCol.c) : request.Descending;
            ordered = desc ? rows.OrderByDescending(r => r[idx], ValueComparer.Instance) : rows.OrderBy(r => r[idx], ValueComparer.Instance);
        }
        var group = allowed.FirstOrDefault(a => a.c.Key == request.GroupBy && a.c.Groupable);
        if (group.c is not null)
        {
            var gi = group.i;
            ordered = ordered.OrderBy(r => r[gi], ValueComparer.Instance); // OrderBy is stable: keeps the inner sort
            if (!chosen.Any(c => c.i == gi)) chosen.Insert(0, group);
        }
        var all = ordered.ToList();
        var summary = source.Summarize(all).ToList();
        if (truncated) summary.Insert(0, $"Only the first {IReportService.MaxRows:N0} rows are included. Narrow the filters to see everything.");

        var pageSize = allRows ? Math.Max(1, all.Count) : Math.Clamp(request.PageSize, 25, 500);
        var page = allRows ? 1 : Math.Max(1, request.Page);
        var pageRows = all.Skip((page - 1) * pageSize).Take(pageSize).Select(r => chosen.Select(c => r[c.i]).ToArray()).ToList();

        return Result<ReportResult>.Success(new ReportResult(d, chosen.Select(c => c.c).ToList(), pageRows, all.Count, page, pageSize,
            await DescribeFiltersAsync(d, request, cancellationToken), summary, group.c, allowed.Select(a => a.c).ToList()));
    }

    private static bool DefaultDescending(ReportColumn c) => c.Key is "time" or "assigned" or "issueCount";

    // ------------------------------------------------------------------ export

    public async Task<Result<ReportFile>> ExportAsync(string code, ReportRequest request, ReportAccess access, ReportFormat format,
        ReportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!access.CanExport) return Result<ReportFile>.Failure(Error.Forbidden("Exporting reports needs the 'Export reports' permission."));
        var run = await RunAsync(code, request, access, allRows: true, cancellationToken);
        if (run.IsFailure) return Result<ReportFile>.Failure(run.Errors.ToArray());
        var r = run.Value;
        var stamp = _clock.UtcNow.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        var baseName = $"{r.Report.Code}_{stamp}";

        ReportFile file = format switch
        {
            ReportFormat.Excel => new ReportFile(Excel(r, context), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", baseName + ".xlsx"),
            ReportFormat.Csv => new ReportFile(Csv(r), "text/csv; charset=utf-8", baseName + ".csv"),
            ReportFormat.Word => new ReportFile(Word(r, context), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", baseName + ".docx"),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        await LogAsync(r.Report.Code, format, r.TotalRows, r.AppliedFilters, cancellationToken);
        return Result<ReportFile>.Success(file);
    }

    private byte[] Excel(ReportResult r, ReportContext ctx)
    {
        var columns = r.Columns.Select(c => new SheetColumn(c.Header, c.Kind switch
        {
            ReportColumnKind.Date or ReportColumnKind.DateTime => SheetColumnKind.Date,
            ReportColumnKind.Money => SheetColumnKind.Number,
            ReportColumnKind.Number => SheetColumnKind.Number,
            _ => SheetColumnKind.Text
        }, c.Width)).ToList();
        var data = r.Rows.Select(row => (IReadOnlyList<object?>)row.Select((v, i) => ExcelValue(v, r.Columns[i].Kind)).ToList()).ToList();
        var about = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Report", r.Report.Title }, new object?[] { "System", Shared.AppInfo.Name }, new object?[] { "Company", ctx.CompanyName },
            new object?[] { "Generated", ctx.GeneratedAtLocal }, new object?[] { "Generated by", ctx.UserName },
            new object?[] { "Classification", r.Report.Confidentiality }, new object?[] { "Rows", r.TotalRows }
        };
        about.AddRange(r.AppliedFilters.Select(f => (IReadOnlyList<object?>)new object?[] { "Filter: " + f.Key, f.Value }));
        about.AddRange(r.Summary.Select(s => (IReadOnlyList<object?>)new object?[] { "Summary", s }));
        return _excel.Write(new[]
        {
            new SheetData(SheetName(r.Report.Title), columns, data),
            new SheetData("About this report", new[] { new SheetColumn("Item", SheetColumnKind.Text, 24), new SheetColumn("Value", SheetColumnKind.Text, 70) }, about)
        });
    }

    private static object? ExcelValue(object? v, ReportColumnKind kind) => v switch
    {
        DateTime dt => DateOnly.FromDateTime(dt),
        int i when kind is ReportColumnKind.Number or ReportColumnKind.Money => (decimal)i,
        _ => v
    };

    private static string SheetName(string title) => title.Length <= 31 ? title : title[..31];

    /// <summary>UTF-8 with BOM (opens correctly in Excel, Arabic included). Cells that could run as formulas are neutralised.</summary>
    internal static byte[] Csv(ReportResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", r.Columns.Select(c => CsvCell(c.Header))));
        foreach (var row in r.Rows)
            sb.AppendLine(string.Join(",", row.Select((v, i) => CsvCell(CsvValue(v, r.Columns[i].Kind)))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string? CsvValue(object? v, ReportColumnKind kind) => v switch
    {
        null => null,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString(kind == ReportColumnKind.Money ? "0.00" : "0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };

    private static readonly System.Buffers.SearchValues<char> CsvSpecial = System.Buffers.SearchValues.Create(",\"\n\r");

    private static string CsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // CSV injection guard (OWASP): a leading = + - @ tab or CR makes spreadsheets evaluate the cell.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.AsSpan().IndexOfAny(CsvSpecial) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private byte[] Word(ReportResult r, ReportContext ctx)
    {
        var groupIndex = r.GroupBy is null ? (int?)null : r.Columns.ToList().FindIndex(c => c.Key == r.GroupBy.Key);
        return _word.Write(new WordReportDocument(r.Report.Title, ctx.CompanyName, ctx.LogoImage, ctx.LogoContentType, r.Report.Confidentiality,
            ctx.UserName, ctx.GeneratedAtLocal, r.AppliedFilters, r.Summary, r.Columns.Select(c => c.Header).ToList(),
            r.Rows.Select(row => (IReadOnlyList<string>)row.Select((v, i) => Format(v, r.Columns[i].Kind) ?? string.Empty).ToList()).ToList(),
            r.Columns.Select(c => c.Kind is ReportColumnKind.Number or ReportColumnKind.Money).ToList(), groupIndex is >= 0 ? groupIndex : null,
            r.Columns.Select(c => c.Width).ToList()));
    }

    public static string? Format(object? v, ReportColumnKind kind) => ReportValue.Format(v, kind);


    // ------------------------------------------------------------------ history and saved configurations

    public async Task LogAsync(string code, ReportFormat format, int rowCount, IReadOnlyList<KeyValuePair<string, string>> filters,
        CancellationToken cancellationToken = default)
    {
        var text = string.Join("; ", filters.Select(f => f.Key + ": " + f.Value));
        await _uow.Repository<ReportRun>().AddAsync(new ReportRun
        {
            ReportCode = code, Format = format.ToString(), RowCount = rowCount,
            Filters = text.Length == 0 ? null : text.Length > 1000 ? text[..1000] : text,
            RunBy = _user.UserName, RunDate = _clock.UtcNow
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReportRunEntry>> HistoryAsync(int maxRows = 200, CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<ReportRun>().QueryAsync(q => q.OrderByDescending(r => r.ReportRunId).Take(Math.Clamp(maxRows, 1, 1000)), cancellationToken);
        return rows.Select(r => new ReportRunEntry(r.ReportRunId, r.ReportCode, Find(r.ReportCode)?.Title ?? r.ReportCode, r.Format, r.RowCount,
            r.Filters, r.RunBy, r.RunDate)).ToList();
    }

    public async Task<IReadOnlyList<SavedReportItem>> SavedAsync(string? code, CancellationToken cancellationToken = default)
    {
        var me = _user.UserId ?? -1;
        var rows = await _uow.Repository<SavedReport>().QueryAsync(q =>
        {
            q = q.Where(s => s.OwnerUserId == me || s.IsShared);
            if (code is not null) q = q.Where(s => s.ReportCode == code);
            return q.OrderBy(s => s.Name);
        }, cancellationToken);
        return rows.Select(s => new SavedReportItem(s.SavedReportId, s.ReportCode, s.Name, s.QueryString, s.OwnerUserId == me, s.CreatedBy)).ToList();
    }

    public async Task<Result<int>> SaveAsync(string code, string name, ReportRequest request, bool shared, CancellationToken cancellationToken = default)
    {
        if (Find(code) is null) return Result<int>.Failure(Error.NotFound("Report not found."));
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 100) return Result<int>.Failure(Error.Validation("Give the saved report a name (2-100 characters).", "SaveName"));
        if (_user.UserId is not { } me) return Result<int>.Failure(Error.Forbidden("Sign in to save reports."));
        var saved = new SavedReport { ReportCode = code, Name = name, QueryString = ToQueryString(request), IsShared = shared, OwnerUserId = me };
        await _uow.Repository<SavedReport>().AddAsync(saved, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(saved.SavedReportId);
    }

    public async Task<Result> DeleteSavedAsync(int savedReportId, CancellationToken cancellationToken = default)
    {
        var me = _user.UserId ?? -1;
        var saved = await _uow.Repository<SavedReport>().FirstOrDefaultAsync(s => s.SavedReportId == savedReportId && s.OwnerUserId == me, cancellationToken);
        if (saved is null) return Result.Failure(Error.NotFound("Saved report not found (only its owner can delete it)."));
        _uow.Repository<SavedReport>().Remove(saved);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public string ToQueryString(ReportRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var parts = new List<string>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) parts.Add(key + "=" + Uri.EscapeDataString(value)); }
        string? I(int? v) => v?.ToString(CultureInfo.InvariantCulture);
        Add("from", r.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("to", r.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("categoryId", I(r.CategoryId)); Add("departmentId", I(r.DepartmentId)); Add("locationId", I(r.LocationId));
        Add("buildingId", I(r.BuildingId)); Add("statusId", I(r.StatusId)); Add("search", r.Search);
        foreach (var c in r.Columns) Add("columns", c);
        Add("sort", r.Sort); if (r.Descending) Add("descending", "true"); Add("groupBy", r.GroupBy);
        if (r.PageSize != 100) Add("pageSize", I(r.PageSize));
        var qs = string.Join("&", parts);
        return qs.Length > 2000 ? qs[..2000] : qs;
    }

    private async Task<IReadOnlyList<KeyValuePair<string, string>>> DescribeFiltersAsync(ReportDescriptor d, ReportRequest r, CancellationToken ct)
    {
        var list = new List<KeyValuePair<string, string>>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) list.Add(new(k, v)); }
        if (r.From is not null || r.To is not null)
            Add(d.DateFilterLabel, $"{r.From?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "…"} to {r.To?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "…"}");
        else if (d.Code == "audit") Add(d.DateFilterLabel, "last 30 days");
        if (d.UsesAssetFilters)
        {
            if (r.CategoryId is { } c) Add("Category", (await _uow.Repository<AssetCategory>().QueryAsync(q => q.Where(x => x.AssetCategoryId == c).Select(x => x.CategoryName), ct)).FirstOrDefault());
            if (r.DepartmentId is { } dep) Add("Department", (await _uow.Repository<Department>().QueryAsync(q => q.Where(x => x.DepartmentId == dep).Select(x => x.DepartmentName), ct)).FirstOrDefault());
            if (r.LocationId is { } l) Add("Location", (await _uow.Repository<Location>().QueryAsync(q => q.Where(x => x.LocationId == l).Select(x => x.LocationName), ct)).FirstOrDefault());
            if (r.BuildingId is { } b) Add("Building", (await _uow.Repository<Building>().QueryAsync(q => q.Where(x => x.BuildingId == b).Select(x => x.BuildingName), ct)).FirstOrDefault());
            if (r.StatusId is { } s) Add("Status", (await _uow.Repository<AssetStatus>().QueryAsync(q => q.Where(x => x.AssetStatusId == s).Select(x => x.StatusName), ct)).FirstOrDefault());
            else Add("Status", "all except disposed");
        }
        Add("Search", r.Search?.Trim());
        return list;
    }

    /// <summary>Orders mixed values: nulls last, numbers and dates by value, text case-insensitively.</summary>
    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();
        public int Compare(object? x, object? y)
        {
            if (x is null) return y is null ? 0 : 1;
            if (y is null) return -1;
            if (x is string sx && y is string sy) return string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);
            if (x is IComparable cx && x.GetType() == y.GetType()) return cx.CompareTo(y);
            if (x is IConvertible && y is IConvertible && IsNumber(x) && IsNumber(y))
                return Convert.ToDecimal(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(y, CultureInfo.InvariantCulture));
            return string.Compare(Convert.ToString(x, CultureInfo.InvariantCulture), Convert.ToString(y, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsNumber(object o) => o is int or long or decimal or double or short or byte;
    }
}

/// <summary>Display format shared by the preview, print, Word and search.</summary>
public static class ReportValue
{
    public static string? Format(object? v, ReportColumnKind kind) => v switch
    {
        null => null,
        DateOnly d => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString(kind == ReportColumnKind.Money ? "N2" : "#,0.##", CultureInfo.InvariantCulture),
        int i => i.ToString(kind == ReportColumnKind.Money ? "N2" : "#,0", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };
}
