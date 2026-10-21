namespace AMGH.ITInventory.Domain.Entities;

/// <summary>One single-use recovery code for two-step verification (only its SHA-256 is stored).</summary>
public class UserRecoveryCode
{
    public long UserRecoveryCodeId { get; set; }
    public int UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime? UsedDate { get; set; }

    public virtual User User { get; set; } = null!;
}
