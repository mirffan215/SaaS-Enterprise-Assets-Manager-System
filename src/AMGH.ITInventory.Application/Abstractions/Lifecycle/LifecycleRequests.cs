using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Application.Abstractions.Lifecycle;

public sealed record AssignAssetRequest(
    int AssetId,
    int? EmployeeId,
    int? DepartmentId,
    int? LocationId,
    AssignmentType AssignmentType,
    DateOnly? ExpectedReturnDate,
    string? ConditionAtAssignment,
    string? Notes);

public sealed record ReturnAssetRequest(
    int AssetId,
    string? ConditionAtReturn,
    string ReturnStatusCode,
    string? Notes);

public sealed record RequestMovementRequest(
    int AssetId,
    int ToLocationId,
    int? ToBuildingId,
    int? ToFloorId,
    int? ToRoomId,
    MovementType MovementType,
    string MovementReason,
    string? ChangeRequestNumber);

public sealed record RecordAuditRequest(
    int AssetId,
    string AuditCycle,
    AuditResult AuditResult,
    ScanMethod ScanMethod,
    int? FoundLocationId,
    int? FoundRoomId,
    int? FoundEmployeeId,
    string? Remarks,
    int NextAuditMonths = 12);

public sealed record RequestDisposalRequest(
    int AssetId,
    int DisposalMethodId,
    string Reason);

public sealed record CompleteDisposalRequest(
    int AssetDisposalId,
    DateOnly DisposalDate,
    string? DataSanitizationMethod,
    DateTime? DataSanitizationDateUtc,
    string? DataSanitizationVerifiedBy,
    string? CertificateNumber,
    string? RecipientName,
    decimal? SaleValue,
    int? CurrencyId,
    string? Remarks);

public sealed record SaveQrCodeRequest(
    int AssetId,
    string Payload,
    string PayloadSha256Hex,
    byte[] QrImagePng);

public sealed record LogLabelPrintRequest(
    int AssetQrCodeId,
    LabelSize LabelSize,
    short Copies,
    Guid PrintBatchId,
    bool IsReprint,
    string? ReprintReason,
    LabelDesign LabelDesign = LabelDesign.QrLabel);
