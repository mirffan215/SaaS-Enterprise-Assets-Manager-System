using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblRoom</c>.
/// </summary>
public class Room : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int RoomId { get; set; }
    public int FloorId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public RoomType RoomType { get; set; } = RoomType.Office;
    /// <summary>Physical security perimeter (ISO 27001 A.7.1).</summary>
    public bool IsSecureArea { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Floor Floor { get; set; } = null!;

    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
