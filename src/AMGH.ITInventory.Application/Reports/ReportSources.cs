using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using C = AMGH.ITInventory.Application.Reports.ReportColumn;
using K = AMGH.ITInventory.Application.Reports.ReportColumnKind;

namespace AMGH.ITInventory.Application.Reports;

/// <summary>A report: its definition and how to load its rows (one value per descriptor column, in order).</summary>
public interface IReportSource
{
    ReportDescriptor Descriptor { get; }
    Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest request, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct);
    IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => Array.Empty<string>();
}

internal static class ReportFilters
{
    /// <summary>The shared asset filters. Disposed assets are excluded unless a status is chosen.</summary>
    public static IQueryable<Asset> Assets(IQueryable<Asset> q, ReportRequest r)
    {
        if (r.CategoryId is { } c) q = q.Where(a => a.AssetCategoryId == c);
        if (r.DepartmentId is { } d) q = q.Where(a => a.DepartmentId == d);
        if (r.LocationId is { } l) q = q.Where(a => a.LocationId == l);
        if (r.BuildingId is { } b) q = q.Where(a => a.BuildingId == b);
        q = r.StatusId is { } s ? q.Where(a => a.AssetStatusId == s) : q.Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed);
        return q;
    }

    public static string? Place(string? location, string? building, string? floor, string? room)
    {
        var parts = new[] { location, building, floor, room }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return parts.Length == 0 ? null : string.Join(" / ", parts);
    }

    public static string YesNo(bool b) => b ? "Yes" : "No";
    public static int Pct(int part, int whole) => whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
}

// ======================================================================== 1. Asset register

internal sealed class AssetRegisterReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("asset-register", "Asset Register",
        "Every asset with its classification, location, custodian, purchase and warranty details.", "bi-journal-text",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 28), new("category", "Category", Groupable: true),
            new("subcategory", "Sub-category", DefaultVisible: false, Groupable: true), new("manufacturer", "Manufacturer", Groupable: true),
            new("model", "Model", DefaultVisible: false), new("serial", "Serial number"), new("status", "Status", Groupable: true),
            new("department", "Department", Groupable: true), new("location", "Location", Width: 28, Groupable: true),
            new("assignedTo", "Assigned to", Width: 22), new("employeeNo", "Employee no.", DefaultVisible: false),
            new("purchaseDate", "Purchase date", K.Date), new("cost", "Purchase value", K.Money, Access: ColumnAccess.Financial),
            new("currency", "Currency", DefaultVisible: false, Access: ColumnAccess.Financial), new("warrantyEnd", "Warranty end", K.Date),
            new("classification", "Classification", DefaultVisible: false, Groupable: true), new("criticality", "Criticality", DefaultVisible: false, Groupable: true),
            new("risk", "Risk level", Groupable: true), new("hostname", "Hostname", DefaultVisible: false),
            new("nextVerification", "Next verification", K.Date, DefaultVisible: false)
        }, "Purchase date", "number", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var rows = await uow.Repository<Asset>().QueryAsync(q =>
        {
            q = ReportFilters.Assets(q, r);
            if (r.From is { } f) q = q.Where(a => a.PurchaseDate >= f);
            if (r.To is { } t) q = q.Where(a => a.PurchaseDate <= t);
            return q.OrderBy(a => a.AssetNumber).Take(maxRows + 1).Select(a => new
            {
                a.AssetNumber, a.AssetName, Category = a.AssetCategory.CategoryName,
                Sub = a.AssetSubCategory == null ? null : a.AssetSubCategory.SubCategoryName,
                Man = a.Manufacturer == null ? null : a.Manufacturer.ManufacturerName, a.ModelName, a.SerialNumber,
                Status = a.AssetStatus.StatusName, Dept = a.Department == null ? null : a.Department.DepartmentName,
                Loc = a.Location == null ? null : a.Location.LocationName, Bld = a.Building == null ? null : a.Building.BuildingName,
                Flr = a.Floor == null ? null : a.Floor.FloorName, Room = a.Room == null ? null : a.Room.RoomName,
                Emp = a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName,
                EmpNo = a.AssignedEmployee == null ? null : a.AssignedEmployee.EmployeeNumber,
                a.PurchaseDate, a.PurchaseCost, Cur = a.Currency == null ? null : a.Currency.CurrencyCode,
                Warranty = a.Warranties.Where(w => !w.IsDeleted).Max(w => (DateOnly?)w.EndDate),
                Cls = a.Classification == null ? null : a.Classification.ClassificationName,
                Crit = a.Criticality == null ? null : a.Criticality.CriticalityName, Risk = a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
                a.Hostname, a.NextAuditDueDate
            });
        }, ct);
        return rows.Select(x => new object?[]
        {
            x.AssetNumber, x.AssetName, x.Category, x.Sub, x.Man, x.ModelName, x.SerialNumber, x.Status, x.Dept,
            ReportFilters.Place(x.Loc, x.Bld, x.Flr, x.Room), x.Emp, x.EmpNo, x.PurchaseDate, x.PurchaseCost, x.Cur, x.Warranty,
            x.Cls, x.Crit, x.Risk, x.Hostname, x.NextAuditDueDate
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} asset(s)",
        $"{rows.Count(r => r[10] is not null)} assigned to a person",
        $"{rows.Count(r => r[9] is null)} without a location"
    };
}

