using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetQRCode</c>.
/// </summary>
public class AssetQrCode : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int AssetQRCodeId { get; set; }
    public int AssetId { get; set; }
    public int QrVersion { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public byte[] QrImagePng { get; set; } = Array.Empty<byte>();
    public bool IsCurrent { get; set; } = true;

    public virtual Asset Asset { get; set; } = null!;

    public virtual ICollection<QrLabelPrintLog> PrintLogs { get; set; } = new List<QrLabelPrintLog>();
}
