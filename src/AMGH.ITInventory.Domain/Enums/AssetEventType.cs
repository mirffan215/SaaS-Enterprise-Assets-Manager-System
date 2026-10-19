namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum AssetEventType
{
    Created,
    Updated,
    StatusChanged,
    Assigned,
    Returned,
    Moved,
    MaintenanceLogged,
    Audited,
    QrGenerated,
    LabelPrinted,
    DocumentAttached,
    DisposalRequested,
    Disposed,
    Deleted,
    Restored,
    LicenseAllocated,
    LicenseRevoked
}
