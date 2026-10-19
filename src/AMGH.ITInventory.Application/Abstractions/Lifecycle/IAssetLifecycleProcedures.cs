namespace AMGH.ITInventory.Application.Abstractions.Lifecycle;

/// <summary>
/// Asset state transitions executed by the database's stored procedures, which enforce the
/// lifecycle rules (assignable status, segregation of duties, disposal evidence, history).
/// The acting user is always taken from ICurrentUserService - never from the caller.
/// Rule violations surface as BusinessRuleException with a user-friendly message.
/// </summary>
public interface IAssetLifecycleProcedures
{
    Task<int> AssignAsync(AssignAssetRequest request, CancellationToken cancellationToken = default);
    Task ReturnAsync(ReturnAssetRequest request, CancellationToken cancellationToken = default);
    Task AcknowledgeAssignmentAsync(int assetAssignmentId, CancellationToken cancellationToken = default);

    Task<int> RequestMovementAsync(RequestMovementRequest request, CancellationToken cancellationToken = default);
    Task ApproveMovementAsync(int assetMovementId, CancellationToken cancellationToken = default);
    Task RejectMovementAsync(int assetMovementId, string reason, CancellationToken cancellationToken = default);
    Task CompleteMovementAsync(int assetMovementId, CancellationToken cancellationToken = default);

    Task<int> RecordAuditAsync(RecordAuditRequest request, CancellationToken cancellationToken = default);

    Task<int> RequestDisposalAsync(RequestDisposalRequest request, CancellationToken cancellationToken = default);
    Task ApproveDisposalAsync(int assetDisposalId, CancellationToken cancellationToken = default);
    Task RejectDisposalAsync(int assetDisposalId, string reason, CancellationToken cancellationToken = default);
    Task CompleteDisposalAsync(CompleteDisposalRequest request, CancellationToken cancellationToken = default);

    Task<int> SaveQrCodeAsync(SaveQrCodeRequest request, CancellationToken cancellationToken = default);
    Task LogLabelPrintAsync(LogLabelPrintRequest request, CancellationToken cancellationToken = default);
}
