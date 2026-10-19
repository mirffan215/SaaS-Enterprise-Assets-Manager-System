namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum AuditAction
{
    Create,
    Update,
    Delete,
    Restore,
    View,
    Export,
    Import,
    Print,
    Approve,
    Reject
}
