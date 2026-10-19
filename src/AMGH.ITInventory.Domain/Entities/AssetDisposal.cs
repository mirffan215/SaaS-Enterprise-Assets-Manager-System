using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetDisposal</c>.
/// </summary>
public class AssetDisposal : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int AssetDisposalId { get; set; }
    public int AssetId { get; set; }
    public int DisposalMethodId { get; set; }
    public WorkflowStatus DisposalStatus { get; set; } = WorkflowStatus.Requested;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>e.g. NIST SP 800-88 Clear / Purge / Destroy.</summary>
    public string? DataSanitizationMethod { get; set; }
    public DateTime? DataSanitizationDate { get; set; }
    public string? DataSanitizationVerifiedBy { get; set; }
    public string? CertificateNumber { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public string? RecipientName { get; set; }
    public decimal? SaleValue { get; set; }
    public int? CurrencyId { get; set; }
    public string? Remarks { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual DisposalMethod DisposalMethod { get; set; } = null!;
    public virtual Currency? Currency { get; set; }
}
