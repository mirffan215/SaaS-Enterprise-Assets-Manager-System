namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblUser</c>.
/// </summary>
public class User : AuditableEntity
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? EmployeeId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Rotated on password change / role change / disable; invalidates existing sessions.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    /// <summary>Platform Super Administrator: manages companies, never a company role.</summary>
    public bool IsPlatformAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public DateTime? PasswordChangedDate { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public string? LastLoginIp { get; set; }

    public virtual Employee? Employee { get; set; }

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<UserPasswordHistory> PasswordHistory { get; set; } = new List<UserPasswordHistory>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
