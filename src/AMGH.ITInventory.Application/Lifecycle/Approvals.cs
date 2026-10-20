using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.CompanyAdmin;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Application.Lifecycle;

public static class RequestTypes
{
    public const string Movement = "Movement";
    public const string Disposal = "Disposal";
    /// <summary>Full export of the company's data: needs a second administrator (script 19).</summary>
    public const string DataExport = "DataExport";
    public static readonly IReadOnlyList<string> All = new[] { Movement, Disposal, DataExport };
    public static string ApprovePermission(string type) => type switch
    {
        Movement => Permissions.Movements.Approve,
        DataExport => Permissions.CompanyAdmin.ExportDataApprove,
        _ => Permissions.Disposals.Approve
    };
}

/// <summary>Who is acting: taken from the signed-in session by the web layer.</summary>
public sealed record ApprovalActor(string UserName, IReadOnlySet<string> Roles, IReadOnlySet<string> Permissions);

public sealed record ApprovalStepItem(byte StageNo, string? ApproverRole, string Decision, string DecidedBy, DateTime DecidedUtc, string? Remarks);

/// <summary>A request waiting for a decision, with the stage it is at and whether the current user may decide it.</summary>
public sealed record PendingApproval(string RequestType, int RequestId, int AssetId, string AssetNumber, string AssetName, string Summary,
    string Reason, string RequestedBy, DateTime RequestedUtc, int Stage, int TotalStages, string? StageRole, bool CanDecide, string? WhyNot,
    IReadOnlyList<ApprovalStepItem> Steps)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public sealed record ApprovalRuleItem(string RequestType, byte StageNo, string ApproverRole);

