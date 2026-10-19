using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Application.Documents;

public sealed record DocumentField(string Label, string? Value);

public sealed record DocumentStep(string Decision, string DecidedBy, DateTime DecidedUtc, string? Remarks, int StageNo);

/// <summary>A document as shown on its detail page.</summary>
public sealed record DocumentView(DocumentType Type, int Id, string? Number, string? ErpReference, string Status, int AssetId, string AssetNumber,
    string AssetName, IReadOnlyList<DocumentField> Fields, IReadOnlyList<DocumentStep> Steps, bool CanEditErp);

public sealed record DocumentRef(DocumentType Type, int Id);

public interface IDocumentService
{
    /// <summary>The document, or null when it does not exist in this company or the user may not view its type.</summary>
    Task<DocumentView?> GetAsync(string typeCode, int id, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default);
    /// <summary>The document with this number (exact, any case) of a type the user may view.</summary>
    Task<DocumentRef?> FindAsync(string number, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default);
    Task<Result> SetErpReferenceAsync(string typeCode, int id, string? erpReference, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default);
}

/// <summary>Document detail pages and ERP references. Company isolation applies to every query.</summary>
internal sealed class DocumentService : IDocumentService
{
    private readonly IUnitOfWork _uow;

    public DocumentService(IUnitOfWork uow) => _uow = uow;

    private static string? D(DateOnly? d) => d?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    private static string? DT(DateTime? d) => d?.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + (d is null ? "" : " UTC");
    private static string? M(decimal? v, string? cur) => v is null ? null : v.Value.ToString("N2", CultureInfo.InvariantCulture) + (cur is null ? "" : " " + cur);
    private static string H(object e) => Common.DisplayText.Humanize(e.ToString()!);
    private static string? Name(string? first, string? last) => first is null ? null : first + " " + last;

