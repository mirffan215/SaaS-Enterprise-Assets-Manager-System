namespace AMGH.ITInventory.Domain.Common;

/// <summary>Creation and modification stamps, set by the persistence layer on SaveChanges.</summary>
public interface IAuditableEntity
{
    string CreatedBy { get; set; }
    DateTime CreatedDate { get; set; }
    string? ModifiedBy { get; set; }
    DateTime? ModifiedDate { get; set; }
}
