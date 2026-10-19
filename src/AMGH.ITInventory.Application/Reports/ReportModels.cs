namespace AMGH.ITInventory.Application.Reports;

public enum ReportColumnKind
{
    Text,
    Number,
    Money,
    Date,
    DateTime
}

/// <summary>Who may see a column. Columns the user may not see are removed before any output is produced.</summary>
public enum ColumnAccess
{
    Everyone,
    Financial,
    Security
}

public sealed record ReportColumn(string Key, string Header, ReportColumnKind Kind = ReportColumnKind.Text, bool DefaultVisible = true,
    ColumnAccess Access = ColumnAccess.Everyone, double Width = 16, bool Groupable = false);

public sealed record ReportDescriptor(
    string Code,
    string Title,
    string Description,
    string Icon,
    IReadOnlyList<ReportColumn> Columns,
    string DateFilterLabel,
    string DefaultSort,
    string Confidentiality,
    bool UsesAssetFilters = true,
    bool RequiresAuditPermission = false);

/// <summary>Everything a report run can vary. Built from the query string; validated by the service.</summary>
public sealed class ReportRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public int? BuildingId { get; set; }
    public int? StatusId { get; set; }
    public string? Search { get; set; }
    /// <summary>Visible column keys in order; empty = the report's default columns.</summary>
    public List<string> Columns { get; set; } = new();
    public string? Sort { get; set; }
    public bool Descending { get; set; }
    public string? GroupBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed record ReportAccess(bool CanExport, bool CanViewFinancials, bool CanViewSecurity, bool CanViewAudit)
{
    public bool Allows(ColumnAccess access) => access switch
    {
        ColumnAccess.Financial => CanViewFinancials,
        ColumnAccess.Security => CanViewSecurity,
        _ => true
    };
}

public sealed record ReportResult(
    ReportDescriptor Report,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    IReadOnlyList<KeyValuePair<string, string>> AppliedFilters,
    IReadOnlyList<string> Summary,
    ReportColumn? GroupBy,
    IReadOnlyList<ReportColumn> AvailableColumns)
{
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (TotalRows + PageSize - 1) / PageSize);
}

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public enum ReportFormat
{
    Excel,
    Csv,
    Word,
    Print
}

public sealed record ReportRunEntry(long ReportRunId, string ReportCode, string ReportTitle, string Format, int RowCount, string? Filters,
    string RunBy, DateTime RunDateUtc);

public sealed record SavedReportItem(int SavedReportId, string ReportCode, string Name, string QueryString, bool IsMine, string CreatedBy);
