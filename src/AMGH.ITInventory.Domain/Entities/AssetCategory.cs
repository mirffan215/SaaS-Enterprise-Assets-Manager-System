using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetCategory</c>.
/// </summary>
public class AssetCategory : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int AssetCategoryId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int AssetNumberSequenceId { get; set; }
    public int? DefaultUsefulLifeMonths { get; set; }
    public bool RequiresHostname { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual AssetNumberSequence AssetNumberSequence { get; set; } = null!;

    public virtual ICollection<AssetSubCategory> SubCategories { get; set; } = new List<AssetSubCategory>();
    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