public interface IApprovalService
{
    Task<IReadOnlyList<PendingApproval>> PendingAsync(ApprovalActor actor, CancellationToken cancellationToken = default);
    Task<int> CountActionableAsync(ApprovalActor actor, CancellationToken cancellationToken = default);
    Task<Result<string>> ApproveAsync(string requestType, int requestId, string? remarks, ApprovalActor actor, CancellationToken cancellationToken = default);
    Task<Result> RejectAsync(string requestType, int requestId, string reason, ApprovalActor actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalStepItem>> StepsAsync(string requestType, int requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalRuleItem>> RulesAsync(CancellationToken cancellationToken = default);
    /// <summary>Replaces the stages of a request type (empty list = single stage, any approver).</summary>
    Task<Result> SaveRulesAsync(string requestType, IReadOnlyList<string> stageRoles, CancellationToken cancellationToken = default);
    /// <summary>
    /// Opens a full-data-export request: it waits in the Approval Center for a second administrator and only then can be
    /// downloaded. Returns the request id.
    /// </summary>
    Task<Result<int>> RequestDataExportAsync(string reason, CancellationToken cancellationToken = default);
}

internal sealed class ApprovalService : IApprovalService
{
    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _user;

    private readonly Notifications.INotificationService _notify;
    private readonly Abstractions.Mail.ISecretProtector _protector;
    private readonly Microsoft.Extensions.Options.IOptions<Tenancy.TenancyOptions> _tenancy;

    public ApprovalService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, IDateTimeProvider clock, ICurrentUserService user,
        Notifications.INotificationService notify, Abstractions.Mail.ISecretProtector protector, Microsoft.Extensions.Options.IOptions<Tenancy.TenancyOptions> tenancy)
    {
        _protector = protector;
        _tenancy = tenancy;
        _uow = uow;
        _procedures = procedures;
        _clock = clock;
        _user = user;
        _notify = notify;
    }

    public async Task<IReadOnlyList<PendingApproval>> PendingAsync(ApprovalActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var ct = cancellationToken;
        var rules = await RulesAsync(ct);
        var movements = await _uow.Repository<AssetMovement>().QueryAsync(q => q.Where(m => m.MovementStatus == WorkflowStatus.Requested)
            .OrderBy(m => m.RequestedDate).Select(m => new
            {
                Id = m.AssetMovementId, Doc = m.DocumentNumber, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.MovementType, m.MovementReason, m.RequestedBy, m.RequestedDate,
                To = m.ToLocation.LocationName, ToB = m.ToBuilding == null ? null : m.ToBuilding.BuildingName, ToR = m.ToRoom == null ? null : m.ToRoom.RoomName
            }), ct);
        var disposals = await _uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(d => d.DisposalStatus == WorkflowStatus.Requested)
            .OrderBy(d => d.RequestedDate).Select(d => new
            {
                Id = d.AssetDisposalId, Doc = d.DocumentNumber, d.AssetId, d.Asset.AssetNumber, d.Asset.AssetName, Method = d.DisposalMethod.MethodName, d.Reason, d.RequestedBy, d.RequestedDate
            }), ct);
        var exports = await _uow.Repository<DataExportRequest>().QueryAsync(q => q.Where(x => x.Status == DataExportStatuses.Requested)
            .OrderBy(x => x.RequestedDate).Select(x => new { Id = x.DataExportRequestId, x.Reason, x.RequestedBy, x.RequestedDate }), ct);
        // only the decisions of requests that are still pending (not the whole history): this runs on every page (menu badge)
        var pendingMoves = movements.Select(m => m.Id).ToList();
        var pendingDisposals = disposals.Select(d => d.Id).ToList();
        var pendingExports = exports.Select(x => x.Id).ToList();
        var steps = (await _uow.Repository<ApprovalStep>().QueryAsync(q => q.Where(s => s.Decision == "Approved"
                && ((s.RequestType == RequestTypes.Movement && pendingMoves.Contains(s.RequestId))
                    || (s.RequestType == RequestTypes.Disposal && pendingDisposals.Contains(s.RequestId))
                    || (s.RequestType == RequestTypes.DataExport && pendingExports.Contains(s.RequestId))))
            .Select(s => new { s.RequestType, s.RequestId, s.StageNo, s.ApproverRole, s.Decision, s.DecidedBy, s.DecidedDate, s.Remarks }), ct))
            .GroupBy(s => (s.RequestType, s.RequestId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ApprovalStepItem>)g.OrderBy(s => s.StageNo)
                .Select(s => new ApprovalStepItem(s.StageNo, s.ApproverRole, s.Decision, s.DecidedBy, s.DecidedDate, s.Remarks)).ToList());

        var result = new List<PendingApproval>();
        foreach (var m in movements)
        {
            var done = steps.GetValueOrDefault((RequestTypes.Movement, m.Id)) ?? Array.Empty<ApprovalStepItem>();
            var summary = $"{Common.DisplayText.Humanize(m.MovementType.ToString())} to {string.Join(" / ", new[] { m.To, m.ToB, m.ToR }.Where(x => x is not null))}";
            result.Add(Build(RequestTypes.Movement, m.Id, m.AssetId, m.AssetNumber, m.AssetName, summary, m.MovementReason, m.RequestedBy, m.RequestedDate, done, rules, actor) with { DocumentNumber = m.Doc });
        }
        foreach (var d in disposals)
        {
            var done = steps.GetValueOrDefault((RequestTypes.Disposal, d.Id)) ?? Array.Empty<ApprovalStepItem>();
            result.Add(Build(RequestTypes.Disposal, d.Id, d.AssetId, d.AssetNumber, d.AssetName, "Disposal: " + d.Method, d.Reason, d.RequestedBy, d.RequestedDate, done, rules, actor) with { DocumentNumber = d.Doc });
        }
        foreach (var x in exports)
        {
            var done = steps.GetValueOrDefault((RequestTypes.DataExport, x.Id)) ?? Array.Empty<ApprovalStepItem>();
            result.Add(Build(RequestTypes.DataExport, x.Id, 0, string.Empty, "Company data", "Full export of all company data (encrypted archive)", x.Reason, x.RequestedBy, x.RequestedDate, done, rules, actor));
        }
        return result;
    }

    public async Task<int> CountActionableAsync(ApprovalActor actor, CancellationToken cancellationToken = default) =>
        (await PendingAsync(actor, cancellationToken)).Count(p => p.CanDecide);

    private static PendingApproval Build(string type, int id, int assetId, string number, string name, string summary, string reason, string requestedBy,
        DateTime requested, IReadOnlyList<ApprovalStepItem> done, IReadOnlyList<ApprovalRuleItem> rules, ApprovalActor actor)
    {
        var stages = rules.Where(r => r.RequestType == type).OrderBy(r => r.StageNo).ToList();
        var total = Math.Max(1, stages.Count);
        var stage = Math.Min(done.Count + 1, total);
        var role = stages.Count >= stage ? stages[stage - 1].ApproverRole : null;
        var why = WhyCannotDecide(type, requestedBy, role, done, actor);
        return new PendingApproval(type, id, assetId, number, name, summary, reason, requestedBy, requested, stage, total, role, why is null, why, done);
    }

