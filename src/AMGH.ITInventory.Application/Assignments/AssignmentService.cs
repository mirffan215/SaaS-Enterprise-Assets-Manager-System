using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Assignments;

public sealed record CurrentAssignment(int AssetAssignmentId, string? EmployeeName, string? EmployeeNumber, string? DepartmentName,
    string AssignmentType, DateTime AssignedDateUtc, DateOnly? ExpectedReturnDate, bool Acknowledged, DateTime? AcknowledgedUtc, string AssignedBy)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public sealed record AssignmentContext(int AssetId, string AssetNumber, string AssetName, string StatusCode, string StatusName,
    bool CanAssign, CurrentAssignment? Current, IReadOnlyList<LookupOption> Employees, IReadOnlyList<LookupOption> Departments,
    IReadOnlyList<LookupOption> ReturnStatuses);

public sealed class AssignForm
{
    public int? EmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public AssignmentType AssignmentType { get; set; } = AssignmentType.Permanent;
    public DateOnly? ExpectedReturnDate { get; set; }
    public string? Condition { get; set; }
    public string? Notes { get; set; }
}

public sealed class ReturnForm
{
    public string? Condition { get; set; }
    public string ReturnStatusCode { get; set; } = AssetStatusCodes.InStock;
    public string? Notes { get; set; }
}

public sealed record AssignmentHistoryItem(int AssetAssignmentId, string? EmployeeName, string? EmployeeNumber, string? DepartmentName,
    string AssignmentType, DateTime AssignedDateUtc, string AssignedBy, string? ConditionAtAssignment, DateTime? ReturnedDateUtc,
    string? ReturnedTo, string? ConditionAtReturn, bool Acknowledged, bool IsCurrent)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public sealed record HandoverDocument(string AssetNumber, string AssetName, string CategoryName, string? Manufacturer, string? Model,
    string? SerialNumber, string? Hostname, string? EmployeeName, string? EmployeeNumber, string? JobTitle, string? DepartmentName,
    string AssignmentType, DateTime AssignedDateUtc, DateOnly? ExpectedReturnDate, string? Condition, string AssignedBy, int AssetAssignmentId)
{
    /// <summary>Document number (Release 2.13).</summary>
    public string? DocumentNumber { get; init; }
}

