using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblFloor</c>.
/// </summary>
public class Floor : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int FloorId { get; set; }
    public int BuildingId { get; set; }
    public string FloorCode { get; set; } = string.Empty;
    public string FloorName { get; set; } = string.Empty;
    public short FloorNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Building Building { get; set; } = null!;

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