    /// <summary>Segregation of duties and stage roles. Null = the actor may decide.</summary>
    private static string? WhyCannotDecide(string type, string requestedBy, string? stageRole, IReadOnlyList<ApprovalStepItem> done, ApprovalActor actor)
    {
        if (!actor.Permissions.Contains(RequestTypes.ApprovePermission(type))) return "You do not have the approval permission.";
        if (string.Equals(requestedBy, actor.UserName, StringComparison.OrdinalIgnoreCase)) return "You requested this; another person must approve it.";
        if (done.Any(s => string.Equals(s.DecidedBy, actor.UserName, StringComparison.OrdinalIgnoreCase))) return "You approved an earlier stage; another person must approve this one.";
        if (stageRole is not null && !actor.Roles.Contains(stageRole)) return $"This stage must be approved by someone with the role \"{Common.DisplayText.Humanize(stageRole)}\".";
        return null;
    }

    public async Task<Result<string>> ApproveAsync(string requestType, int requestId, string? remarks, ApprovalActor actor, CancellationToken cancellationToken = default)
    {
        var item = (await PendingAsync(actor, cancellationToken)).FirstOrDefault(p => p.RequestType == requestType && p.RequestId == requestId);
        if (item is null) return Result<string>.Failure(Error.NotFound("This request is no longer waiting for approval."));
        if (!item.CanDecide) return Result<string>.Failure(Error.Forbidden(item.WhyNot!));
        if (remarks?.Length > 1000) return Result<string>.Failure(Error.Validation("Remarks can be up to 1000 characters.", "remarks"));
        try
        {
            var final = item.Stage >= item.TotalStages;
            await _uow.ExecuteInTransactionAsync(async ct =>
            {
                await _uow.Repository<ApprovalStep>().AddAsync(new ApprovalStep
                {
                    RequestType = requestType, RequestId = requestId, StageNo = (byte)item.Stage, ApproverRole = item.StageRole, Decision = "Approved",
                    DecidedBy = _user.UserName, DecidedDate = _clock.UtcNow, Remarks = Clean(remarks)
                }, ct);
                await _uow.SaveChangesAsync(ct);
                if (!final) return;
                if (requestType == RequestTypes.Movement) await _procedures.ApproveMovementAsync(requestId, ct);
                else if (requestType == RequestTypes.DataExport) await ApproveExportAsync(requestId, ct);
                else await _procedures.ApproveDisposalAsync(requestId, ct);
            }, cancellationToken);
            if (final && requestType != RequestTypes.DataExport)
                await Notifications.WorkflowNotifications.DecidedAsync(_notify, _uow, requestType, requestId, item.AssetId, item.RequestedBy, true, _user.UserName, Clean(remarks), cancellationToken);
            return Result<string>.Success(final ? "Approved." : $"Stage {item.Stage} of {item.TotalStages} approved; waiting for stage {item.Stage + 1}.");
        }
        catch (BusinessRuleException ex)
        {
            return Result<string>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<Result> RejectAsync(string requestType, int requestId, string reason, ApprovalActor actor, CancellationToken cancellationToken = default)
    {
        var item = (await PendingAsync(actor, cancellationToken)).FirstOrDefault(p => p.RequestType == requestType && p.RequestId == requestId);
        if (item is null) return Result.Failure(Error.NotFound("This request is no longer waiting for approval."));
        if (!item.CanDecide) return Result.Failure(Error.Forbidden(item.WhyNot!));
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 500) return Result.Failure(Error.Validation("Give the reason for rejecting (5-500 characters).", "reason"));
        try
        {
            await _uow.ExecuteInTransactionAsync(async ct =>
            {
                await _uow.Repository<ApprovalStep>().AddAsync(new ApprovalStep
                {
                    RequestType = requestType, RequestId = requestId, StageNo = (byte)item.Stage, ApproverRole = item.StageRole, Decision = "Rejected",
                    DecidedBy = _user.UserName, DecidedDate = _clock.UtcNow, Remarks = reason
                }, ct);
                await _uow.SaveChangesAsync(ct);
                if (requestType == RequestTypes.Movement) await _procedures.RejectMovementAsync(requestId, reason, ct);
                else if (requestType == RequestTypes.DataExport) await RejectExportAsync(requestId, reason, ct);
                else await _procedures.RejectDisposalAsync(requestId, reason, ct);
            }, cancellationToken);
            if (requestType != RequestTypes.DataExport)
                await Notifications.WorkflowNotifications.DecidedAsync(_notify, _uow, requestType, requestId, item.AssetId, item.RequestedBy, false, _user.UserName, reason, cancellationToken);
            return Result.Success();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<IReadOnlyList<ApprovalStepItem>> StepsAsync(string requestType, int requestId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<ApprovalStep>().QueryAsync(q => q.Where(s => s.RequestType == requestType && s.RequestId == requestId)
            .OrderBy(s => s.ApprovalStepId)
            .Select(s => new ApprovalStepItem(s.StageNo, s.ApproverRole, s.Decision, s.DecidedBy, s.DecidedDate, s.Remarks)), cancellationToken);

    public async Task<IReadOnlyList<ApprovalRuleItem>> RulesAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<ApprovalRule>().QueryAsync(q => q.OrderBy(r => r.RequestType).ThenBy(r => r.StageNo)
            .Select(r => new ApprovalRuleItem(r.RequestType, r.StageNo, r.ApproverRole)), cancellationToken);

    public async Task<Result> SaveRulesAsync(string requestType, IReadOnlyList<string> stageRoles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stageRoles);
        if (!RequestTypes.All.Contains(requestType)) return Result.Failure(Error.Validation("Unknown request type."));
        var roles = stageRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        if (roles.Count > 5) return Result.Failure(Error.Validation("At most 5 approval stages."));
        var known = (await _uow.Repository<Role>().QueryAsync(q => q.Where(r => r.IsActive).Select(r => r.RoleName), cancellationToken)).ToHashSet();
        var unknown = roles.FirstOrDefault(r => !known.Contains(r));
        if (unknown is not null) return Result.Failure(Error.Validation($"Unknown role '{unknown}'."));
        var pending = requestType switch
        {
            RequestTypes.Movement => await _uow.Repository<AssetMovement>().AnyAsync(m => m.MovementStatus == WorkflowStatus.Requested, cancellationToken),
            RequestTypes.DataExport => await _uow.Repository<DataExportRequest>().AnyAsync(x => x.Status == DataExportStatuses.Requested, cancellationToken),
            _ => await _uow.Repository<AssetDisposal>().AnyAsync(d => d.DisposalStatus == WorkflowStatus.Requested, cancellationToken)
        };
        if (pending) return Result.Failure(Error.BusinessRule("Decide the pending requests of this type first: changing stages mid-way would be unclear."));

        await _uow.ExecuteInTransactionAsync(async ct =>
        {
            var repo = _uow.Repository<ApprovalRule>();
            foreach (var r in await repo.ListAsync(x => x.RequestType == requestType, ct)) repo.Remove(r);
            await _uow.SaveChangesAsync(ct);
            for (var i = 0; i < roles.Count; i++)
                await repo.AddAsync(new ApprovalRule { RequestType = requestType, StageNo = (byte)(i + 1), ApproverRole = roles[i], ModifiedBy = _user.UserName, ModifiedDate = _clock.UtcNow }, ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- full data export (second-administrator approval)
    public async Task<Result<int>> RequestDataExportAsync(string reason, CancellationToken cancellationToken = default)
    {
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 10 or > 500) return Result<int>.Failure(Error.Validation("Explain why the data is needed (10-500 characters).", "reason"));
        // a person has one open request at a time
        if (await _uow.Repository<DataExportRequest>().AnyAsync(x => x.RequestedBy == _user.UserName
                && (x.Status == DataExportStatuses.Requested || x.Status == DataExportStatuses.Approved), cancellationToken))
            return Result<int>.Failure(Error.Conflict("You already have an export that is waiting for approval or ready to download."));
        var request = new DataExportRequest { Reason = reason, RequestedBy = _user.UserName, RequestedDate = _clock.UtcNow, Status = DataExportStatuses.Requested };
        if (!_tenancy.Value.ExportRequiresApproval)
        {
            // self-service: the company administrator may take the export right away; it is still recorded (who, why, when) and downloads once
            request.Status = DataExportStatuses.Approved;
            request.ApprovedBy = "(self-service)";
            request.ApprovedDate = _clock.UtcNow;
            ExportCredentialFactory.Issue(request, _clock.UtcNow, _protector);
        }
        await _uow.Repository<DataExportRequest>().AddAsync(request, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(request.DataExportRequestId);
    }

    /// <summary>Final approval: the export becomes downloadable with a one-time token and an archive password (24 hours).</summary>
    private async Task ApproveExportAsync(int requestId, CancellationToken ct)
    {
        var request = await _uow.Repository<DataExportRequest>().FirstOrDefaultAsync(x => x.DataExportRequestId == requestId, ct)
            ?? throw new BusinessRuleException("The export request no longer exists.");
        request.Status = DataExportStatuses.Approved;
        request.ApprovedBy = _user.UserName;
        request.ApprovedDate = _clock.UtcNow;
        ExportCredentialFactory.Issue(request, _clock.UtcNow, _protector);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task RejectExportAsync(int requestId, string reason, CancellationToken ct)
    {
        var request = await _uow.Repository<DataExportRequest>().FirstOrDefaultAsync(x => x.DataExportRequestId == requestId, ct)
            ?? throw new BusinessRuleException("The export request no longer exists.");
        request.Status = DataExportStatuses.Rejected;
        request.RejectionReason = reason;
        await _uow.SaveChangesAsync(ct);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

// ============================================================================ movements

public sealed class MovementForm
{
    public int? ToLocationId { get; set; }
    public int? ToBuildingId { get; set; }
    public int? ToFloorId { get; set; }
    public int? ToRoomId { get; set; }
    public MovementType MovementType { get; set; } = MovementType.Relocation;
    public string? Reason { get; set; }
    public string? ChangeRequestNumber { get; set; }
}

public sealed record MovementItem(int AssetMovementId, int AssetId, string AssetNumber, string AssetName, string MovementType, string Status,
    string? From, string To, string Reason, string? ChangeRequest, string RequestedBy, DateTime RequestedUtc, string? ApprovedBy,
    DateTime? ApprovedUtc, DateTime? CompletedUtc, string? RejectionReason)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public interface IMovementService
{
    Task<Result<int>> RequestAsync(int assetId, MovementForm form, CancellationToken cancellationToken = default);
    Task<Result> CompleteAsync(int movementId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MovementItem>> ListAsync(int? assetId, WorkflowStatus? status, int max = 300, CancellationToken cancellationToken = default);
}

internal sealed class MovementService : IMovementService
{
    private readonly Documents.IDocumentNumbers _docs;
    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly Notifications.INotificationService _notify;
    private readonly ICurrentUserService _user;

    public MovementService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, Notifications.INotificationService notify, ICurrentUserService user, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _procedures = procedures;
        _notify = notify;
        _user = user;
    }

    public async Task<Result<int>> RequestAsync(int assetId, MovementForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<Error>();
        if (form.ToLocationId is null) errors.Add(Error.Validation("Choose the destination location.", nameof(form.ToLocationId)));
        var reason = form.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500) errors.Add(Error.Validation("Give the reason for the move (5-500 characters).", nameof(form.Reason)));
        if (form.ChangeRequestNumber?.Trim().Length > 50) errors.Add(Error.Validation("Change request number: up to 50 characters.", nameof(form.ChangeRequestNumber)));
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());
        try
        {
            // the request and its document number in one transaction: a failed request never consumes a number
            var id = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var newId = await _procedures.RequestMovementAsync(new RequestMovementRequest(assetId, form.ToLocationId!.Value, form.ToBuildingId, form.ToFloorId,
                    form.ToRoomId, form.MovementType, reason, string.IsNullOrWhiteSpace(form.ChangeRequestNumber) ? null : form.ChangeRequestNumber.Trim()), ct);
                await _docs.AssignAsync(Documents.DocumentTypes.Movement, newId, ct);
                return newId;
            }, cancellationToken);
            var to = (await _uow.Repository<Location>().QueryAsync(q => q.Where(l => l.LocationId == form.ToLocationId).Select(l => l.LocationName), cancellationToken)).FirstOrDefault();
            await Notifications.WorkflowNotifications.ApprovalRequiredAsync(_notify, _uow, RequestTypes.Movement, id, assetId,
                $"{Common.DisplayText.Humanize(form.MovementType.ToString())} to {to}", reason, _user.UserName, cancellationToken);
            return Result<int>.Success(id);
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException)
        {
            return Result<int>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<Result> CompleteAsync(int movementId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _procedures.CompleteMovementAsync(movementId, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<IReadOnlyList<MovementItem>> ListAsync(int? assetId, WorkflowStatus? status, int max = 300, CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<AssetMovement>().QueryAsync(q =>
        {
            if (assetId is { } a) q = q.Where(m => m.AssetId == a);
            if (status is { } s) q = q.Where(m => m.MovementStatus == s);
            return q.OrderByDescending(m => m.AssetMovementId).Take(max).Select(m => new
            {
                m.AssetMovementId, m.DocumentNumber, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.MovementType, m.MovementStatus,
                FromL = m.FromLocation == null ? null : m.FromLocation.LocationName, FromB = m.FromBuilding == null ? null : m.FromBuilding.BuildingName,
                FromR = m.FromRoom == null ? null : m.FromRoom.RoomName,
                ToL = m.ToLocation.LocationName, ToB = m.ToBuilding == null ? null : m.ToBuilding.BuildingName, ToR = m.ToRoom == null ? null : m.ToRoom.RoomName,
                m.MovementReason, m.ChangeRequestNumber, m.RequestedBy, m.RequestedDate, m.ApprovedBy, m.ApprovedDate, m.CompletedDate, m.RejectionReason
            });
        }, cancellationToken);
        static string? Join(params string?[] p) { var x = p.Where(v => v is not null).ToArray(); return x.Length == 0 ? null : string.Join(" / ", x); }
        return rows.Select(m => new MovementItem(m.AssetMovementId, m.AssetId, m.AssetNumber, m.AssetName, Common.DisplayText.Humanize(m.MovementType.ToString()),
            m.MovementStatus.ToString(), Join(m.FromL, m.FromB, m.FromR), Join(m.ToL, m.ToB, m.ToR)!, m.MovementReason, m.ChangeRequestNumber, m.RequestedBy,
            m.RequestedDate, m.ApprovedBy, m.ApprovedDate, m.CompletedDate, m.RejectionReason) { DocumentNumber = m.DocumentNumber }).ToList();
    }
}

// ============================================================================ disposals

public sealed class DisposalCompletionForm
{
    public DateOnly? DisposalDate { get; set; }
    public string? SanitizationMethod { get; set; }
    public DateOnly? SanitizationDate { get; set; }
    public string? SanitizationVerifiedBy { get; set; }
    public string? CertificateNumber { get; set; }
    public string? RecipientName { get; set; }
    public decimal? SaleValue { get; set; }
    public int? CurrencyId { get; set; }
    public string? Remarks { get; set; }
}

public sealed record DisposalItem(int AssetDisposalId, int AssetId, string AssetNumber, string AssetName, string Method, bool NeedsSanitization,
    bool NeedsCertificate, string Status, string Reason, string RequestedBy, DateTime RequestedUtc, string? ApprovedBy, DateTime? ApprovedUtc,
    DateOnly? DisposalDate, string? CertificateNumber, string? SanitizationMethod, string? RejectionReason)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public interface IDisposalService
{
    Task<Result<int>> RequestAsync(int assetId, int disposalMethodId, string reason, CancellationToken cancellationToken = default);
    Task<Result> CompleteAsync(int disposalId, DisposalCompletionForm form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DisposalItem>> ListAsync(int? assetId, WorkflowStatus? status, int max = 300, CancellationToken cancellationToken = default);
}

internal sealed class DisposalService : IDisposalService
{
    private readonly Documents.IDocumentNumbers _docs;
    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly IDateTimeProvider _clock;

    private readonly Notifications.INotificationService _notify;
    private readonly ICurrentUserService _user;

    public DisposalService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, IDateTimeProvider clock, Notifications.INotificationService notify, ICurrentUserService user, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _procedures = procedures;
        _clock = clock;
        _notify = notify;
        _user = user;
    }

    public async Task<Result<int>> RequestAsync(int assetId, int disposalMethodId, string reason, CancellationToken cancellationToken = default)
    {
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 500) return Result<int>.Failure(Error.Validation("Give the reason for disposal (5-500 characters).", "reason"));
        try
        {
            var id = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var newId = await _procedures.RequestDisposalAsync(new RequestDisposalRequest(assetId, disposalMethodId, reason), ct);
                await _docs.AssignAsync(Documents.DocumentTypes.Disposal, newId, ct);
                return newId;
            }, cancellationToken);
            var method = (await _uow.Repository<DisposalMethod>().QueryAsync(q => q.Where(m => m.DisposalMethodId == disposalMethodId).Select(m => m.MethodName), cancellationToken)).FirstOrDefault();
            await Notifications.WorkflowNotifications.ApprovalRequiredAsync(_notify, _uow, RequestTypes.Disposal, id, assetId, "Disposal: " + method, reason, _user.UserName, cancellationToken);
            return Result<int>.Success(id);
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException)
        {
            return Result<int>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    /// <summary>The method's evidence rules are checked here for clear field messages; the database enforces them again.</summary>
    public async Task<Result> CompleteAsync(int disposalId, DisposalCompletionForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var rows = await _uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(d => d.AssetDisposalId == disposalId)
            .Select(d => new { d.DisposalStatus, d.DisposalMethod.RequiresDataSanitization, d.DisposalMethod.RequiresCertificate }), cancellationToken);
        if (rows.Count == 0) return Result.Failure(Error.NotFound("Disposal not found."));
        var m = rows[0];
        var errors = new List<Error>();
        var today = _clock.UtcToday;
        if (form.DisposalDate is null || form.DisposalDate > today) errors.Add(Error.Validation("Enter the disposal date (not in the future).", nameof(form.DisposalDate)));
        if (m.RequiresDataSanitization)
        {
            if (string.IsNullOrWhiteSpace(form.SanitizationMethod)) errors.Add(Error.Validation("This method requires the data sanitisation method (e.g. NIST 800-88 purge).", nameof(form.SanitizationMethod)));
            if (form.SanitizationDate is null || form.SanitizationDate > today) errors.Add(Error.Validation("Enter the sanitisation date.", nameof(form.SanitizationDate)));
            if (string.IsNullOrWhiteSpace(form.SanitizationVerifiedBy)) errors.Add(Error.Validation("Enter who verified the sanitisation.", nameof(form.SanitizationVerifiedBy)));
        }
        if (m.RequiresCertificate && string.IsNullOrWhiteSpace(form.CertificateNumber))
            errors.Add(Error.Validation("This method requires the certificate number (destruction / recycling certificate).", nameof(form.CertificateNumber)));
        if (form.SaleValue is < 0) errors.Add(Error.Validation("Sale value cannot be negative.", nameof(form.SaleValue)));
        if (form.SaleValue is > 0 && form.CurrencyId is null) errors.Add(Error.Validation("Select the currency of the sale value.", nameof(form.CurrencyId)));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());
        try
        {
            await _procedures.CompleteDisposalAsync(new CompleteDisposalRequest(disposalId, form.DisposalDate!.Value, Clean(form.SanitizationMethod),
                form.SanitizationDate?.ToDateTime(TimeOnly.MinValue), Clean(form.SanitizationVerifiedBy), Clean(form.CertificateNumber),
                Clean(form.RecipientName), form.SaleValue, form.SaleValue is > 0 ? form.CurrencyId : null, Clean(form.Remarks)), cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is BusinessRuleException or NotFoundException)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<IReadOnlyList<DisposalItem>> ListAsync(int? assetId, WorkflowStatus? status, int max = 300, CancellationToken cancellationToken = default) =>
        await _uow.Repository<AssetDisposal>().QueryAsync(q =>
        {
            if (assetId is { } a) q = q.Where(d => d.AssetId == a);
            if (status is { } s) q = q.Where(d => d.DisposalStatus == s);
            return q.OrderByDescending(d => d.AssetDisposalId).Take(max).Select(d => new DisposalItem(d.AssetDisposalId, d.AssetId, d.Asset.AssetNumber,
                d.Asset.AssetName, d.DisposalMethod.MethodName, d.DisposalMethod.RequiresDataSanitization, d.DisposalMethod.RequiresCertificate,
                d.DisposalStatus.ToString(), d.Reason, d.RequestedBy, d.RequestedDate, d.ApprovedBy, d.ApprovedDate, d.DisposalDate,
                d.CertificateNumber, d.DataSanitizationMethod, d.RejectionReason) { DocumentNumber = d.DocumentNumber });
        }, cancellationToken);

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
