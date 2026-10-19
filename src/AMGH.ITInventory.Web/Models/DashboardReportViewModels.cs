using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.Dashboard;
using AMGH.ITInventory.Application.Reports;

namespace AMGH.ITInventory.Web.Models;

public sealed class DashboardViewModel
{
    public required DashboardFilter Filter { get; init; }
    public required DashboardData Data { get; init; }
    public IReadOnlyList<LookupOption> Departments { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Buildings { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Categories { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Statuses { get; init; } = Array.Empty<LookupOption>();
}

public sealed class ReportCentreViewModel
{
    public IReadOnlyList<ReportDescriptor> Reports { get; init; } = Array.Empty<ReportDescriptor>();
    public IReadOnlyList<SavedReportItem> Saved { get; init; } = Array.Empty<SavedReportItem>();
    public IReadOnlyList<ReportRunEntry> Recent { get; init; } = Array.Empty<ReportRunEntry>();
}

public sealed class ReportRunViewModel
{
    public required ReportResult Result { get; init; }
    public required ReportRequest Request { get; init; }
    public string QueryString { get; init; } = string.Empty;
    public bool CanExport { get; init; }
    public IReadOnlyList<SavedReportItem> Saved { get; init; } = Array.Empty<SavedReportItem>();
    public IReadOnlyList<LookupOption> Categories { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Departments { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Locations { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Buildings { get; init; } = Array.Empty<LookupOption>();
    public IReadOnlyList<LookupOption> Statuses { get; init; } = Array.Empty<LookupOption>();
}

public sealed class ReportPrintViewModel
{
    public required ReportResult Result { get; init; }
    public required ReportContext Context { get; init; }
    public string CompanyCode { get; init; } = string.Empty;
    public bool HasLogo { get; init; }
    public string? BackUrl { get; init; }
}
