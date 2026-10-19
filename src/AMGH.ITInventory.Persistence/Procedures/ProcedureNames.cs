namespace AMGH.ITInventory.Persistence.Procedures;

/// <summary>Stored procedures in deployment/SQLScripts/05_StoredProcedures.sql.</summary>
internal static class ProcedureNames
{
    public const string AssetNumberGenerate = "dbo.usp_AssetNumber_Generate";
    public const string DocumentNumberNext = "dbo.usp_DocumentNumber_Next";
    public const string AssetAssign = "dbo.usp_Asset_Assign";
    public const string AssetReturn = "dbo.usp_Asset_Return";
    public const string AssignmentAcknowledge = "dbo.usp_AssetAssignment_Acknowledge";
    public const string MovementRequest = "dbo.usp_AssetMovement_Request";
    public const string MovementApprove = "dbo.usp_AssetMovement_Approve";
    public const string MovementReject = "dbo.usp_AssetMovement_Reject";
    public const string MovementComplete = "dbo.usp_AssetMovement_Complete";
    public const string AuditRecord = "dbo.usp_AssetAudit_Record";
    public const string DisposalRequest = "dbo.usp_AssetDisposal_Request";
    public const string DisposalApprove = "dbo.usp_AssetDisposal_Approve";
    public const string DisposalReject = "dbo.usp_AssetDisposal_Reject";
    public const string DisposalComplete = "dbo.usp_AssetDisposal_Complete";
    public const string QrCodeSave = "dbo.usp_AssetQrCode_Save";
    public const string QrLabelLogPrint = "dbo.usp_QrLabel_LogPrint";
    public const string LicenseSetKey = "dbo.usp_SoftwareLicense_SetKey";
    public const string LicenseGetKey = "dbo.usp_SoftwareLicense_GetKey";
}
