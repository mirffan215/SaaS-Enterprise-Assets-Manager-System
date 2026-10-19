using System.Data;
using AMGH.ITInventory.Application.Abstractions.Lifecycle;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.Data.SqlClient;
using static AMGH.ITInventory.Persistence.Procedures.StoredProcedureExecutor;

namespace AMGH.ITInventory.Persistence.Procedures;

internal sealed class AssetLifecycleProcedures : IAssetLifecycleProcedures
{
    private const int UserNameSize = 100;

    private readonly StoredProcedureExecutor _executor;
    private readonly ICurrentUserService _user;

    public AssetLifecycleProcedures(StoredProcedureExecutor executor, ICurrentUserService user)
    {
        _executor = executor;
        _user = user;
    }

    private SqlParameter Actor(string name = "@PerformedBy") => In(name, SqlDbType.NVarChar, _user.UserName, UserNameSize);

    private static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    public async Task<int> AssignAsync(AssignAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Out("@AssetAssignmentId", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.AssetAssign,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@EmployeeId", SqlDbType.Int, request.EmployeeId),
            In("@DepartmentId", SqlDbType.Int, request.DepartmentId),
            In("@LocationId", SqlDbType.Int, request.LocationId),
            In("@AssignmentType", SqlDbType.NVarChar, request.AssignmentType.ToString(), 30),
            In("@ExpectedReturnDate", SqlDbType.Date, ToDateTime(request.ExpectedReturnDate)),
            In("@ConditionAtAssignment", SqlDbType.NVarChar, request.ConditionAtAssignment, 200),
            In("@Notes", SqlDbType.NVarChar, request.Notes, 1000),
            Actor(),
            id
        ], cancellationToken);
        return (int)id.Value;
    }

    public Task ReturnAsync(ReturnAssetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _executor.ExecuteAsync(ProcedureNames.AssetReturn,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@ConditionAtReturn", SqlDbType.NVarChar, request.ConditionAtReturn, 200),
            In("@ReturnStatusCode", SqlDbType.NVarChar, request.ReturnStatusCode, 20),
            In("@Notes", SqlDbType.NVarChar, request.Notes, 1000),
            Actor()
        ], cancellationToken);
    }

    public Task AcknowledgeAssignmentAsync(int assetAssignmentId, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.AssignmentAcknowledge,
        [
            In("@AssetAssignmentId", SqlDbType.Int, assetAssignmentId),
            Actor()
        ], cancellationToken);

    public async Task<int> RequestMovementAsync(RequestMovementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Out("@AssetMovementId", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.MovementRequest,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@ToLocationId", SqlDbType.Int, request.ToLocationId),
            In("@ToBuildingId", SqlDbType.Int, request.ToBuildingId),
            In("@ToFloorId", SqlDbType.Int, request.ToFloorId),
            In("@ToRoomId", SqlDbType.Int, request.ToRoomId),
            In("@MovementType", SqlDbType.NVarChar, request.MovementType.ToString(), 30),
            In("@MovementReason", SqlDbType.NVarChar, request.MovementReason, 500),
            In("@ChangeRequestNumber", SqlDbType.NVarChar, request.ChangeRequestNumber, 50),
            Actor("@RequestedBy"),
            id
        ], cancellationToken);
        return (int)id.Value;
    }

    public Task ApproveMovementAsync(int assetMovementId, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.MovementApprove,
        [
            In("@AssetMovementId", SqlDbType.Int, assetMovementId),
            Actor("@ApprovedBy")
        ], cancellationToken);

    public Task RejectMovementAsync(int assetMovementId, string reason, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.MovementReject,
        [
            In("@AssetMovementId", SqlDbType.Int, assetMovementId),
            Actor("@RejectedBy"),
            In("@RejectionReason", SqlDbType.NVarChar, reason, 500)
        ], cancellationToken);

    public Task CompleteMovementAsync(int assetMovementId, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.MovementComplete,
        [
            In("@AssetMovementId", SqlDbType.Int, assetMovementId),
            Actor()
        ], cancellationToken);

    public async Task<int> RecordAuditAsync(RecordAuditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Out("@AssetAuditId", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.AuditRecord,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@AuditCycle", SqlDbType.NVarChar, request.AuditCycle, 20),
            In("@AuditResult", SqlDbType.NVarChar, request.AuditResult.ToString(), 30),
            In("@ScanMethod", SqlDbType.NVarChar, request.ScanMethod.ToString(), 30),
            In("@FoundLocationId", SqlDbType.Int, request.FoundLocationId),
            In("@FoundRoomId", SqlDbType.Int, request.FoundRoomId),
            In("@FoundEmployeeId", SqlDbType.Int, request.FoundEmployeeId),
            In("@Remarks", SqlDbType.NVarChar, request.Remarks, 1000),
            Actor("@AuditedBy"),
            In("@NextAuditMonths", SqlDbType.Int, request.NextAuditMonths),
            id
        ], cancellationToken);
        return (int)id.Value;
    }

    public async Task<int> RequestDisposalAsync(RequestDisposalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Out("@AssetDisposalId", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.DisposalRequest,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@DisposalMethodId", SqlDbType.Int, request.DisposalMethodId),
            In("@Reason", SqlDbType.NVarChar, request.Reason, 1000),
            Actor("@RequestedBy"),
            id
        ], cancellationToken);
        return (int)id.Value;
    }

    public Task ApproveDisposalAsync(int assetDisposalId, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.DisposalApprove,
        [
            In("@AssetDisposalId", SqlDbType.Int, assetDisposalId),
            Actor("@ApprovedBy")
        ], cancellationToken);

    public Task RejectDisposalAsync(int assetDisposalId, string reason, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(ProcedureNames.DisposalReject,
        [
            In("@AssetDisposalId", SqlDbType.Int, assetDisposalId),
            Actor("@RejectedBy"),
            In("@RejectionReason", SqlDbType.NVarChar, reason, 500)
        ], cancellationToken);

    public Task CompleteDisposalAsync(CompleteDisposalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _executor.ExecuteAsync(ProcedureNames.DisposalComplete,
        [
            In("@AssetDisposalId", SqlDbType.Int, request.AssetDisposalId),
            In("@DisposalDate", SqlDbType.Date, request.DisposalDate.ToDateTime(TimeOnly.MinValue)),
            In("@DataSanitizationMethod", SqlDbType.NVarChar, request.DataSanitizationMethod, 150),
            In("@DataSanitizationDate", SqlDbType.DateTime2, request.DataSanitizationDateUtc),
            In("@DataSanitizationVerifiedBy", SqlDbType.NVarChar, request.DataSanitizationVerifiedBy, 100),
            In("@CertificateNumber", SqlDbType.NVarChar, request.CertificateNumber, 100),
            In("@RecipientName", SqlDbType.NVarChar, request.RecipientName, 200),
            new SqlParameter("@SaleValue", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = (object?)request.SaleValue ?? DBNull.Value },
            In("@CurrencyId", SqlDbType.Int, request.CurrencyId),
            In("@Remarks", SqlDbType.NVarChar, request.Remarks, 1000),
            Actor()
        ], cancellationToken);
    }

    public async Task<int> SaveQrCodeAsync(SaveQrCodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Out("@AssetQRCodeId", SqlDbType.Int);
        await _executor.ExecuteAsync(ProcedureNames.QrCodeSave,
        [
            In("@AssetId", SqlDbType.Int, request.AssetId),
            In("@Payload", SqlDbType.NVarChar, request.Payload, 2000),
            In("@PayloadHash", SqlDbType.Char, request.PayloadSha256Hex, 64),
            In("@QrImagePng", SqlDbType.VarBinary, request.QrImagePng, -1),
            Actor("@GeneratedBy"),
            id
        ], cancellationToken);
        return (int)id.Value;
    }

    public Task LogLabelPrintAsync(LogLabelPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _executor.ExecuteAsync(ProcedureNames.QrLabelLogPrint,
        [
            In("@AssetQRCodeId", SqlDbType.Int, request.AssetQrCodeId),
            In("@LabelSize", SqlDbType.NVarChar, request.LabelSize.ToString(), 30),
            In("@Copies", SqlDbType.SmallInt, request.Copies),
            In("@PrintBatchId", SqlDbType.UniqueIdentifier, request.PrintBatchId),
            In("@IsReprint", SqlDbType.Bit, request.IsReprint),
            In("@ReprintReason", SqlDbType.NVarChar, request.ReprintReason, 500),
            Actor("@PrintedBy"),
            In("@LabelDesign", SqlDbType.NVarChar, request.LabelDesign.ToString(), 30)
        ], cancellationToken);
    }
}