// ======================================================================== 2. Assignments

internal sealed class AssignmentReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("assignments", "Assignment Report",
        "Who has which asset: issue and return dates, overdue loans and hand-over acknowledgements.", "bi-person-check",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 26), new("category", "Category", DefaultVisible: false, Groupable: true),
            new("employee", "Employee", Width: 22, Groupable: true), new("employeeNo", "Employee no."), new("department", "Department", Groupable: true),
            new("type", "Type", Groupable: true), new("assigned", "Assigned", K.Date), new("expectedReturn", "Expected return", K.Date),
            new("returned", "Returned", K.Date), new("state", "Status", Groupable: true), new("acknowledged", "Acknowledged", Groupable: true),
            new("assignedBy", "Assigned by", DefaultVisible: false), new("docNo", "Document no.", Width: 18)
        }, "Assignment date", "assigned", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var from = r.From?.ToDateTime(TimeOnly.MinValue);
        var to = r.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await uow.Repository<AssetAssignment>().QueryAsync(q =>
        {
            if (r.CategoryId is { } c) q = q.Where(x => x.Asset.AssetCategoryId == c);
            if (r.DepartmentId is { } d) q = q.Where(x => x.DepartmentId == d);
            if (r.LocationId is { } l) q = q.Where(x => x.Asset.LocationId == l);
            if (r.BuildingId is { } b) q = q.Where(x => x.Asset.BuildingId == b);
            if (r.StatusId is { } s) q = q.Where(x => x.Asset.AssetStatusId == s);
            if (from is { } f) q = q.Where(x => x.AssignedDate >= f);
            if (to is { } t) q = q.Where(x => x.AssignedDate < t);
            return q.OrderByDescending(x => x.AssignedDate).Take(maxRows + 1).Select(x => new
            {
                DocNo = x.DocumentNumber,
                x.Asset.AssetNumber, x.Asset.AssetName, Category = x.Asset.AssetCategory.CategoryName,
                Emp = x.Employee == null ? null : x.Employee.FirstName + " " + x.Employee.LastName,
                EmpNo = x.Employee == null ? null : x.Employee.EmployeeNumber, Dept = x.Department == null ? null : x.Department.DepartmentName,
                x.AssignmentType, x.AssignedDate, x.ExpectedReturnDate, x.ReturnedDate, x.IsCurrent, x.AcknowledgementReceived, x.AssignedBy
            });
        }, ct);
        return rows.Select(x => new object?[]
        {
            x.AssetNumber, x.AssetName, x.Category, x.Emp ?? "(department)", x.EmpNo, x.Dept, Common.DisplayText.Humanize(x.AssignmentType.ToString()),
            DateOnly.FromDateTime(x.AssignedDate), x.ExpectedReturnDate, x.ReturnedDate is { } rd ? DateOnly.FromDateTime(rd) : null,
            !x.IsCurrent ? "Returned" : x.ExpectedReturnDate is { } e && e < today ? "Overdue" : "Current",
            ReportFilters.YesNo(x.AcknowledgementReceived), x.AssignedBy, x.DocNo
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows)
    {
        var current = rows.Count(r => (string?)r[10] is "Current" or "Overdue");
        var unack = rows.Count(r => (string?)r[10] is "Current" or "Overdue" && (string?)r[11] == "No");
        return new[]
        {
            $"{rows.Count} assignment(s), {current} current",
            $"{rows.Count(r => (string?)r[10] == "Overdue")} overdue for return",
            $"{unack} current assignment(s) without signed acknowledgement ({ReportFilters.Pct(unack, current)}%)"
        };
    }
}