    public async Task<DocumentRef?> FindAsync(string number, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var n = (number ?? string.Empty).Trim().ToUpperInvariant();
        var type = DocumentNumberFormat.TypeOf(n);
        if (type is null || !permissions.Contains(type.ViewPermission)) return null;
        var ct = cancellationToken;
        int? id = type.Code switch
        {
            "MOV" => (await _uow.Repository<AssetMovement>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.AssetMovementId), ct)).FirstOrDefault(),
            "MNT" => (await _uow.Repository<AssetMaintenance>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.AssetMaintenanceId), ct)).FirstOrDefault(),
            "DSP" => (await _uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.AssetDisposalId), ct)).FirstOrDefault(),
            "ASG" => (await _uow.Repository<AssetAssignment>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.AssetAssignmentId), ct)).FirstOrDefault(),
            "VER" => (await _uow.Repository<AssetAudit>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.AssetAuditId), ct)).FirstOrDefault(),
            _ => (await _uow.Repository<Warranty>().QueryAsync(q => q.Where(x => x.DocumentNumber == n).Select(x => (int?)x.WarrantyId), ct)).FirstOrDefault()
        };
        return id is null ? null : new DocumentRef(type, id.Value);
    }

    public async Task<DocumentView?> GetAsync(string typeCode, int id, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var type = DocumentTypes.Find(typeCode);
        if (type is null || !permissions.Contains(type.ViewPermission)) return null;
        var money = permissions.Contains(Permissions.Assets.ViewFinancials);
        var ct = cancellationToken;
        var canEdit = permissions.Contains(type.EditPermission);
        switch (type.Code)
        {
            case "MOV":
            {
                var x = (await _uow.Repository<AssetMovement>().QueryAsync(q => q.Where(m => m.AssetMovementId == id).Select(m => new
                {
                    m.DocumentNumber, m.ErpReference, m.MovementStatus, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.MovementType,
                    FL = m.FromLocation == null ? null : m.FromLocation.LocationName, FB = m.FromBuilding == null ? null : m.FromBuilding.BuildingName,
                    FR = m.FromRoom == null ? null : m.FromRoom.RoomName, TL = m.ToLocation.LocationName,
                    TB = m.ToBuilding == null ? null : m.ToBuilding.BuildingName, TR = m.ToRoom == null ? null : m.ToRoom.RoomName,
                    m.MovementReason, m.ChangeRequestNumber, m.RequestedBy, m.RequestedDate, m.ApprovedBy, m.ApprovedDate, m.CompletedDate, m.RejectionReason
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                static string? J(params string?[] p) { var v = p.Where(s => s is not null).ToArray(); return v.Length == 0 ? null : string.Join(" / ", v); }
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, x.MovementStatus.ToString(), x.AssetId, x.AssetNumber, x.AssetName, new[]
                {
                    new DocumentField("Type", H(x.MovementType)), new DocumentField("From", J(x.FL, x.FB, x.FR)), new DocumentField("To", J(x.TL, x.TB, x.TR)),
                    new DocumentField("Reason", x.MovementReason), new DocumentField("Change request", x.ChangeRequestNumber),
                    new DocumentField("Requested", $"{DT(x.RequestedDate)} by {x.RequestedBy}"),
                    new DocumentField("Approved", x.ApprovedBy is null ? null : $"{DT(x.ApprovedDate)} by {x.ApprovedBy}"),
                    new DocumentField("Completed", DT(x.CompletedDate)), new DocumentField("Rejection reason", x.RejectionReason)
                }, await StepsAsync(Lifecycle.RequestTypes.Movement, id, ct), canEdit);
            }
            case "MNT":
            {
                var x = (await _uow.Repository<AssetMaintenance>().QueryAsync(q => q.Where(m => m.AssetMaintenanceId == id).Select(m => new
                {
                    m.DocumentNumber, m.ErpReference, m.MaintenanceStatus, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.MaintenanceType, m.ScheduledDate,
                    m.StartDate, m.CompletedDate, m.ProblemDescription, m.ResolutionDetails, m.TicketNumber, m.PerformedBy,
                    Tech = m.AssignedToUser == null ? null : m.AssignedToUser.DisplayName, Vendor = m.Vendor == null ? null : m.Vendor.VendorName,
                    m.Cost, Cur = m.Currency == null ? null : m.Currency.CurrencyCode, m.DowntimeHours, m.NextMaintenanceDate
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                var f = new List<DocumentField>
                {
                    new("Type", H(x.MaintenanceType)), new("Scheduled", D(x.ScheduledDate)), new("Started", DT(x.StartDate)), new("Completed", DT(x.CompletedDate)),
                    new("Technician", x.Tech), new("External technician", x.PerformedBy), new("Vendor", x.Vendor), new("Ticket", x.TicketNumber),
                    new("Problem / work", x.ProblemDescription), new("Resolution", x.ResolutionDetails),
                    new("Downtime (hours)", x.DowntimeHours?.ToString("0.#", CultureInfo.InvariantCulture)), new("Next maintenance", D(x.NextMaintenanceDate))
                };
                if (money) f.Add(new("Cost", M(x.Cost, x.Cur)));
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, H(x.MaintenanceStatus), x.AssetId, x.AssetNumber, x.AssetName, f, Array.Empty<DocumentStep>(), canEdit);
            }
            case "DSP":
            {
                var x = (await _uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(d => d.AssetDisposalId == id).Select(d => new
                {
                    d.DocumentNumber, d.ErpReference, d.DisposalStatus, d.AssetId, d.Asset.AssetNumber, d.Asset.AssetName, Method = d.DisposalMethod.MethodName,
                    d.Reason, d.RequestedBy, d.RequestedDate, d.ApprovedBy, d.ApprovedDate, d.RejectionReason, d.DisposalDate, d.DataSanitizationMethod,
                    d.DataSanitizationDate, d.DataSanitizationVerifiedBy, d.CertificateNumber, d.RecipientName, d.SaleValue,
                    Cur = d.Currency == null ? null : d.Currency.CurrencyCode, d.Remarks
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                var f = new List<DocumentField>
                {
                    new("Method", x.Method), new("Reason", x.Reason), new("Requested", $"{DT(x.RequestedDate)} by {x.RequestedBy}"),
                    new("Approved", x.ApprovedBy is null ? null : $"{DT(x.ApprovedDate)} by {x.ApprovedBy}"), new("Rejection reason", x.RejectionReason),
                    new("Disposal date", D(x.DisposalDate)), new("Data sanitisation", x.DataSanitizationMethod is null ? null
                        : $"{x.DataSanitizationMethod}, {DT(x.DataSanitizationDate)}, verified by {x.DataSanitizationVerifiedBy}"),
                    new("Certificate", x.CertificateNumber), new("Recipient", x.RecipientName), new("Remarks", x.Remarks)
                };
                if (money) f.Add(new("Sale / residual value", M(x.SaleValue, x.Cur)));
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, x.DisposalStatus.ToString(), x.AssetId, x.AssetNumber, x.AssetName, f,
                    await StepsAsync(Lifecycle.RequestTypes.Disposal, id, ct), canEdit);
            }
            case "ASG":
            {
                var x = (await _uow.Repository<AssetAssignment>().QueryAsync(q => q.Where(a => a.AssetAssignmentId == id).Select(a => new
                {
                    a.DocumentNumber, a.ErpReference, a.IsCurrent, a.AssetId, a.Asset.AssetNumber, a.Asset.AssetName, a.AssignmentType,
                    EF = a.Employee == null ? null : a.Employee.FirstName, EL = a.Employee == null ? null : a.Employee.LastName,
                    Dept = a.Department == null ? null : a.Department.DepartmentName, a.AssignedDate, a.ExpectedReturnDate, a.ReturnedDate,
                    a.ConditionAtAssignment, a.ConditionAtReturn, a.AcknowledgementReceived, a.AcknowledgementDate, a.AssignedBy, a.ReturnedTo, a.Notes
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, x.IsCurrent ? "Current" : "Returned", x.AssetId, x.AssetNumber, x.AssetName, new[]
                {
                    new DocumentField("Assigned to", Name(x.EF, x.EL) ?? x.Dept), new DocumentField("Department", x.Dept),
                    new DocumentField("Type", H(x.AssignmentType)), new DocumentField("Assigned", $"{DT(x.AssignedDate)} by {x.AssignedBy}"),
                    new DocumentField("Expected return", D(x.ExpectedReturnDate)), new DocumentField("Condition at hand-over", x.ConditionAtAssignment),
                    new DocumentField("Acknowledged", x.AcknowledgementReceived ? DT(x.AcknowledgementDate) ?? "Yes" : "Not yet"),
                    new DocumentField("Returned", x.ReturnedDate is null ? null : $"{DT(x.ReturnedDate)} to {x.ReturnedTo}"),
                    new DocumentField("Condition at return", x.ConditionAtReturn), new DocumentField("Notes", x.Notes)
                }, Array.Empty<DocumentStep>(), canEdit);
            }
            case "VER":
            {
                var x = (await _uow.Repository<AssetAudit>().QueryAsync(q => q.Where(a => a.AssetAuditId == id).Select(a => new
                {
                    a.DocumentNumber, a.ErpReference, a.AuditResult, a.AssetId, a.Asset.AssetNumber, a.Asset.AssetName, a.AuditCycle, a.AuditDate, a.AuditedBy,
                    a.ScanMethod, FL = a.FoundLocation == null ? null : a.FoundLocation.LocationName, FR = a.FoundRoom == null ? null : a.FoundRoom.RoomName,
                    FF = a.FoundEmployee == null ? null : a.FoundEmployee.FirstName, FE = a.FoundEmployee == null ? null : a.FoundEmployee.LastName,
                    a.Remarks, a.IsResolved, a.CorrectiveAction, a.ResolvedDate
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, H(x.AuditResult), x.AssetId, x.AssetNumber, x.AssetName, new[]
                {
                    new DocumentField("Cycle", x.AuditCycle), new DocumentField("Checked", $"{DT(x.AuditDate)} by {x.AuditedBy}"),
                    new DocumentField("Scan", H(x.ScanMethod)), new DocumentField("Found at", x.FL is null ? null : x.FL + (x.FR is null ? "" : " / " + x.FR)),
                    new DocumentField("Found with", Name(x.FF, x.FE)), new DocumentField("Remarks", x.Remarks),
                    new DocumentField("Corrective action", x.CorrectiveAction), new DocumentField("Resolved", x.IsResolved ? DT(x.ResolvedDate) ?? "Yes" : "No")
                }, Array.Empty<DocumentStep>(), canEdit);
            }
            case "WRN":
            {
                var x = (await _uow.Repository<Warranty>().QueryAsync(q => q.Where(w => w.WarrantyId == id).Select(w => new
                {
                    w.DocumentNumber, w.ErpReference, Status = w.WarrantyStatus.StatusName, w.AssetId, w.Asset.AssetNumber, w.Asset.AssetName, w.WarrantyType,
                    Provider = w.ProviderVendor == null ? null : w.ProviderVendor.VendorName, w.ContractNumber, w.StartDate, w.EndDate, w.SupportLevel, w.Cost,
                    Cur = w.Currency == null ? null : w.Currency.CurrencyCode, w.Terms
                }), ct)).FirstOrDefault();
                if (x is null) return null;
                var f = new List<DocumentField>
                {
                    new("Type", H(x.WarrantyType)), new("Provider", x.Provider), new("Contract / service tag", x.ContractNumber),
                    new("Start", D(x.StartDate)), new("End", D(x.EndDate)), new("Coverage", x.SupportLevel), new("Terms", x.Terms)
                };
                if (money) f.Add(new("Cost", M(x.Cost, x.Cur)));
                return new DocumentView(type, id, x.DocumentNumber, x.ErpReference, x.Status, x.AssetId, x.AssetNumber, x.AssetName, f, Array.Empty<DocumentStep>(), canEdit);
            }
            default: return null;
        }
    }

    private async Task<IReadOnlyList<DocumentStep>> StepsAsync(string requestType, int id, CancellationToken ct) =>
        await _uow.Repository<ApprovalStep>().QueryAsync(q => q.Where(s => s.RequestType == requestType && s.RequestId == id).OrderBy(s => s.ApprovalStepId)
            .Select(s => new DocumentStep(s.Decision, s.DecidedBy, s.DecidedDate, s.Remarks, s.StageNo)), ct);

    public async Task<Result> SetErpReferenceAsync(string typeCode, int id, string? erpReference, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var type = DocumentTypes.Find(typeCode);
        if (type is null) return Result.Failure(Error.NotFound("Unknown document type."));
        if (!permissions.Contains(type.EditPermission)) return Result.Failure(Error.Forbidden("You may not change this document."));
        var erp = string.IsNullOrWhiteSpace(erpReference) ? null : erpReference.Trim();
        if (erp is { Length: > 50 }) return Result.Failure(Error.Validation("ERP reference: up to 50 characters.", "erpReference"));
        if (erp is not null && erp.Any(c => char.IsControl(c))) return Result.Failure(Error.Validation("ERP reference: letters, digits and punctuation only.", "erpReference"));
        // an ERP reference must never look like one of this system's own document numbers (search would be ambiguous)
        if (erp is not null && DocumentNumberFormat.TypeOf(erp) is not null)
            return Result.Failure(Error.Validation("That is a document number of this system, not an ERP reference.", "erpReference"));
        var ct = cancellationToken;
        bool found;
        switch (type.Code)
        {
            case "MOV": found = await Set<AssetMovement>(x => x.AssetMovementId == id, e => e.ErpReference = erp, ct); break;
            case "MNT": found = await Set<AssetMaintenance>(x => x.AssetMaintenanceId == id, e => e.ErpReference = erp, ct); break;
            case "DSP": found = await Set<AssetDisposal>(x => x.AssetDisposalId == id, e => e.ErpReference = erp, ct); break;
            case "ASG": found = await Set<AssetAssignment>(x => x.AssetAssignmentId == id, e => e.ErpReference = erp, ct); break;
            case "VER": found = await Set<AssetAudit>(x => x.AssetAuditId == id, e => e.ErpReference = erp, ct); break;
            default: found = await Set<Warranty>(x => x.WarrantyId == id, e => e.ErpReference = erp, ct); break;
        }
        if (!found) return Result.Failure(Error.NotFound("Document not found."));
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<bool> Set<T>(System.Linq.Expressions.Expression<Func<T, bool>> where, Action<T> apply, CancellationToken ct) where T : class
    {
        var e = await _uow.Repository<T>().FirstOrDefaultAsync(where, ct);
        if (e is null) return false;
        apply(e);
        return true;
    }
}
