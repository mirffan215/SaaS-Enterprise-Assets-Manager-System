namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblPermission</c>.
/// </summary>
public class Permission : AuditableEntity
{
    public int PermissionId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
