using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using C = AMGH.ITInventory.Application.Reports.ReportColumn;
using K = AMGH.ITInventory.Application.Reports.ReportColumnKind;

namespace AMGH.ITInventory.Application.Reports;

internal static class Lc
{
    public static string H(object e) => Common.DisplayText.Humanize(e.ToString()!);
    public static string? Join(params string?[] p) { var x = p.Where(v => !string.IsNullOrEmpty(v)).ToArray(); return x.Length == 0 ? null : string.Join(" / ", x); }
}

// ======================================================================== Maintenance

internal sealed class MaintenanceReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("maintenance", "Maintenance Report",
        "Preventive and corrective maintenance: scheduled and completed jobs, technicians, downtime and cost.", "bi-tools",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 24), new("category", "Category", DefaultVisible: false, Groupable: true),
            new("type", "Type", Groupable: true), new("status", "Status", Groupable: true), new("scheduled", "Scheduled", K.Date),
            new("completed", "Completed", K.Date), new("technician", "Technician / vendor", Width: 20, Groupable: true),
            new("ticket", "Ticket", DefaultVisible: false), new("problem", "Problem", Width: 30), new("resolution", "Resolution", Width: 30, DefaultVisible: false),
            new("downtime", "Downtime (h)", K.Number), new("cost", "Cost", K.Money, Access: ColumnAccess.Financial),
            new("currency", "Currency", DefaultVisible: false, Access: ColumnAccess.Financial), new("overdue", "Overdue", Groupable: true), new("docNo", "Document no.", Width: 18)
        }, "Scheduled date", "scheduled", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var rows = await uow.Repository<AssetMaintenance>().QueryAsync(q =>
        {
            if (r.CategoryId is { } c) q = q.Where(m => m.Asset.AssetCategoryId == c);
            if (r.DepartmentId is { } d) q = q.Where(m => m.Asset.DepartmentId == d);
            if (r.LocationId is { } l) q = q.Where(m => m.Asset.LocationId == l);
            if (r.BuildingId is { } b) q = q.Where(m => m.Asset.BuildingId == b);
            if (r.StatusId is { } s) q = q.Where(m => m.Asset.AssetStatusId == s);
            if (r.From is { } f) q = q.Where(m => m.ScheduledDate >= f);
            if (r.To is { } t) q = q.Where(m => m.ScheduledDate <= t);
            return q.OrderByDescending(m => m.ScheduledDate).Take(maxRows + 1).Select(m => new
            {
                DocNo = m.DocumentNumber,
                m.Asset.AssetNumber, m.Asset.AssetName, Cat = m.Asset.AssetCategory.CategoryName, m.MaintenanceType, m.MaintenanceStatus, m.ScheduledDate,
                m.CompletedDate, m.PerformedBy, Vendor = m.Vendor == null ? null : m.Vendor.VendorName, m.TicketNumber, m.ProblemDescription,
                Tech = m.AssignedToUser == null ? null : m.AssignedToUser.DisplayName,
                m.ResolutionDetails, m.DowntimeHours, m.Cost, Cur = m.Currency == null ? null : m.Currency.CurrencyCode
            });
        }, ct);
        return rows.Select(x => new object?[]
        {
            x.AssetNumber, x.AssetName, x.Cat, Lc.H(x.MaintenanceType), Lc.H(x.MaintenanceStatus), x.ScheduledDate,
            x.CompletedDate is { } cd ? DateOnly.FromDateTime(cd) : null, Lc.Join(x.Tech, x.PerformedBy, x.Vendor), x.TicketNumber, x.ProblemDescription,
            x.ResolutionDetails, x.DowntimeHours, x.Cost, x.Cur,
            x.MaintenanceStatus == MaintenanceStatus.Scheduled && x.ScheduledDate < today ? "Yes" : "No", x.DocNo
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows)
    {
        var completed = rows.Count(r => (string?)r[4] == "Completed");
        var downtime = rows.Sum(r => r[11] is decimal d ? d : 0m);
        return new[]
        {
            $"{rows.Count} job(s): {completed} completed, {rows.Count(r => (string?)r[4] is "Scheduled" or "In progress")} open, {rows.Count(r => (string?)r[14] == "Yes")} overdue",
            $"{rows.Count(r => (string?)r[3] == "Corrective")} corrective (repairs), {rows.Count(r => (string?)r[3] == "Preventive")} preventive",
            "Total downtime " + downtime.ToString("#,0.#", System.Globalization.CultureInfo.InvariantCulture) + " hours"
        };
    }
}

// ======================================================================== Movements

