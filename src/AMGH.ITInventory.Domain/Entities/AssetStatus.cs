namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetStatus</c>.
/// </summary>
public class AssetStatus : AuditableEntity
{
    public int AssetStatusId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public bool IsAssignable { get; set; }
    public bool CountsAsInventory { get; set; } = true;
    public bool IsTerminal { get; set; }
    public string? ColorHex { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
