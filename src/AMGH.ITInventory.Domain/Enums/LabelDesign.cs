namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum LabelDesign
{
    QrLabel,
    AssetTag,
    /// <summary>QR label for an RFID tag: also carries the EPC written to the tag.</summary>
    RfidLabel
}