// ======================================================================== 3. Warranty

internal sealed class WarrantyReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("warranty", "Warranty Report",
        "Warranty and support contracts per asset, with status and days remaining.", "bi-shield-check",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 26), new("category", "Category", Groupable: true),
            new("provider", "Provider", Groupable: true), new("contract", "Contract no."), new("type", "Type", Groupable: true),
            new("support", "Coverage", DefaultVisible: false), new("start", "Start", K.Date), new("end", "End", K.Date),
            new("state", "Status", Groupable: true), new("daysLeft", "Days remaining", K.Number),
            new("cost", "Cost", K.Money, DefaultVisible: false, Access: ColumnAccess.Financial), new("docNo", "Document no.", Width: 18)
        }, "Warranty end date", "end", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var rows = await uow.Repository<Warranty>().QueryAsync(q =>
        {
            q = q.Where(w => !w.Asset.IsDeleted);
            if (r.CategoryId is { } c) q = q.Where(w => w.Asset.AssetCategoryId == c);
            if (r.DepartmentId is { } d) q = q.Where(w => w.Asset.DepartmentId == d);
            if (r.LocationId is { } l) q = q.Where(w => w.Asset.LocationId == l);
            if (r.BuildingId is { } b) q = q.Where(w => w.Asset.BuildingId == b);
            q = r.StatusId is { } s ? q.Where(w => w.Asset.AssetStatusId == s) : q.Where(w => w.Asset.AssetStatus.StatusCode != AssetStatusCodes.Disposed);
            if (r.From is { } f) q = q.Where(w => w.EndDate >= f);
            if (r.To is { } t) q = q.Where(w => w.EndDate <= t);
            return q.OrderBy(w => w.EndDate).Take(maxRows + 1).Select(w => new
            {
                DocNo = w.DocumentNumber,
                w.Asset.AssetNumber, w.Asset.AssetName, Category = w.Asset.AssetCategory.CategoryName,
                Provider = w.ProviderVendor == null ? null : w.ProviderVendor.VendorName, w.ContractNumber, w.WarrantyType, w.SupportLevel,
                w.StartDate, w.EndDate, w.Cost
            });
        }, ct);
        return rows.Select(x =>
        {
            var days = x.EndDate.DayNumber - today.DayNumber;
            var state = days < 0 ? "Expired" : days <= AssetFocus.ExpiringDays ? "Expiring" : "Active";
            return new object?[] { x.AssetNumber, x.AssetName, x.Category, x.Provider, x.ContractNumber, Common.DisplayText.Humanize(x.WarrantyType.ToString()),
                x.SupportLevel, x.StartDate, x.EndDate, state, days < 0 ? 0 : days, x.Cost, x.DocNo };
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => rows.Count == 0
        ? new[] { "No warranties recorded yet: add them from an asset (Actions > Add warranty)." }
        : new[]
        {
            $"{rows.Count} warranty record(s)",
            $"{rows.Count(r => (string?)r[9] == "Expired")} expired, {rows.Count(r => (string?)r[9] == "Expiring")} expiring within {AssetFocus.ExpiringDays} days"
        };
}

// ======================================================================== 4. Risk assessment

internal sealed class RiskReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("risk", "Risk Assessment Report",
        "Business criticality, risk level and security controls per asset, with security review due dates.", "bi-exclamation-triangle",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 26), new("category", "Category", DefaultVisible: false, Groupable: true),
            new("criticality", "Criticality", Groupable: true), new("rto", "RTO (h)", K.Number, DefaultVisible: false),
            new("rpo", "RPO (h)", K.Number, DefaultVisible: false), new("risk", "Risk level", Groupable: true),
            new("classification", "Classification", Groupable: true), new("sensitive", "Sensitive data", Groupable: true),
            new("encrypted", "Disk encrypted", Access: ColumnAccess.Security, Groupable: true),
            new("edr", "Endpoint protection", Access: ColumnAccess.Security, Groupable: true),
            new("mdm", "MDM managed", DefaultVisible: false, Access: ColumnAccess.Security),
            new("owner", "Owner", Width: 22, Groupable: true), new("lastReview", "Last security review", K.Date),
            new("nextReview", "Review due", K.Date), new("reviewState", "Review status", Groupable: true)
        }, "Last security review", "number", "Confidential");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var rows = await uow.Repository<Asset>().QueryAsync(q =>
        {
            q = ReportFilters.Assets(q, r);
            if (r.From is { } f) q = q.Where(a => a.LastSecurityReviewDate >= f);
            if (r.To is { } t) q = q.Where(a => a.LastSecurityReviewDate <= t);
            return q.OrderBy(a => a.AssetNumber).Take(maxRows + 1).Select(a => new
            {
                a.AssetNumber, a.AssetName, Category = a.AssetCategory.CategoryName,
                Crit = a.Criticality == null ? null : a.Criticality.CriticalityName,
                Rto = a.Criticality == null ? null : a.Criticality.RtoHours, Rpo = a.Criticality == null ? null : a.Criticality.RpoHours,
                Risk = a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
                Months = a.RiskLevel == null ? null : a.RiskLevel.ReviewFrequencyMonths,
                Cls = a.Classification == null ? null : a.Classification.ClassificationName,
                a.StoresSensitiveData, a.IsDiskEncrypted, a.EndpointProtectionInstalled, a.IsMdmManaged,
                Owner = a.AssetOwner == null ? null : a.AssetOwner.FirstName + " " + a.AssetOwner.LastName, a.LastSecurityReviewDate
            });
        }, ct);
        return rows.Select(x =>
        {
            DateOnly? due = x.LastSecurityReviewDate is { } last && x.Months is { } m ? last.AddMonths(m) : null;
            var state = x.Risk is null ? "Not assessed" : x.LastSecurityReviewDate is null ? "Never reviewed" : due < today ? "Overdue" : "Up to date";
            return new object?[] { x.AssetNumber, x.AssetName, x.Category, x.Crit, x.Rto, x.Rpo, x.Risk, x.Cls, ReportFilters.YesNo(x.StoresSensitiveData),
                ReportFilters.YesNo(x.IsDiskEncrypted), ReportFilters.YesNo(x.EndpointProtectionInstalled), ReportFilters.YesNo(x.IsMdmManaged),
                x.Owner, x.LastSecurityReviewDate, due, state };
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} asset(s); {rows.Count(r => r[6] is null)} without a risk assessment",
        $"{rows.Count(r => (string?)r[15] == "Overdue")} security review(s) overdue, {rows.Count(r => (string?)r[15] == "Never reviewed")} never reviewed",
        "Risk level and criticality are set on each asset (Security tab)."
    };
}

