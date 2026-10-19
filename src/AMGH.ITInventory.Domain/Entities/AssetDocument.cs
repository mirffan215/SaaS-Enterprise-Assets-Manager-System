using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetDocument</c>.
/// </summary>
public class AssetDocument : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int AssetDocumentId { get; set; }
    public int AssetId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public int? ClassificationId { get; set; }
    public string? Description { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual Classification? Classification { get; set; }
}
