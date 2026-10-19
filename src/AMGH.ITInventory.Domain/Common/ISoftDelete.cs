namespace AMGH.ITInventory.Domain.Common;

/// <summary>
/// Rows are never physically deleted (enforced by database permissions and triggers).
/// The persistence layer converts deletes into these flags and filters deleted rows by default.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    string? DeletedBy { get; set; }
    DateTime? DeletedDate { get; set; }
}