internal sealed class MovementReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("movements", "Asset Movement Report",
        "Every requested, approved, rejected and completed move: from where, to where, who requested and approved it.", "bi-arrow-left-right",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 22), new("type", "Type", Groupable: true),
            new("status", "Status", Groupable: true), new("from", "From", Width: 26), new("to", "To", Width: 26),
            new("requestedBy", "Requested by", Groupable: true), new("requested", "Requested", K.Date), new("approvedBy", "Approved by"),
            new("approved", "Approved", K.Date, DefaultVisible: false), new("completed", "Completed", K.Date),
            new("reason", "Reason", Width: 30), new("cr", "Change request", DefaultVisible: false), new("rejection", "Rejection reason", DefaultVisible: false), new("docNo", "Document no.", Width: 18)
        }, "Request date", "requested", "Internal");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var from = r.From?.ToDateTime(TimeOnly.MinValue);
        var to = r.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await uow.Repository<AssetMovement>().QueryAsync(q =>
        {
            if (r.CategoryId is { } c) q = q.Where(m => m.Asset.AssetCategoryId == c);
            if (r.DepartmentId is { } d) q = q.Where(m => m.Asset.DepartmentId == d);
            if (r.LocationId is { } l) q = q.Where(m => m.ToLocationId == l || m.FromLocationId == l);
            if (r.BuildingId is { } b) q = q.Where(m => m.ToBuildingId == b || m.FromBuildingId == b);
            if (from is { } f) q = q.Where(m => m.RequestedDate >= f);
            if (to is { } t) q = q.Where(m => m.RequestedDate < t);
            return q.OrderByDescending(m => m.RequestedDate).Take(maxRows + 1).Select(m => new
            {
                DocNo = m.DocumentNumber,
                m.Asset.AssetNumber, m.Asset.AssetName, m.MovementType, m.MovementStatus,
                FL = m.FromLocation == null ? null : m.FromLocation.LocationName, FB = m.FromBuilding == null ? null : m.FromBuilding.BuildingName,
                FR = m.FromRoom == null ? null : m.FromRoom.RoomName, TL = m.ToLocation.LocationName,
                TB = m.ToBuilding == null ? null : m.ToBuilding.BuildingName, TR = m.ToRoom == null ? null : m.ToRoom.RoomName,
                m.RequestedBy, m.RequestedDate, m.ApprovedBy, m.ApprovedDate, m.CompletedDate, m.MovementReason, m.ChangeRequestNumber, m.RejectionReason
            });
        }, ct);
        static DateOnly? D(DateTime? v) => v is { } x ? DateOnly.FromDateTime(x) : null;
        return rows.Select(x => new object?[]
        {
            x.AssetNumber, x.AssetName, Lc.H(x.MovementType), x.MovementStatus.ToString(), Lc.Join(x.FL, x.FB, x.FR), Lc.Join(x.TL, x.TB, x.TR),
            x.RequestedBy, DateOnly.FromDateTime(x.RequestedDate), x.ApprovedBy, D(x.ApprovedDate), D(x.CompletedDate), x.MovementReason,
            x.ChangeRequestNumber, x.RejectionReason, x.DocNo
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} movement(s): {rows.Count(r => (string?)r[3] == "Completed")} completed, {rows.Count(r => (string?)r[3] is "Requested" or "Approved")} open, {rows.Count(r => (string?)r[3] == "Rejected")} rejected"
    };
}

// ======================================================================== Disposals

