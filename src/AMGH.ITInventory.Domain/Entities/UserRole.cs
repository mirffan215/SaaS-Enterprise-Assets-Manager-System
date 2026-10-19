using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblUserRole</c>.
/// </summary>
public class UserRole : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}
