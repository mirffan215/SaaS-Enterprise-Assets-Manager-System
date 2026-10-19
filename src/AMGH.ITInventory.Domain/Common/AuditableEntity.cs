namespace AMGH.ITInventory.Domain.Common;

/// <summary>
/// Base class for every business entity. Maps to the standard audit columns present on
/// all non-log tables. All dates are UTC.
/// </summary>
public abstract class AuditableEntity : IAuditableEntity, ISoftDelete
{
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>SQL Server ROWVERSION used for optimistic concurrency.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Marks the entity as deleted without removing it.</summary>
    public void SoftDelete(string deletedBy, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deletedBy);
        IsDeleted = true;
        DeletedBy = deletedBy;
        DeletedDate = utcNow;
    }

    /// <summary>Reverses a soft delete.</summary>
    public void Restore(string restoredBy, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoredBy);
        IsDeleted = false;
        DeletedBy = null;
        DeletedDate = null;
        ModifiedBy = restoredBy;
        ModifiedDate = utcNow;
    }
}
