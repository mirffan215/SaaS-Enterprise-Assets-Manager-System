using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetMovement</c>.
/// </summary>
public class AssetMovement : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int AssetMovementId { get; set; }
    public int AssetId { get; set; }
    public int? FromLocationId { get; set; }
    public int? FromBuildingId { get; set; }
    public int? FromFloorId { get; set; }
    public int? FromRoomId { get; set; }
    public int ToLocationId { get; set; }
    public int? ToBuildingId { get; set; }
    public int? ToFloorId { get; set; }
    public int? ToRoomId { get; set; }
    public MovementType MovementType { get; set; } = MovementType.Relocation;
    public WorkflowStatus MovementStatus { get; set; } = WorkflowStatus.Requested;
    public string MovementReason { get; set; } = string.Empty;
    /// <summary>Reference to the Change Management record.</summary>
    public string? ChangeRequestNumber { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? RejectionReason { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual Location? FromLocation { get; set; }
    public virtual Building? FromBuilding { get; set; }
    public virtual Floor? FromFloor { get; set; }
    public virtual Room? FromRoom { get; set; }
    public virtual Location ToLocation { get; set; } = null!;
    public virtual Building? ToBuilding { get; set; }
    public virtual Floor? ToFloor { get; set; }
    public virtual Room? ToRoom { get; set; }
}
