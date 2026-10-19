using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblQrLabelPrintLog</c>.
/// </summary>
public class QrLabelPrintLog : ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public long QrLabelPrintLogId { get; set; }
    public int AssetQRCodeId { get; set; }
    public int AssetId { get; set; }
    public LabelSize LabelSize { get; set; }
    public LabelDesign LabelDesign { get; set; } = LabelDesign.QrLabel;
    public short Copies { get; set; } = 1;
    public Guid PrintBatchId { get; set; }
    public bool IsReprint { get; set; }
    public string? ReprintReason { get; set; }
    public string PrintedBy { get; set; } = string.Empty;
    public DateTime PrintedDate { get; set; }

    public virtual AssetQrCode AssetQrCode { get; set; } = null!;
    public virtual Asset Asset { get; set; } = null!;
}