public interface IAssignmentService
{
    Task<AssignmentContext?> GetContextAsync(int assetId, CancellationToken cancellationToken = default);
    Task<Result<int>> AssignAsync(int assetId, AssignForm form, CancellationToken cancellationToken = default);
    Task<Result> ReturnAsync(int assetId, ReturnForm form, CancellationToken cancellationToken = default);
    Task<Result> AcknowledgeAsync(int assetId, int assetAssignmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssignmentHistoryItem>> GetHistoryAsync(int assetId, CancellationToken cancellationToken = default);
    Task<HandoverDocument?> GetHandoverAsync(int assetId, int assetAssignmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hand-over of assets to custodians (ISO 27001 A.5.9-A.5.11). The database procedures enforce the rules
/// (assignable status, one custodian at a time, terminated employees excluded) and write the history.
/// </summary>
internal sealed class AssignmentService : IAssignmentService
{
    private readonly Documents.IDocumentNumbers _docs;
    public static readonly IReadOnlyList<string> ReturnStatusCodes =
        new[] { AssetStatusCodes.InStock, AssetStatusCodes.UnderMaintenance, AssetStatusCodes.Retired };

    private readonly IUnitOfWork _uow;
    private readonly IAssetLifecycleProcedures _procedures;
    private readonly ILookupService _lookups;
    private readonly IDateTimeProvider _clock;
    private readonly Notifications.INotificationService _notify;
    private readonly ICurrentUserService _user;

    public AssignmentService(IUnitOfWork uow, IAssetLifecycleProcedures procedures, ILookupService lookups, IDateTimeProvider clock,
        Notifications.INotificationService notify, ICurrentUserService user, Documents.IDocumentNumbers docs)
    {
        _docs = docs;
        _uow = uow;
        _procedures = procedures;
        _lookups = lookups;
        _clock = clock;
        _notify = notify;
        _user = user;
    }

    public async Task<AssignmentContext?> GetContextAsync(int assetId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var rows = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId).Select(a => new
        {
            a.AssetId, a.AssetNumber, a.AssetName, a.AssetStatus.StatusCode, a.AssetStatus.StatusName, a.AssetStatus.IsAssignable
        }), ct);
        if (rows.Count == 0) return null;
        var a = rows[0];
        var current = await CurrentAsync(assetId, ct);
        var statuses = (await _lookups.GetOptionsAsync(LookupKeys.Statuses, ct)).Where(s => ReturnStatusCodes.Contains(s.Code ?? "")).ToList();
        return new AssignmentContext(a.AssetId, a.AssetNumber, a.AssetName, a.StatusCode, a.StatusName, a.IsAssignable && current is null,
            current, await _lookups.GetOptionsAsync(LookupKeys.Employees, ct), await _lookups.GetOptionsAsync(LookupKeys.Departments, ct), statuses);
    }

    public async Task<Result<int>> AssignAsync(int assetId, AssignForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var errors = new List<Error>();
        if (form.EmployeeId is null && form.DepartmentId is null)
            errors.Add(Error.Validation("Choose the person receiving the asset (or a department for shared equipment).", nameof(form.EmployeeId)));
        var today = _clock.UtcToday;
        if (form.AssignmentType != AssignmentType.Permanent)
        {
            if (form.ExpectedReturnDate is null)
                errors.Add(Error.Validation("Enter the date the asset must be returned.", nameof(form.ExpectedReturnDate)));
            else if (form.ExpectedReturnDate < today || form.ExpectedReturnDate > today.AddYears(2))
                errors.Add(Error.Validation("The return date must be between today and two years from now.", nameof(form.ExpectedReturnDate)));
        }
        if (form.Condition?.Length > 200) errors.Add(Error.Validation("Condition can be up to 200 characters.", nameof(form.Condition)));
        if (form.Notes?.Length > 1000) errors.Add(Error.Validation("Notes can be up to 1000 characters.", nameof(form.Notes)));
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());

        try
        {
            var id = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var newId = await _procedures.AssignAsync(new AssignAssetRequest(assetId, form.EmployeeId, form.DepartmentId, null, form.AssignmentType,
                    form.AssignmentType == AssignmentType.Permanent ? null : form.ExpectedReturnDate,
                    Clean(form.Condition), Clean(form.Notes)), ct);
                await _docs.AssignAsync(Documents.DocumentTypes.Assignment, newId, ct);
                return newId;
            }, cancellationToken);
            var dept = form.EmployeeId is null && form.DepartmentId is { } d
                ? (await _uow.Repository<Department>().QueryAsync(q => q.Where(x => x.DepartmentId == d).Select(x => x.DepartmentName), cancellationToken)).FirstOrDefault()
                : null;
            await Notifications.WorkflowNotifications.AssignedAsync(_notify, _uow, assetId, form.EmployeeId, dept, form.AssignmentType, _user.UserName, cancellationToken);
            return Result<int>.Success(id);
        }
        catch (BusinessRuleException ex)
        {
            return Result<int>.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<Result> ReturnAsync(int assetId, ReturnForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!ReturnStatusCodes.Contains(form.ReturnStatusCode))
            return Result.Failure(Error.Validation("Choose what happens to the asset after return.", nameof(form.ReturnStatusCode)));
        if (form.Condition?.Length > 200) return Result.Failure(Error.Validation("Condition can be up to 200 characters.", nameof(form.Condition)));
        // the custodian is read before the return: afterwards the asset no longer has one
        var custodian = (await _uow.Repository<AssetAssignment>().QueryAsync(q => q.Where(x => x.AssetId == assetId && x.IsCurrent)
            .Select(x => x.EmployeeId), cancellationToken)).FirstOrDefault();
        try
        {
            await _procedures.ReturnAsync(new ReturnAssetRequest(assetId, Clean(form.Condition), form.ReturnStatusCode, Clean(form.Notes)), cancellationToken);
            await Notifications.WorkflowNotifications.ReturnedAsync(_notify, _uow, assetId, custodian, Clean(form.Condition), cancellationToken);
            return Result.Success();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<Result> AcknowledgeAsync(int assetId, int assetAssignmentId, CancellationToken cancellationToken = default)
    {
        if (!await _uow.Repository<AssetAssignment>().AnyAsync(x => x.AssetAssignmentId == assetAssignmentId && x.AssetId == assetId, cancellationToken))
            return Result.Failure(Error.NotFound("Assignment not found."));
        try
        {
            await _procedures.AcknowledgeAssignmentAsync(assetAssignmentId, cancellationToken);
            return Result.Success();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(Error.BusinessRule(ex.Message));
        }
    }

    public async Task<IReadOnlyList<AssignmentHistoryItem>> GetHistoryAsync(int assetId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<AssetAssignment>().QueryAsync(q => q.Where(x => x.AssetId == assetId)
            .OrderByDescending(x => x.AssignedDate).ThenByDescending(x => x.AssetAssignmentId)
            .Select(x => new AssignmentHistoryItem(x.AssetAssignmentId,
                x.Employee == null ? null : x.Employee.FirstName + " " + x.Employee.LastName,
                x.Employee == null ? null : x.Employee.EmployeeNumber,
                x.Department == null ? null : x.Department.DepartmentName,
                x.AssignmentType.ToString(), x.AssignedDate, x.AssignedBy, x.ConditionAtAssignment, x.ReturnedDate, x.ReturnedTo,
                x.ConditionAtReturn, x.AcknowledgementReceived, x.IsCurrent) { DocumentNumber = x.DocumentNumber }), cancellationToken);

    public async Task<HandoverDocument?> GetHandoverAsync(int assetId, int assetAssignmentId, CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<AssetAssignment>().QueryAsync(q => q
            .Where(x => x.AssetAssignmentId == assetAssignmentId && x.AssetId == assetId)
            .Select(x => new HandoverDocument(x.Asset.AssetNumber, x.Asset.AssetName, x.Asset.AssetCategory.CategoryName,
                x.Asset.Manufacturer == null ? null : x.Asset.Manufacturer.ManufacturerName, x.Asset.ModelName, x.Asset.SerialNumber,
                x.Asset.Hostname,
                x.Employee == null ? null : x.Employee.FirstName + " " + x.Employee.LastName,
                x.Employee == null ? null : x.Employee.EmployeeNumber,
                x.Employee == null ? null : x.Employee.JobTitle,
                x.Department == null ? null : x.Department.DepartmentName,
                x.AssignmentType.ToString(), x.AssignedDate, x.ExpectedReturnDate, x.ConditionAtAssignment, x.AssignedBy,
                x.AssetAssignmentId) { DocumentNumber = x.DocumentNumber }), cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<CurrentAssignment?> CurrentAsync(int assetId, CancellationToken ct) =>
        (await _uow.Repository<AssetAssignment>().QueryAsync(q => q.Where(x => x.AssetId == assetId && x.IsCurrent)
            .Select(x => new CurrentAssignment(x.AssetAssignmentId,
                x.Employee == null ? null : x.Employee.FirstName + " " + x.Employee.LastName,
                x.Employee == null ? null : x.Employee.EmployeeNumber,
                x.Department == null ? null : x.Department.DepartmentName,
                x.AssignmentType.ToString(), x.AssignedDate, x.ExpectedReturnDate, x.AcknowledgementReceived, x.AcknowledgementDate,
                x.AssignedBy) { DocumentNumber = x.DocumentNumber }), ct)).FirstOrDefault();

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
