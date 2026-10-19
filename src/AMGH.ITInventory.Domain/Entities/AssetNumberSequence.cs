using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Monotonic number generator per asset family. LastNumber can only increase (enforced by trigger).
/// </summary>
public class AssetNumberSequence : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int AssetNumberSequenceId { get; set; }
    public string SequenceCode { get; set; } = string.Empty;
    public string Prefix { get; set; } = "AMGH-IT";
    public long LastNumber { get; set; }
    public byte PaddingLength { get; set; } = 5;
    /// <summary>Between prefix, code and number (Release 2.9): "-", "/", ".", "_" or none.</summary>
    public string Separator { get; set; } = "-";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<AssetCategory> AssetCategories { get; set; } = new List<AssetCategory>();
}
