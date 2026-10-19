using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Application.Lifecycle;

internal static class Text
{
    public static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

// ============================================================================ maintenance and repairs

public sealed class MaintenanceForm
{
    public MaintenanceType MaintenanceType { get; set; } = MaintenanceType.Corrective;
    public DateOnly? ScheduledDate { get; set; }
    public string? ProblemDescription { get; set; }
    public int? VendorId { get; set; }
    public string? TicketNumber { get; set; }
    /// <summary>External technician or company name (free text) - for people without an account.</summary>
    public string? PerformedBy { get; set; }
    /// <summary>The technician (user) the job is assigned to.</summary>
    public int? AssignedToUserId { get; set; }
}

/// <summary>A person who may be assigned maintenance: an active member of the company whose role grants Maintenance.Manage.</summary>
public sealed record TechnicianOption(int UserId, string Name);

public sealed class MaintenanceCompletionForm
{
    public string? ResolutionDetails { get; set; }
    public decimal? Cost { get; set; }
    public int? CurrencyId { get; set; }
    public decimal? DowntimeHours { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
}

public sealed record MaintenanceItem(int AssetMaintenanceId, int AssetId, string AssetNumber, string AssetName, string Type, string Status,
    DateOnly ScheduledDate, DateTime? StartUtc, DateTime? CompletedUtc, string Problem, string? Resolution, string? Vendor, string? Ticket,
    string? PerformedBy, decimal? Cost, string? Currency, decimal? DowntimeHours, DateOnly? NextDate, bool Overdue,
    int? AssignedToUserId = null, string? AssignedTo = null)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
    /// <summary>Who does the job: the assigned technician, else the external name, else the vendor.</summary>
    public string? DoneBy => AssignedTo ?? PerformedBy ?? Vendor;
}

public enum MaintenanceView { Open, Overdue, Completed, All }

public interface IMaintenanceService
{
    Task<Result<int>> ScheduleAsync(int assetId, MaintenanceForm form, CancellationToken cancellationToken = default);
    /// <summary>Starts the job; with <paramref name="takeOutOfService"/> the asset's status becomes Under Maintenance.</summary>
    Task<Result> StartAsync(int maintenanceId, bool takeOutOfService, CancellationToken cancellationToken = default);
    /// <summary>Completes the job; an asset taken out of service returns to Assigned (if it has a custodian) or In Stock.</summary>
    Task<Result> CompleteAsync(int maintenanceId, MaintenanceCompletionForm form, CancellationToken cancellationToken = default);
    Task<Result> CancelAsync(int maintenanceId, string reason, CancellationToken cancellationToken = default);
    /// <summary>Jobs by view; with <c>assignedToUserId</c> only that technician's jobs ("My jobs").</summary>
    Task<IReadOnlyList<MaintenanceItem>> ListAsync(int? assetId, MaintenanceView view, int max = 300, int? assignedToUserId = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TechnicianOption>> TechniciansAsync(CancellationToken cancellationToken = default);
    /// <summary>Assigns an open job to a technician (null = unassign).</summary>
    Task<Result> AssignAsync(int maintenanceId, int? userId, CancellationToken cancellationToken = default);
}

internal sealed class MaintenanceService : IMaintenanceService
{
    private readonly Documents.IDocumentNumbers _docs;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ICompanyContext _company;

    public MaintenanceService(IUnitOfWork uow, IDateTimeProvider clock, ICompanyContext company, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _clock = clock;
        _company = company;
    }

    public async Task<IReadOnlyList<TechnicianOption>> TechniciansAsync(CancellationToken cancellationToken = default)
    {
        var companyId = _company.CompanyId ?? -1;
        // roles of this company that grant maintenance management (roles and their links are company-isolated)
        var ids = await _uow.Repository<UserRole>().QueryAsync(q => q
            .Where(ur => ur.Role.IsActive && ur.Role.RolePermissions.Any(rp => rp.Permission.PermissionCode == Permissions.Maintenance.Manage))
            .Select(ur => ur.UserId).Distinct(), cancellationToken);
        if (ids.Count == 0) return Array.Empty<TechnicianOption>();
        // memberships are not company-filtered by the model, so the company is checked explicitly
        return await _uow.Repository<CompanyUser>().QueryAsync(q => q
            .Where(m => m.CompanyId == companyId && m.IsActive && m.User.IsActive && ids.Contains(m.UserId))
            .OrderBy(m => m.User.DisplayName)
            .Select(m => new TechnicianOption(m.UserId, m.User.DisplayName)), cancellationToken);
    }

    private async Task<Error?> CheckTechnicianAsync(int? userId, CancellationToken ct)
    {
        if (userId is null) return null;
        return (await TechniciansAsync(ct)).Any(t => t.UserId == userId)
            ? null
            : Error.Validation("Choose a technician from the list (an active user of this company who may manage maintenance).", nameof(MaintenanceForm.AssignedToUserId));
    }

