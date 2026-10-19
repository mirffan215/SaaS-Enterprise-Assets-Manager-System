namespace AMGH.ITInventory.Domain.Enums;

/// <summary>Stored as NVARCHAR(30) text; values are enforced by a CHECK constraint.</summary>
public enum EmploymentStatus
{
    Active,
    OnLeave,
    Terminated
}
