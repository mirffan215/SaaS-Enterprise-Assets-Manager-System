using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetAssignment</c>.
/// </summary>
public class AssetAssignment : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int AssetAssignmentId { get; set; }
    public int AssetId { get; set; }
    public int? EmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public AssignmentType AssignmentType { get; set; } = AssignmentType.Permanent;
    public DateTime AssignedDate { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public DateTime? ReturnedDate { get; set; }
    public string? ConditionAtAssignment { get; set; }
    public string? ConditionAtReturn { get; set; }
    /// <summary>Custodian accepted the Acceptable Use terms for this asset (ISO 27001 A.5.10).</summary>
    public bool AcknowledgementReceived { get; set; }
    public DateTime? AcknowledgementDate { get; set; }
    public bool IsCurrent { get; set; } = true;
    public string AssignedBy { get; set; } = string.Empty;
    public string? ReturnedTo { get; set; }
    public string? Notes { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual Employee? Employee { get; set; }
    public virtual Department? Department { get; set; }
    public virtual Location? Location { get; set; }
}
