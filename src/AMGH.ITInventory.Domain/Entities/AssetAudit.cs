using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetAudit</c>.
/// </summary>
public class AssetAudit : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int AssetAuditId { get; set; }
    public int AssetId { get; set; }
    public string AuditCycle { get; set; } = string.Empty;
    public DateTime AuditDate { get; set; }
    public string AuditedBy { get; set; } = string.Empty;
    public AuditResult AuditResult { get; set; }
    public ScanMethod ScanMethod { get; set; } = ScanMethod.Manual;
    public int? FoundLocationId { get; set; }
    public int? FoundRoomId { get; set; }
    public int? FoundEmployeeId { get; set; }
    public string? Remarks { get; set; }
    public string? CorrectiveAction { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedDate { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual Location? FoundLocation { get; set; }
    public virtual Room? FoundRoom { get; set; }
    public virtual Employee? FoundEmployee { get; set; }
}