internal sealed class DisposalReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("disposals", "Disposal Report",
        "Disposal requests and completed disposals with method, approvals, data sanitisation evidence and certificates.", "bi-recycle",
        new C[]
        {
            new("number", "Asset number", Width: 18), new("name", "Asset name", Width: 22), new("category", "Category", Groupable: true),
            new("method", "Method", Groupable: true), new("status", "Status", Groupable: true), new("reason", "Reason", Width: 26),
            new("requested", "Requested", K.Date), new("requestedBy", "Requested by", DefaultVisible: false), new("approvedBy", "Approved by"),
            new("disposed", "Disposal date", K.Date), new("sanitization", "Data sanitisation", Width: 22), new("verifiedBy", "Verified by", DefaultVisible: false),
            new("certificate", "Certificate no."), new("recipient", "Recipient", DefaultVisible: false),
            new("value", "Disposal value", K.Money, Access: ColumnAccess.Financial), new("bookCost", "Purchase value", K.Money, DefaultVisible: false, Access: ColumnAccess.Financial), new("docNo", "Document no.", Width: 18)
        }, "Request date", "requested", "Confidential");

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var from = r.From?.ToDateTime(TimeOnly.MinValue);
        var to = r.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await uow.Repository<AssetDisposal>().QueryAsync(q =>
        {
            if (r.CategoryId is { } c) q = q.Where(d => d.Asset.AssetCategoryId == c);
            if (r.DepartmentId is { } dep) q = q.Where(d => d.Asset.DepartmentId == dep);
            if (r.LocationId is { } l) q = q.Where(d => d.Asset.LocationId == l);
            if (r.BuildingId is { } b) q = q.Where(d => d.Asset.BuildingId == b);
            if (from is { } f) q = q.Where(d => d.RequestedDate >= f);
            if (to is { } t) q = q.Where(d => d.RequestedDate < t);
            return q.OrderByDescending(d => d.RequestedDate).Take(maxRows + 1).Select(d => new
            {
                DocNo = d.DocumentNumber,
                d.Asset.AssetNumber, d.Asset.AssetName, Cat = d.Asset.AssetCategory.CategoryName, Method = d.DisposalMethod.MethodName, d.DisposalStatus,
                d.Reason, d.RequestedDate, d.RequestedBy, d.ApprovedBy, d.DisposalDate, d.DataSanitizationMethod, d.DataSanitizationDate,
                d.DataSanitizationVerifiedBy, d.CertificateNumber, d.RecipientName, d.SaleValue, d.Asset.PurchaseCost
            });
        }, ct);
        return rows.Select(x => new object?[]
        {
            x.AssetNumber, x.AssetName, x.Cat, x.Method, x.DisposalStatus.ToString(), x.Reason, DateOnly.FromDateTime(x.RequestedDate), x.RequestedBy,
            x.ApprovedBy, x.DisposalDate,
            x.DataSanitizationMethod is null ? null : x.DataSanitizationMethod + (x.DataSanitizationDate is { } sd ? " (" + sd.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + ")" : ""),
            x.DataSanitizationVerifiedBy, x.CertificateNumber, x.RecipientName, x.SaleValue, x.PurchaseCost, x.DocNo
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} disposal(s): {rows.Count(r => (string?)r[4] == "Completed")} completed, {rows.Count(r => (string?)r[4] is "Requested" or "Approved")} in progress, {rows.Count(r => (string?)r[4] == "Rejected")} rejected",
        $"{rows.Count(r => (string?)r[4] == "Completed" && r[10] is null && (string?)r[3] != "Write-off (Lost/Stolen)")} completed disposal(s) without recorded data sanitisation"
    };
}

// ======================================================================== Licence compliance

internal sealed class LicenseReport : IReportSource
{
    public ReportDescriptor Descriptor { get; } = new("licenses", "Software Licence Compliance",
        "Licences with seats purchased and in use, expiry and renewal status; over-allocation shows non-compliance.", "bi-key",
        new C[]
        {
            new("number", "Licence no.", Width: 16), new("software", "Software", Width: 24), new("version", "Version", DefaultVisible: false),
            new("publisher", "Publisher", Groupable: true), new("type", "Type", Groupable: true), new("seats", "Seats", K.Number),
            new("used", "In use", K.Number), new("free", "Available", K.Number), new("expiry", "Expiry", K.Date),
            new("state", "Compliance", Groupable: true), new("owner", "Owner", DefaultVisible: false),
            new("cost", "Cost", K.Money, DefaultVisible: false, Access: ColumnAccess.Financial)
        }, "Expiry date", "expiry", "Internal", UsesAssetFilters: false);

    public async Task<List<object?[]>> LoadAsync(IUnitOfWork uow, ReportRequest r, ReportAccess access, DateOnly today, int maxRows, CancellationToken ct)
    {
        var rows = await uow.Repository<SoftwareLicense>().QueryAsync(q =>
        {
            if (r.From is { } f) q = q.Where(l => l.ExpiryDate >= f);
            if (r.To is { } t) q = q.Where(l => l.ExpiryDate <= t);
            return q.OrderBy(l => l.SoftwareName).Take(maxRows + 1).Select(l => new
            {
                l.LicenseNumber, l.SoftwareName, l.SoftwareVersion, Pub = l.Publisher == null ? null : l.Publisher.ManufacturerName, l.LicenseType,
                l.TotalSeats, Used = l.Allocations.Count(a => a.IsCurrent && !a.IsDeleted), l.ExpiryDate, l.RenewalReminderDays, l.IsActive,
                Owner = l.ContractOwner == null ? null : l.ContractOwner.FirstName + " " + l.ContractOwner.LastName, l.Cost
            });
        }, ct);
        return rows.Select(x => new object?[]
        {
            x.LicenseNumber, x.SoftwareName, x.SoftwareVersion, x.Pub, Lc.H(x.LicenseType), x.TotalSeats, x.Used,
            x.TotalSeats is { } s ? Math.Max(0, s - x.Used) : null, x.ExpiryDate,
            LicenseService.StateOf(x.ExpiryDate, x.RenewalReminderDays, today, x.TotalSeats, x.Used, x.IsActive), x.Owner, x.Cost
        }).ToList();
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<object?[]> rows) => new[]
    {
        $"{rows.Count} licence(s): {rows.Count(r => (string?)r[9] == "Compliant")} compliant, {rows.Count(r => (string?)r[9] == "Over-allocated")} over-allocated, {rows.Count(r => (string?)r[9] == "Expired")} expired, {rows.Count(r => (string?)r[9] == "Renewal due")} due for renewal"
    };
}
