using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Application.Notifications;

/// <summary>Event notifications raised by the workflows (assign, return, approval requested, approval decided). Never throw.</summary>
internal static class WorkflowNotifications
{
    private sealed record AssetInfo(string Number, string Name);

    private static async Task<AssetInfo?> AssetAsync(IUnitOfWork uow, int assetId, CancellationToken ct) =>
        (await uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetId == assetId).Select(a => new AssetInfo(a.AssetNumber, a.AssetName)), ct)).FirstOrDefault();

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The document number of a movement or disposal request (empty when not numbered yet).</summary>
    private static async Task<string> DocumentNumberAsync(IUnitOfWork uow, string requestType, int requestId, CancellationToken ct) =>
        (requestType == "Movement"
            ? (await uow.Repository<AssetMovement>().QueryAsync(q => q.Where(m => m.AssetMovementId == requestId).Select(m => m.DocumentNumber), ct)).FirstOrDefault()
            : (await uow.Repository<AssetDisposal>().QueryAsync(q => q.Where(d => d.AssetDisposalId == requestId).Select(d => d.DocumentNumber), ct)).FirstOrDefault())
        ?? string.Empty;

    public static async Task AssignedAsync(INotificationService n, IUnitOfWork uow, int assetId, int? employeeId, string? departmentName,
        AssignmentType type, string assignedBy, CancellationToken ct)
    {
        try
        {
            var asset = await AssetAsync(uow, assetId, ct);
            if (asset is null) return;
            var rule = NotificationCatalog.Find(NotificationCatalog.AssetAssigned)!;
            var recipients = (await n.UsersInRolesAsync((rule.DefaultRoles ?? "").Split(','), ct)).ToList();
            var custodian = await n.EmployeeAsync(employeeId, ct);
            if (custodian is not null) recipients.Add(custodian);
            await n.TryNotifyAsync(new NotificationRequest(NotificationCatalog.AssetAssigned, new Dictionary<string, string>
            {
                ["AssetNumber"] = asset.Number, ["AssetName"] = asset.Name, ["Custodian"] = custodian?.Name ?? departmentName ?? "a department",
                ["AssignedBy"] = assignedBy, ["AssignmentType"] = Common.DisplayText.Humanize(type.ToString())
            }, recipients, NotificationSeverity.Info, "Asset", Id(assetId), $"/Assets/Details/{assetId}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }

    public static async Task ReturnedAsync(INotificationService n, IUnitOfWork uow, int assetId, int? employeeId, string? condition, CancellationToken ct)
    {
        try
        {
            var asset = await AssetAsync(uow, assetId, ct);
            if (asset is null) return;
            var rule = NotificationCatalog.Find(NotificationCatalog.AssetReturned)!;
            var recipients = (await n.UsersInRolesAsync((rule.DefaultRoles ?? "").Split(','), ct)).ToList();
            var custodian = await n.EmployeeAsync(employeeId, ct);
            if (custodian is not null) recipients.Add(custodian);
            await n.TryNotifyAsync(new NotificationRequest(NotificationCatalog.AssetReturned, new Dictionary<string, string>
            {
                ["AssetNumber"] = asset.Number, ["AssetName"] = asset.Name, ["Custodian"] = custodian?.Name ?? "the custodian", ["Condition"] = condition ?? "not recorded"
            }, recipients, NotificationSeverity.Info, "Asset", Id(assetId), $"/Assets/Details/{assetId}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }

    /// <summary>To everyone who may approve requests of this type (the requester is excluded).</summary>
    public static async Task ApprovalRequiredAsync(INotificationService n, IUnitOfWork uow, string requestType, int requestId, int assetId,
        string summary, string reason, string requestedBy, CancellationToken ct)
    {
        try
        {
            var asset = await AssetAsync(uow, assetId, ct);
            if (asset is null) return;
            var permission = requestType == "Movement" ? Permissions.Movements.Approve : Permissions.Disposals.Approve;
            var approvers = (await n.UsersWithPermissionAsync(permission, ct)).ToList();
            var requester = await n.UserByNameAsync(requestedBy, ct);
            if (requester?.UserId is { } rid) approvers.RemoveAll(a => a.UserId == rid);
            await n.TryNotifyAsync(new NotificationRequest(NotificationCatalog.ApprovalRequired, new Dictionary<string, string>
            {
                ["AssetNumber"] = asset.Number, ["AssetName"] = asset.Name, ["RequestType"] = requestType.ToLowerInvariant(), ["Summary"] = summary,
                ["RequestedBy"] = requestedBy, ["Reason"] = reason, ["DocumentNumber"] = await DocumentNumberAsync(uow, requestType, requestId, ct)
            }, approvers, NotificationSeverity.Warning, requestType, Id(requestId), "/approvals", $"approval:{requestType}:{requestId}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }

    public static async Task DecidedAsync(INotificationService n, IUnitOfWork uow, string requestType, int requestId, int assetId, string requestedBy,
        bool approved, string decidedBy, string? remarks, CancellationToken ct)
    {
        try
        {
            var asset = await AssetAsync(uow, assetId, ct);
            var requester = await n.UserByNameAsync(requestedBy, ct);
            if (asset is null || requester is null) return;
            await n.TryNotifyAsync(new NotificationRequest(NotificationCatalog.ApprovalDecided, new Dictionary<string, string>
            {
                ["AssetNumber"] = asset.Number, ["AssetName"] = asset.Name, ["RequestType"] = requestType.ToLowerInvariant(),
                ["Decision"] = approved ? "approved" : "rejected", ["DecidedBy"] = decidedBy, ["Remarks"] = remarks ?? string.Empty,
                ["DocumentNumber"] = await DocumentNumberAsync(uow, requestType, requestId, ct)
            }, new[] { requester }, approved ? NotificationSeverity.Info : NotificationSeverity.Warning, requestType, Id(requestId),
            $"/Assets/Details/{assetId}?tab=lifecycle", $"decided:{requestType}:{requestId}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
    }
}