// ======================================================================== 5. Compliance

internal sealed class ComplianceReport : IReportSource
{
    public static readonly string[] Checks =
    {
        "No QR code", "No location", "No department", "No serial number", "Not classified", "Warranty expired",
        "Verification overdue", "Missing or stolen", "Sensitive data on unencrypted disk"
    };

    public ReportDescriptor Descriptor { get; } = new("compliance", "Compliance Report",
        "Assets with incomplete or non-compliant records, and what is missing.", "bi-clipboard-check",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 26), new("category", "Category", Groupable: true),
            new("status", "Status", Groupable: true), new("department", "Department", Groupable: true),
            new("issues", "Issues", Width: 60), new("issueCount", "No. of issues", K.Number)
        }, "Registration date", "issueCount", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var from = r.From?.ToDateTime(TimeOnly.MinValue);
        var to = r.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await uow.Repository<Asset>().QueryAsync(q =>
        {
            q = ReportFilters.Assets(q, r);
            if (from is { } f) q = q.Where(a => a.CreatedDate >= f);
            if (to is { } t) q = q.Where(a => a.CreatedDate < t);
            return q.OrderBy(a => a.AssetNumber).Select(a => new
            {
                a.AssetNumber, a.AssetName, Category = a.AssetCategory.CategoryName, Status = a.AssetStatus.StatusName, StatusCode = a.AssetStatus.StatusCode,
                Dept = a.Department == null ? null : a.Department.DepartmentName,
                HasQr = a.QrCodes.Any(x => x.IsCurrent), a.LocationId, a.DepartmentId, a.SerialNumber, a.ClassificationId,
                Warranty = a.Warranties.Where(w => !w.IsDeleted).Max(w => (DateOnly?)w.EndDate), a.NextAuditDueDate,
                a.StoresSensitiveData, a.IsDiskEncrypted
            });
        }, ct);
        var result = new List<object?[]>();
        foreach (var x in rows)
        {
            var issues = new List<string>();
            if (!x.HasQr) issues.Add(Checks[0]);
            if (x.LocationId is null) issues.Add(Checks[1]);
            if (x.DepartmentId is null) issues.Add(Checks[2]);
            if (string.IsNullOrWhiteSpace(x.SerialNumber)) issues.Add(Checks[3]);
            if (x.ClassificationId is null) issues.Add(Checks[4]);
            if (x.Warranty is { } w && w < today) issues.Add(Checks[5]);
            if (x.NextAuditDueDate is { } n && n < today) issues.Add(Checks[6]);
            if (x.StatusCode is "MISSING" or "STOLEN") issues.Add(Checks[7]);
            if (access.CanViewSecurity && x.StoresSensitiveData && !x.IsDiskEncrypted) issues.Add(Checks[8]);
            if (issues.Count == 0) continue;
            result.Add(new object?[] { x.AssetNumber, x.AssetName, x.Category, x.Status, x.Dept, string.Join("; ", issues), issues.Count });
            if (result.Count > maxRows) break;
        }
        return result;
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows)
    {
        var lines = new List<string> { $"{rows.Count} asset(s) with at least one issue" };
        foreach (var check in Checks)
        {
            var n = rows.Count(r => ((string)r[5]!).Split("; ").Contains(check));
            if (n > 0) lines.Add($"{check}: {n}");
        }
        return lines;
    }
}

