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
    /// <summary>Authenticator-app secret, protected by the application; null when two-step verification is off.</summary>
    public string? TotpSecret { get; set; }
    public DateTime? TotpEnabledDate { get; set; }
    /// <summary>Last accepted 30-second time step (a code can be used once).</summary>
    public long? TotpLastStep { get; set; }
    public bool HasTwoStep => TotpSecret is not null && TotpEnabledDate is not null;

    public virtual Employee? Employee { get; set; }

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<UserPasswordHistory> PasswordHistory { get; set; } = new List<UserPasswordHistory>();
    public virtual ICollection<UserRecoveryCode> RecoveryCodes { get; set; } = new List<UserRecoveryCode>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
