namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Prevents password reuse (Access Control Procedure).
/// </summary>
public class UserPasswordHistory
{
    public long UserPasswordHistoryId { get; set; }
    public int UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }

    public virtual User User { get; set; } = null!;
}
