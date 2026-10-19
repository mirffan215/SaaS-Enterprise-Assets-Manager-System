using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>Limits a role to an asset category (dbo.tblRoleCategory, script 16). A role without rows covers all categories.</summary>
public class RoleCategory : ICompanyOwned
{
    public int CompanyId { get; set; }
    public int RoleId { get; set; }
    public int AssetCategoryId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
}