    public async Task<Result> AssignAsync(int maintenanceId, int? userId, CancellationToken cancellationToken = default)
    {
        var job = await _uow.Repository<AssetMaintenance>().FirstOrDefaultAsync(m => m.AssetMaintenanceId == maintenanceId, cancellationToken);
        if (job is null) return Result.Failure(Error.NotFound("Maintenance job not found."));
        if (job.MaintenanceStatus is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            return Result.Failure(Error.BusinessRule("Only open jobs can be reassigned."));
        if (await CheckTechnicianAsync(userId, cancellationToken) is { } error) return Result.Failure(error);
        job.AssignedToUserId = userId;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<int>> ScheduleAsync(int assetId, MaintenanceForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var asset = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId).Select(a => a.AssetStatus.IsTerminal), cancellationToken);
        if (asset.Count == 0) return Result<int>.Failure(Error.NotFound("Asset not found."));
        if (asset[0]) return Result<int>.Failure(Error.BusinessRule("Disposed assets cannot be maintained."));
        var problem = form.ProblemDescription?.Trim() ?? string.Empty;
        var errors = new List<Error>();
        if (problem.Length is < 3 or > 2000) errors.Add(Error.Validation("Describe the work or the problem (3-2000 characters).", nameof(form.ProblemDescription)));
        if (form.ScheduledDate is null) errors.Add(Error.Validation("Enter the scheduled date.", nameof(form.ScheduledDate)));
        else if (form.ScheduledDate < _clock.UtcToday.AddYears(-1) || form.ScheduledDate > _clock.UtcToday.AddYears(3))
            errors.Add(Error.Validation("The scheduled date must be within the last year and the next three years.", nameof(form.ScheduledDate)));
        if (form.TicketNumber?.Trim().Length > 50) errors.Add(Error.Validation("Ticket number: up to 50 characters.", nameof(form.TicketNumber)));
        if (form.PerformedBy?.Trim().Length > 150) errors.Add(Error.Validation("External technician: up to 150 characters.", nameof(form.PerformedBy)));
        if (await CheckTechnicianAsync(form.AssignedToUserId, cancellationToken) is { } techError) errors.Add(techError);
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());
        var job = new AssetMaintenance
        {
            AssetId = assetId, MaintenanceType = form.MaintenanceType, MaintenanceStatus = MaintenanceStatus.Scheduled, ScheduledDate = form.ScheduledDate!.Value,
            ProblemDescription = problem, VendorId = form.VendorId, TicketNumber = Text.Clean(form.TicketNumber), PerformedBy = Text.Clean(form.PerformedBy),
            AssignedToUserId = form.AssignedToUserId
        };
        await _uow.ExecuteInTransactionAsync(async ct =>
        {
            job.DocumentNumber = await _docs.NextAsync(Documents.DocumentTypes.Maintenance, _clock.UtcToday, ct);
            await _uow.Repository<AssetMaintenance>().AddAsync(job, ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
        return Result<int>.Success(job.AssetMaintenanceId);
    }

    public async Task<Result> StartAsync(int maintenanceId, bool takeOutOfService, CancellationToken cancellationToken = default)
    {
        var job = await _uow.Repository<AssetMaintenance>().FirstOrDefaultAsync(m => m.AssetMaintenanceId == maintenanceId, cancellationToken);
        if (job is null) return Result.Failure(Error.NotFound("Maintenance job not found."));
        if (job.MaintenanceStatus != MaintenanceStatus.Scheduled) return Result.Failure(Error.BusinessRule("Only scheduled jobs can be started."));
        job.MaintenanceStatus = MaintenanceStatus.InProgress;
        job.StartDate = _clock.UtcNow;
        if (takeOutOfService)
        {
            var asset = await _uow.Repository<Asset>().FirstOrDefaultAsync(a => a.AssetId == job.AssetId, cancellationToken);
            var inMaint = await StatusIdAsync(AssetStatusCodes.UnderMaintenance, cancellationToken);
            if (asset is not null && inMaint is not null) asset.AssetStatusId = inMaint.Value;
        }
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CompleteAsync(int maintenanceId, MaintenanceCompletionForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var job = await _uow.Repository<AssetMaintenance>().FirstOrDefaultAsync(m => m.AssetMaintenanceId == maintenanceId, cancellationToken);
        if (job is null) return Result.Failure(Error.NotFound("Maintenance job not found."));
        if (job.MaintenanceStatus is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            return Result.Failure(Error.BusinessRule("This job is already closed."));
        var errors = new List<Error>();
        var resolution = form.ResolutionDetails?.Trim() ?? string.Empty;
        if (resolution.Length is < 3 or > 2000) errors.Add(Error.Validation("Describe what was done (3-2000 characters).", nameof(form.ResolutionDetails)));
        if (form.Cost is < 0) errors.Add(Error.Validation("Cost cannot be negative.", nameof(form.Cost)));
        if (form.Cost is > 0 && form.CurrencyId is null) errors.Add(Error.Validation("Select the currency of the cost.", nameof(form.CurrencyId)));
        if (form.DowntimeHours is < 0 or > 10000) errors.Add(Error.Validation("Downtime must be between 0 and 10,000 hours.", nameof(form.DowntimeHours)));
        if (form.NextMaintenanceDate is { } n && n <= _clock.UtcToday) errors.Add(Error.Validation("The next maintenance date must be in the future.", nameof(form.NextMaintenanceDate)));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());

        job.MaintenanceStatus = MaintenanceStatus.Completed;
        job.StartDate ??= _clock.UtcNow;
        job.CompletedDate = _clock.UtcNow;
        job.ResolutionDetails = resolution;
        job.Cost = form.Cost;
        job.CurrencyId = form.Cost is > 0 ? form.CurrencyId : null;
        job.DowntimeHours = form.DowntimeHours;
        job.NextMaintenanceDate = form.NextMaintenanceDate;
        await ReturnToServiceAsync(job.AssetId, job.AssetMaintenanceId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(int maintenanceId, string reason, CancellationToken cancellationToken = default)
    {
        var job = await _uow.Repository<AssetMaintenance>().FirstOrDefaultAsync(m => m.AssetMaintenanceId == maintenanceId, cancellationToken);
        if (job is null) return Result.Failure(Error.NotFound("Maintenance job not found."));
        if (job.MaintenanceStatus is not (MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress))
            return Result.Failure(Error.BusinessRule("This job is already closed."));
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 3 or > 500) return Result.Failure(Error.Validation("Give the reason for cancelling.", "reason"));
        job.MaintenanceStatus = MaintenanceStatus.Cancelled;
        job.ResolutionDetails = "Cancelled: " + reason;
        await ReturnToServiceAsync(job.AssetId, job.AssetMaintenanceId, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>An asset under maintenance goes back to Assigned (custodian kept) or In Stock - when no other job keeps it in service.</summary>
    private async Task ReturnToServiceAsync(int assetId, int closingJobId, CancellationToken ct)
    {
        var asset = await _uow.Repository<Asset>().FirstOrDefaultAsync(a => a.AssetId == assetId, ct);
        var inMaint = await StatusIdAsync(AssetStatusCodes.UnderMaintenance, ct);
        if (asset is null || asset.AssetStatusId != inMaint) return;
        // the job being closed is not saved yet, so it is excluded explicitly
        var otherOpen = await _uow.Repository<AssetMaintenance>().AnyAsync(m => m.AssetId == assetId && m.AssetMaintenanceId != closingJobId
            && m.MaintenanceStatus == MaintenanceStatus.InProgress, ct);
        if (otherOpen) return;
        var hasCustodian = await _uow.Repository<AssetAssignment>().AnyAsync(x => x.AssetId == assetId && x.IsCurrent, ct);
        var target = await StatusIdAsync(hasCustodian ? AssetStatusCodes.Assigned : AssetStatusCodes.InStock, ct);
        if (target is not null) asset.AssetStatusId = target.Value;
    }

    private async Task<int?> StatusIdAsync(string code, CancellationToken ct) =>
        (await _uow.Repository<AssetStatus>().QueryAsync(q => q.Where(s => s.StatusCode == code).Select(s => (int?)s.AssetStatusId), ct)).FirstOrDefault();

    public async Task<IReadOnlyList<MaintenanceItem>> ListAsync(int? assetId, MaintenanceView view, int max = 300, int? assignedToUserId = null,
        CancellationToken cancellationToken = default)
    {
        var today = _clock.UtcToday;
        var rows = await _uow.Repository<AssetMaintenance>().QueryAsync(q =>
        {
            if (assetId is { } a) q = q.Where(m => m.AssetId == a);
            if (assignedToUserId is { } tech) q = q.Where(m => m.AssignedToUserId == tech);
            q = view switch
            {
                MaintenanceView.Open => q.Where(m => m.MaintenanceStatus == MaintenanceStatus.Scheduled || m.MaintenanceStatus == MaintenanceStatus.InProgress),
                MaintenanceView.Overdue => q.Where(m => m.MaintenanceStatus == MaintenanceStatus.Scheduled && m.ScheduledDate < today),
                MaintenanceView.Completed => q.Where(m => m.MaintenanceStatus == MaintenanceStatus.Completed || m.MaintenanceStatus == MaintenanceStatus.Cancelled),
                _ => q
            };
            return q.OrderBy(m => m.MaintenanceStatus == MaintenanceStatus.Completed || m.MaintenanceStatus == MaintenanceStatus.Cancelled)
                .ThenBy(m => m.ScheduledDate).Take(max).Select(m => new
                {
                    m.AssetMaintenanceId, m.DocumentNumber, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.MaintenanceType, m.MaintenanceStatus, m.ScheduledDate, m.StartDate,
                    m.CompletedDate, m.ProblemDescription, m.ResolutionDetails, Vendor = m.Vendor == null ? null : m.Vendor.VendorName, m.TicketNumber, m.PerformedBy,
                    m.Cost, Cur = m.Currency == null ? null : m.Currency.CurrencyCode, m.DowntimeHours, m.NextMaintenanceDate,
                    m.AssignedToUserId, AssignedTo = m.AssignedToUser == null ? null : m.AssignedToUser.DisplayName
                });
        }, cancellationToken);
        return rows.Select(m => new MaintenanceItem(m.AssetMaintenanceId, m.AssetId, m.AssetNumber, m.AssetName,
            Common.DisplayText.Humanize(m.MaintenanceType.ToString()), Common.DisplayText.Humanize(m.MaintenanceStatus.ToString()), m.ScheduledDate, m.StartDate,
            m.CompletedDate, m.ProblemDescription, m.ResolutionDetails, m.Vendor, m.TicketNumber, m.PerformedBy, m.Cost, m.Cur, m.DowntimeHours,
            m.NextMaintenanceDate, m.MaintenanceStatus == MaintenanceStatus.Scheduled && m.ScheduledDate < today, m.AssignedToUserId, m.AssignedTo) { DocumentNumber = m.DocumentNumber }).ToList();
    }
}

// ============================================================================ physical verification

public sealed record VerificationTarget(int AssetId, string AssetNumber, string AssetName, string Status, string? ExpectedLocation,
    int? ExpectedLocationId, int? ExpectedRoomId, string? Custodian, int? CustodianId, DateOnly? NextDue, string? LastResultInCycle);

public sealed record VerificationProgress(string Cycle, int ActiveAssets, int Checked, int Verified, int Exceptions,
    IReadOnlyList<VerificationTarget> NotYetChecked);

public sealed record VerificationException(int AssetAuditId, int AssetId, string AssetNumber, string AssetName, string Cycle, string Result,
    DateTime AuditedUtc, string AuditedBy, string? Remarks, string? FoundAt)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public sealed class VerificationForm
{
    public string? Cycle { get; set; }
    public AuditResult Result { get; set; } = AuditResult.Verified;
    public ScanMethod ScanMethod { get; set; } = ScanMethod.Manual;
    public int? FoundLocationId { get; set; }
    public int? FoundRoomId { get; set; }
    public int? FoundEmployeeId { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>Reads an asset number from a scan: QR label text (first line), the label's link (/a/NUMBER) or a typed number.</summary>
public static class ScanText
{
    public static string? AssetNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var first = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')[0].Trim();
        var link = first.LastIndexOf("/a/", StringComparison.OrdinalIgnoreCase);
        if (link >= 0) first = first[(link + 3)..];
        first = Uri.UnescapeDataString(first).Trim().ToUpperInvariant();
        return first.Length is > 0 and <= 30 ? first : null;
    }

}

public interface IVerificationService
{
    /// <summary>Finds the asset from a scan: QR label text (first line), the label's link (/a/NUMBER) or a typed asset number.</summary>
    Task<VerificationTarget?> FindAsync(string scannedText, string? cycle, CancellationToken cancellationToken = default);
    Task<Result<int>> RecordAsync(int assetId, VerificationForm form, CancellationToken cancellationToken = default);
    Task<VerificationProgress> ProgressAsync(string cycle, int notCheckedLimit = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VerificationException>> ExceptionsAsync(CancellationToken cancellationToken = default);
    Task<Result> ResolveAsync(int assetAuditId, string correctiveAction, CancellationToken cancellationToken = default);
    string CurrentCycle();
}

internal sealed class VerificationService : IVerificationService
{
    private readonly Documents.IDocumentNumbers _docs;
    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly IDateTimeProvider _clock;

    public VerificationService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, IDateTimeProvider clock, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _procedures = procedures;
        _clock = clock;
    }

    /// <summary>Default cycle name: calendar quarter, e.g. 2026-Q4.</summary>
    public string CurrentCycle() => $"{_clock.UtcToday.Year}-Q{(_clock.UtcToday.Month - 1) / 3 + 1}";

    public async Task<VerificationTarget?> FindAsync(string scannedText, string? cycle, CancellationToken cancellationToken = default)
    {
        var number = ScanText.AssetNumber(scannedText);
        if (number is null) return null;
        var rows = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetNumber == number).Select(a => new
        {
            a.AssetId, a.AssetNumber, a.AssetName, Status = a.AssetStatus.StatusName,
            Loc = a.Location == null ? null : a.Location.LocationName, Bld = a.Building == null ? null : a.Building.BuildingName,
            Room = a.Room == null ? null : a.Room.RoomName, a.LocationId, a.RoomId,
            Emp = a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName, a.AssignedEmployeeId, a.NextAuditDueDate
        }), cancellationToken);
        if (rows.Count == 0) return null;
        var x = rows[0];
        string? last = null;
        if (!string.IsNullOrWhiteSpace(cycle))
        {
            var c = cycle.Trim();
            last = (await _uow.Repository<AssetAudit>().QueryAsync(q => q.Where(a => a.AssetId == x.AssetId && a.AuditCycle == c)
                .OrderByDescending(a => a.AssetAuditId).Select(a => a.AuditResult.ToString()).Take(1), cancellationToken)).FirstOrDefault();
        }
        var place = string.Join(" / ", new[] { x.Loc, x.Bld, x.Room }.Where(p => p is not null));
        return new VerificationTarget(x.AssetId, x.AssetNumber, x.AssetName, x.Status, place.Length == 0 ? null : place, x.LocationId, x.RoomId,
            x.Emp, x.AssignedEmployeeId, x.NextAuditDueDate, last);
    }

    public async Task<Result<int>> RecordAsync(int assetId, VerificationForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var cycle = form.Cycle?.Trim() ?? string.Empty;
        // 20 = the procedure's @AuditCycle NVARCHAR(20): a longer value would be silently truncated by SQL Server
        if (cycle.Length is < 2 or > 20) return Result<int>.Failure(Error.Validation("Enter the verification cycle (2-20 characters, e.g. 2026-Q4).", nameof(form.Cycle)));
        if (form.Result != AuditResult.Verified && string.IsNullOrWhiteSpace(form.Remarks))
            return Result<int>.Failure(Error.Validation("Describe what was found.", nameof(form.Remarks)));
        if (form.Remarks?.Length > 1000) return Result<int>.Failure(Error.Validation("Remarks can be up to 1000 characters.", nameof(form.Remarks)));
        try
        {
            var id = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var newId = await _procedures.RecordAuditAsync(new RecordAuditRequest(assetId, cycle, form.Result, form.ScanMethod, form.FoundLocationId,
                    form.FoundRoomId, form.FoundEmployeeId, Text.Clean(form.Remarks)), ct);
                await _docs.AssignAsync(Documents.DocumentTypes.Verification, newId, ct);
                return newId;
            }, cancellationToken);
            return Result<int>.Success(id);
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException)
        {
            return Result<int>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<VerificationProgress> ProgressAsync(string cycle, int notCheckedLimit = 200, CancellationToken cancellationToken = default)
    {
        cycle = (cycle ?? string.Empty).Trim();
        var ct = cancellationToken;
        var active = await _uow.Repository<Asset>().QueryCountAsync(q => q.Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed), ct);
        var results = await _uow.Repository<AssetAudit>().QueryAsync(q => q.Where(a => a.AuditCycle == cycle)
            .Select(a => new { a.AssetId, a.AssetAuditId, a.AuditResult }), ct);
        var latest = results.GroupBy(r => r.AssetId).Select(g => g.OrderByDescending(r => r.AssetAuditId).First()).ToList();
        var notChecked = await _uow.Repository<Asset>().QueryAsync(q => q
            .Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed && !a.Audits.Any(x => x.AuditCycle == cycle && !x.IsDeleted))
            .OrderBy(a => a.Location == null ? null : a.Location.LocationName).ThenBy(a => a.AssetNumber).Take(notCheckedLimit)
            .Select(a => new VerificationTarget(a.AssetId, a.AssetNumber, a.AssetName, a.AssetStatus.StatusName,
                a.Location == null ? null : a.Location.LocationName + (a.Room == null ? "" : " / " + a.Room.RoomName), a.LocationId, a.RoomId,
                a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName, a.AssignedEmployeeId,
                a.NextAuditDueDate, null)), ct);
        return new VerificationProgress(cycle, active, latest.Count, latest.Count(r => r.AuditResult == AuditResult.Verified),
            latest.Count(r => r.AuditResult != AuditResult.Verified), notChecked);
    }

    public async Task<IReadOnlyList<VerificationException>> ExceptionsAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<AssetAudit>().QueryAsync(q => q.Where(a => a.AuditResult != AuditResult.Verified && !a.IsResolved)
            .OrderByDescending(a => a.AuditDate).Take(500)
            .Select(a => new VerificationException(a.AssetAuditId, a.AssetId, a.Asset.AssetNumber, a.Asset.AssetName, a.AuditCycle,
                a.AuditResult.ToString(), a.AuditDate, a.AuditedBy, a.Remarks,
                a.FoundLocation == null ? null : a.FoundLocation.LocationName + (a.FoundRoom == null ? "" : " / " + a.FoundRoom.RoomName)) { DocumentNumber = a.DocumentNumber }), cancellationToken);

    public async Task<Result> ResolveAsync(int assetAuditId, string correctiveAction, CancellationToken cancellationToken = default)
    {
        var audit = await _uow.Repository<AssetAudit>().FirstOrDefaultAsync(a => a.AssetAuditId == assetAuditId, cancellationToken);
        if (audit is null) return Result.Failure(Error.NotFound("Verification record not found."));
        if (audit.IsResolved) return Result.Failure(Error.BusinessRule("Already resolved."));
        correctiveAction = (correctiveAction ?? string.Empty).Trim();
        if (correctiveAction.Length is < 5 or > 1000) return Result.Failure(Error.Validation("Describe the corrective action (5-1000 characters).", "correctiveAction"));
        audit.IsResolved = true;
        audit.CorrectiveAction = correctiveAction;
        audit.ResolvedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================================ warranties

public sealed class WarrantyForm
{
    public WarrantyType WarrantyType { get; set; } = WarrantyType.Manufacturer;
    public int? ProviderVendorId { get; set; }
    public string? ContractNumber { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? SupportLevel { get; set; }
    public decimal? Cost { get; set; }
    public int? CurrencyId { get; set; }
    public string? Terms { get; set; }
}

public sealed record WarrantyItem(int WarrantyId, int AssetId, string AssetNumber, string AssetName, string Type, string? Provider, string? Contract,
    DateOnly Start, DateOnly End, string State, int DaysRemaining, string? SupportLevel, decimal? Cost, string? Currency, string? Terms)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public interface IWarrantyService
{
    Task<IReadOnlyList<WarrantyItem>> ListAsync(int? assetId, string? state, int max = 500, CancellationToken cancellationToken = default);
    Task<WarrantyForm?> GetAsync(int warrantyId, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(int assetId, int? warrantyId, WarrantyForm form, CancellationToken cancellationToken = default);
    Task<Result> VoidAsync(int warrantyId, CancellationToken cancellationToken = default);
}

internal sealed class WarrantyService : IWarrantyService
{
    private readonly Documents.IDocumentNumbers _docs;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public WarrantyService(IUnitOfWork uow, IDateTimeProvider clock, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _clock = clock;
    }

    public static string StateOf(DateOnly end, DateOnly today, bool voided) =>
        voided ? "Void" : end < today ? "Expired" : end.DayNumber - today.DayNumber <= AssetFocus.ExpiringDays ? "Expiring" : "Active";

    public async Task<IReadOnlyList<WarrantyItem>> ListAsync(int? assetId, string? state, int max = 500, CancellationToken cancellationToken = default)
    {
        var today = _clock.UtcToday;
        var rows = await _uow.Repository<Warranty>().QueryAsync(q =>
        {
            if (assetId is { } a) q = q.Where(w => w.AssetId == a);
            return q.OrderBy(w => w.EndDate).Take(max * 2).Select(w => new
            {
                w.WarrantyId, w.DocumentNumber, w.AssetId, w.Asset.AssetNumber, w.Asset.AssetName, w.WarrantyType, Provider = w.ProviderVendor == null ? null : w.ProviderVendor.VendorName,
                w.ContractNumber, w.StartDate, w.EndDate, Void = w.WarrantyStatus.StatusCode == "VOID", w.SupportLevel, w.Cost,
                Cur = w.Currency == null ? null : w.Currency.CurrencyCode, w.Terms
            });
        }, cancellationToken);
        return rows.Select(w => new WarrantyItem(w.WarrantyId, w.AssetId, w.AssetNumber, w.AssetName, Common.DisplayText.Humanize(w.WarrantyType.ToString()),
                w.Provider, w.ContractNumber, w.StartDate, w.EndDate, StateOf(w.EndDate, today, w.Void), Math.Max(0, w.EndDate.DayNumber - today.DayNumber),
                w.SupportLevel, w.Cost, w.Cur, w.Terms) { DocumentNumber = w.DocumentNumber })
            .Where(w => state is null || string.Equals(w.State, state, StringComparison.OrdinalIgnoreCase)).Take(max).ToList();
    }

    public async Task<WarrantyForm?> GetAsync(int warrantyId, CancellationToken cancellationToken = default)
    {
        var w = await _uow.Repository<Warranty>().FirstOrDefaultAsync(x => x.WarrantyId == warrantyId, cancellationToken);
        return w is null ? null : new WarrantyForm
        {
            WarrantyType = w.WarrantyType, ProviderVendorId = w.ProviderVendorId, ContractNumber = w.ContractNumber, StartDate = w.StartDate,
            EndDate = w.EndDate, SupportLevel = w.SupportLevel, Cost = w.Cost, CurrencyId = w.CurrencyId, Terms = w.Terms
        };
    }

    public async Task<Result<int>> SaveAsync(int assetId, int? warrantyId, WarrantyForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<Error>();
        if (form.StartDate is null) errors.Add(Error.Validation("Enter the start date.", nameof(form.StartDate)));
        if (form.EndDate is null) errors.Add(Error.Validation("Enter the end date.", nameof(form.EndDate)));
        if (form.StartDate is { } s && form.EndDate is { } e && e < s) errors.Add(Error.Validation("The end date is before the start date.", nameof(form.EndDate)));
        if (form.Cost is < 0) errors.Add(Error.Validation("Cost cannot be negative.", nameof(form.Cost)));
        if (form.Cost is > 0 && form.CurrencyId is null) errors.Add(Error.Validation("Select the currency of the cost.", nameof(form.CurrencyId)));
        if (form.ContractNumber?.Trim().Length > 100) errors.Add(Error.Validation("Contract number: up to 100 characters.", nameof(form.ContractNumber)));
        if (form.SupportLevel?.Trim().Length > 100) errors.Add(Error.Validation("Coverage: up to 100 characters.", nameof(form.SupportLevel)));
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());
        if (!await _uow.Repository<Asset>().AnyAsync(a => a.AssetId == assetId, cancellationToken)) return Result<int>.Failure(Error.NotFound("Asset not found."));

        var repo = _uow.Repository<Warranty>();
        Warranty? w;
        if (warrantyId is { } id)
        {
            w = await repo.FirstOrDefaultAsync(x => x.WarrantyId == id && x.AssetId == assetId, cancellationToken);
            if (w is null) return Result<int>.Failure(Error.NotFound("Warranty not found."));
        }
        else
        {
            w = new Warranty { AssetId = assetId };
            await repo.AddAsync(w, cancellationToken);
        }
        var isNew = warrantyId is null;
        w.WarrantyType = form.WarrantyType; w.ProviderVendorId = form.ProviderVendorId; w.ContractNumber = Text.Clean(form.ContractNumber);
        w.StartDate = form.StartDate!.Value; w.EndDate = form.EndDate!.Value; w.SupportLevel = Text.Clean(form.SupportLevel);
        w.Cost = form.Cost; w.CurrencyId = form.Cost is > 0 ? form.CurrencyId : null; w.Terms = Text.Clean(form.Terms);
        var code = StateOf(w.EndDate, _clock.UtcToday, false) switch { "Expired" => "EXPIRED", "Expiring" => "EXPIRING", _ => "ACTIVE" };
        w.WarrantyStatusId = (await _uow.Repository<WarrantyStatus>().QueryAsync(q => q.Where(x => x.StatusCode == code).Select(x => x.WarrantyStatusId), cancellationToken)).First();
        await _uow.ExecuteInTransactionAsync(async ct =>
        {
            if (isNew) w.DocumentNumber = await _docs.NextAsync(Documents.DocumentTypes.Warranty, _clock.UtcToday, ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
        return Result<int>.Success(w.WarrantyId);
    }

    /// <summary>Void keeps the record for audit but excludes it from warranty cover (and from the dashboard).</summary>
    public async Task<Result> VoidAsync(int warrantyId, CancellationToken cancellationToken = default)
    {
        var w = await _uow.Repository<Warranty>().FirstOrDefaultAsync(x => x.WarrantyId == warrantyId, cancellationToken);
        if (w is null) return Result.Failure(Error.NotFound("Warranty not found."));
        w.WarrantyStatusId = (await _uow.Repository<WarrantyStatus>().QueryAsync(q => q.Where(x => x.StatusCode == "VOID").Select(x => x.WarrantyStatusId), cancellationToken)).First();
        _uow.Repository<Warranty>().Remove(w); // soft delete: kept for audit, no longer counts as cover
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================================ software licences

public sealed class LicenseForm
{
    public string? SoftwareName { get; set; }
    public string? SoftwareVersion { get; set; }
    public int? PublisherManufacturerId { get; set; }
    public int? VendorId { get; set; }
    public LicenseType LicenseType { get; set; } = LicenseType.Subscription;
    public int? TotalSeats { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? Cost { get; set; }
    public int? CurrencyId { get; set; }
    public int RenewalReminderDays { get; set; } = 60;
    public int? ContractOwnerEmployeeId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Only when entering or replacing the key; stored encrypted (AES-256 in SQL Server), never shown in lists.</summary>
    public string? LicenseKey { get; set; }
    public string? RowVersion { get; set; }
}

public sealed record LicenseItem(int SoftwareLicenseId, string LicenseNumber, string SoftwareName, string? Version, string Type, string? Publisher,
    int? TotalSeats, int UsedSeats, DateOnly? ExpiryDate, string State, bool HasKey, decimal? Cost, string? Currency, string? Owner, bool IsActive);

public sealed record LicenseAllocationItem(int AllocationId, int SoftwareLicenseId, int? AssetId, string? AssetNumber, int? EmployeeId, string? Employee,
    DateTime AllocatedUtc, DateTime? RevokedUtc, bool IsCurrent, string? Notes);

public interface ILicenseService
{
    Task<IReadOnlyList<LicenseItem>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<LicenseItem?> GetItemAsync(int licenseId, CancellationToken cancellationToken = default);
    Task<LicenseForm?> GetFormAsync(int licenseId, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(int? licenseId, LicenseForm form, CancellationToken cancellationToken = default);
    Task<Result<string>> RevealKeyAsync(int licenseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LicenseAllocationItem>> AllocationsAsync(int? licenseId, int? assetId, CancellationToken cancellationToken = default);
    Task<Result> AllocateAsync(int licenseId, int? assetId, int? employeeId, string? notes, CancellationToken cancellationToken = default);
    Task<Result> RevokeAsync(int allocationId, CancellationToken cancellationToken = default);
}

internal sealed class LicenseService : ILicenseService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly IAssetNumberGenerator _numbers;
    private readonly ILicenseKeyStore _keys;

    public LicenseService(IUnitOfWork uow, IDateTimeProvider clock, IAssetNumberGenerator numbers, ILicenseKeyStore keys)
    {
        _uow = uow;
        _clock = clock;
        _numbers = numbers;
        _keys = keys;
    }

    public static string StateOf(DateOnly? expiry, int reminderDays, DateOnly today, int? seats, int used, bool active) =>
        !active ? "Inactive" : expiry is { } e && e < today ? "Expired" : seats is { } s && used > s ? "Over-allocated"
        : expiry is { } e2 && e2.DayNumber - today.DayNumber <= reminderDays ? "Renewal due" : "Compliant";

    public async Task<IReadOnlyList<LicenseItem>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var today = _clock.UtcToday;
        var rows = await _uow.Repository<SoftwareLicense>().QueryAsync(q => (includeInactive ? q : q.Where(l => l.IsActive))
            .OrderBy(l => l.SoftwareName).Select(l => new
            {
                l.SoftwareLicenseId, l.LicenseNumber, l.SoftwareName, l.SoftwareVersion, l.LicenseType,
                Publisher = l.Publisher == null ? null : l.Publisher.ManufacturerName, l.TotalSeats,
                Used = l.Allocations.Count(a => a.IsCurrent && !a.IsDeleted), l.ExpiryDate, l.RenewalReminderDays, HasKey = l.LicenseKeyEncrypted != null,
                l.Cost, Cur = l.Currency == null ? null : l.Currency.CurrencyCode,
                Owner = l.ContractOwner == null ? null : l.ContractOwner.FirstName + " " + l.ContractOwner.LastName, l.IsActive
            }), cancellationToken);
        return rows.Select(l => new LicenseItem(l.SoftwareLicenseId, l.LicenseNumber, l.SoftwareName, l.SoftwareVersion,
            Common.DisplayText.Humanize(l.LicenseType.ToString()), l.Publisher, l.TotalSeats, l.Used, l.ExpiryDate,
            StateOf(l.ExpiryDate, l.RenewalReminderDays, today, l.TotalSeats, l.Used, l.IsActive), l.HasKey, l.Cost, l.Cur, l.Owner, l.IsActive)).ToList();
    }

    public async Task<LicenseItem?> GetItemAsync(int licenseId, CancellationToken cancellationToken = default) =>
        (await ListAsync(true, cancellationToken)).FirstOrDefault(l => l.SoftwareLicenseId == licenseId);

    public async Task<LicenseForm?> GetFormAsync(int licenseId, CancellationToken cancellationToken = default)
    {
        var l = await _uow.Repository<SoftwareLicense>().FirstOrDefaultAsync(x => x.SoftwareLicenseId == licenseId, cancellationToken);
        return l is null ? null : new LicenseForm
        {
            SoftwareName = l.SoftwareName, SoftwareVersion = l.SoftwareVersion, PublisherManufacturerId = l.PublisherManufacturerId, VendorId = l.VendorId,
            LicenseType = l.LicenseType, TotalSeats = l.TotalSeats, PurchaseDate = l.PurchaseDate, StartDate = l.StartDate, ExpiryDate = l.ExpiryDate,
            PurchaseOrderNumber = l.PurchaseOrderNumber, InvoiceNumber = l.InvoiceNumber, Cost = l.Cost, CurrencyId = l.CurrencyId,
            RenewalReminderDays = l.RenewalReminderDays, ContractOwnerEmployeeId = l.ContractOwnerEmployeeId, Notes = l.Notes,
            RowVersion = Convert.ToBase64String(l.RowVersion)
        };
    }

    public async Task<Result<int>> SaveAsync(int? licenseId, LicenseForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<Error>();
        var name = form.SoftwareName?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 200) errors.Add(Error.Validation("Enter the software name.", nameof(form.SoftwareName)));
        if (form.TotalSeats is < 1 or > 1_000_000) errors.Add(Error.Validation("Seats must be between 1 and 1,000,000 (empty = unlimited).", nameof(form.TotalSeats)));
        if (form.StartDate is { } s && form.ExpiryDate is { } e && e < s) errors.Add(Error.Validation("The expiry date is before the start date.", nameof(form.ExpiryDate)));
        if (form.Cost is < 0) errors.Add(Error.Validation("Cost cannot be negative.", nameof(form.Cost)));
        if (form.Cost is > 0 && form.CurrencyId is null) errors.Add(Error.Validation("Select the currency of the cost.", nameof(form.CurrencyId)));
        if (form.RenewalReminderDays is < 0 or > 365) errors.Add(Error.Validation("Renewal reminder: 0-365 days before expiry.", nameof(form.RenewalReminderDays)));
        if (form.LicenseKey?.Length > 4000) errors.Add(Error.Validation("The licence key is too long.", nameof(form.LicenseKey)));
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());

        var repo = _uow.Repository<SoftwareLicense>();
        try
        {
            var id = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                SoftwareLicense l;
                if (licenseId is { } existing)
                {
                    l = await repo.FirstOrDefaultAsync(x => x.SoftwareLicenseId == existing, ct) ?? throw new NotFoundException("Licence not found.");
                    if (!string.IsNullOrEmpty(form.RowVersion)) repo.SetOriginalRowVersion(l, Convert.FromBase64String(form.RowVersion));
                    if (form.TotalSeats is { } seats)
                    {
                        var used = await _uow.Repository<SoftwareLicenseAllocation>().CountAsync(a => a.SoftwareLicenseId == existing && a.IsCurrent, ct);
                        if (used > seats) throw new BusinessRuleException($"{used} seats are allocated; revoke some before reducing the seats to {seats}.", 50002);
                    }
                }
                else
                {
                    l = new SoftwareLicense { LicenseNumber = await _numbers.NextAsync(SequenceCodes.SoftwareLicense, ct), IsActive = true };
                    await repo.AddAsync(l, ct);
                }
                l.SoftwareName = name; l.SoftwareVersion = Text.Clean(form.SoftwareVersion); l.PublisherManufacturerId = form.PublisherManufacturerId;
                l.VendorId = form.VendorId; l.LicenseType = form.LicenseType; l.TotalSeats = form.TotalSeats; l.PurchaseDate = form.PurchaseDate;
                l.StartDate = form.StartDate; l.ExpiryDate = form.ExpiryDate; l.PurchaseOrderNumber = Text.Clean(form.PurchaseOrderNumber);
                l.InvoiceNumber = Text.Clean(form.InvoiceNumber); l.Cost = form.Cost; l.CurrencyId = form.Cost is > 0 ? form.CurrencyId : null;
                l.RenewalReminderDays = form.RenewalReminderDays; l.ContractOwnerEmployeeId = form.ContractOwnerEmployeeId; l.Notes = Text.Clean(form.Notes);
                await _uow.SaveChangesAsync(ct);
                if (!string.IsNullOrWhiteSpace(form.LicenseKey)) await _keys.SetKeyAsync(l.SoftwareLicenseId, form.LicenseKey.Trim(), ct);
                return l.SoftwareLicenseId;
            }, cancellationToken);
            return Result<int>.Success(id);
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException or ConcurrencyConflictException)
        {
            return Result<int>.Failure(Error.BusinessRule(ex is ConcurrencyConflictException ? "Someone else changed this licence. Reload and try again." : ex.Message));
        }
    }

    /// <summary>Decrypts the key in the database; every reveal is recorded in the audit log by the procedure.</summary>
    public async Task<Result<string>> RevealKeyAsync(int licenseId, CancellationToken cancellationToken = default)
    {
        var key = await _keys.GetKeyAsync(licenseId, cancellationToken);
        return key is null ? Result<string>.Failure(Error.NotFound("No key is stored for this licence.")) : Result<string>.Success(key);
    }

    public async Task<IReadOnlyList<LicenseAllocationItem>> AllocationsAsync(int? licenseId, int? assetId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<SoftwareLicenseAllocation>().QueryAsync(q =>
        {
            if (licenseId is { } l) q = q.Where(a => a.SoftwareLicenseId == l);
            if (assetId is { } x) q = q.Where(a => a.AssetId == x);
            return q.OrderByDescending(a => a.IsCurrent).ThenByDescending(a => a.AllocatedDate).Take(1000).Select(a => new LicenseAllocationItem(
                a.SoftwareLicenseAllocationId, a.SoftwareLicenseId, a.AssetId, a.Asset == null ? null : a.Asset.AssetNumber, a.EmployeeId,
                a.Employee == null ? null : a.Employee.FirstName + " " + a.Employee.LastName, a.AllocatedDate, a.RevokedDate, a.IsCurrent, a.Notes));
        }, cancellationToken);

    public async Task<Result> AllocateAsync(int licenseId, int? assetId, int? employeeId, string? notes, CancellationToken cancellationToken = default)
    {
        if (assetId is null == employeeId is null) return Result.Failure(Error.Validation("Allocate the seat to either an asset or a person."));
        var l = await _uow.Repository<SoftwareLicense>().QueryAsync(q => q.Where(x => x.SoftwareLicenseId == licenseId)
            .Select(x => new { x.TotalSeats, x.IsActive, x.ExpiryDate, Used = x.Allocations.Count(a => a.IsCurrent && !a.IsDeleted) }), cancellationToken);
        if (l.Count == 0) return Result.Failure(Error.NotFound("Licence not found."));
        if (!l[0].IsActive) return Result.Failure(Error.BusinessRule("This licence is inactive."));
        if (l[0].ExpiryDate is { } e && e < _clock.UtcToday) return Result.Failure(Error.BusinessRule("This licence has expired; renew it before allocating seats."));
        if (l[0].TotalSeats is { } seats && l[0].Used >= seats) return Result.Failure(Error.BusinessRule($"All seats are in use ({seats} of {seats}). Release a seat or add seats to the licence."));
        if (await _uow.Repository<SoftwareLicenseAllocation>().AnyAsync(a => a.SoftwareLicenseId == licenseId && a.IsCurrent
                && ((assetId != null && a.AssetId == assetId) || (employeeId != null && a.EmployeeId == employeeId)), cancellationToken))
            return Result.Failure(Error.Conflict("This asset or person already has a seat of this licence."));
        await _uow.Repository<SoftwareLicenseAllocation>().AddAsync(new SoftwareLicenseAllocation
        {
            SoftwareLicenseId = licenseId, AssetId = assetId, EmployeeId = employeeId, AllocatedDate = _clock.UtcNow, IsCurrent = true, Notes = Text.Clean(notes)
        }, cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (BusinessRuleException ex) // seat-limit trigger (race with another user)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<Result> RevokeAsync(int allocationId, CancellationToken cancellationToken = default)
    {
        var a = await _uow.Repository<SoftwareLicenseAllocation>().FirstOrDefaultAsync(x => x.SoftwareLicenseAllocationId == allocationId, cancellationToken);
        if (a is null) return Result.Failure(Error.NotFound("Allocation not found."));
        if (!a.IsCurrent) return Result.Failure(Error.BusinessRule("Already revoked."));
        a.IsCurrent = false;
        a.RevokedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