// ======================================================================== 6. Audit

internal sealed class AuditReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("audit", "Audit Report",
        "Every recorded change: who did what, when, to which record, with old and new values.", "bi-clock-history",
        new C[]
        {
            new("time", "Date and time (UTC)", K.DateTime, Width: 20), new("user", "User", Groupable: true), new("action", "Action", Groupable: true),
            new("entity", "Record type", Groupable: true), new("key", "Record"), new("changed", "Changed fields", Width: 30),
            new("old", "Old values", Width: 40, DefaultVisible: false), new("new", "New values", Width: 40, DefaultVisible: false),
            new("ip", "IP address", DefaultVisible: false), new("correlation", "Transaction", DefaultVisible: false)
        }, "Event date", "time", "Confidential", UsesAssetFilters: false, RequiresAuditPermission: true);

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var from = (r.From ?? today.AddDays(-30)).ToDateTime(TimeOnly.MinValue); // default: last 30 days
        var to = r.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await uow.Repository<AuditLog>().QueryAsync(q =>
        {
            q = q.Where(a => a.EventDate >= from);
            if (to is { } t) q = q.Where(a => a.EventDate < t);
            return q.OrderByDescending(a => a.AuditLogId).Take(maxRows + 1)
                .Select(a => new { a.EventDate, a.PerformedBy, a.Action, a.EntityName, a.EntityKey, a.ChangedColumns, a.OldValues, a.NewValues, a.IpAddress, a.CorrelationId });
        }, ct);
        static string? Cut(string? v) => v is null ? null : v.Length <= 2000 ? v : v[..2000] + "…";
        return rows.Select(x => new object?[] { x.EventDate, x.PerformedBy, x.Action.ToString(), x.EntityName, x.EntityKey, x.ChangedColumns,
            Cut(x.OldValues), Cut(x.NewValues), x.IpAddress, x.CorrelationId }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} event(s) by {rows.Select(r => r[1]).Distinct().Count()} user(s)",
        "Sign-in and sign-out events are in Administration > Sign-in history."
    };
}
