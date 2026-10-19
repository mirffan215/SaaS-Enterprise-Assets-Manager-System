using System.ComponentModel.DataAnnotations;

namespace AMGH.ITInventory.Security;

/// <summary>
/// Security settings (appsettings.json section "Security"). Defaults implement the
/// Access Control Procedure; change them there, not in code.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    [Range(12, 128)] public int PasswordMinimumLength { get; set; } = 12;

    /// <summary>Number of previous passwords that cannot be reused.</summary>
    [Range(0, 24)] public int PasswordHistoryCount { get; set; } = 5;

    /// <summary>Days before a password must be changed; 0 disables expiry.</summary>
    [Range(0, 365)] public int PasswordMaximumAgeDays { get; set; } = 90;

    [Range(3, 20)] public int MaxFailedAccessAttempts { get; set; } = 5;

    [Range(1, 1440)] public int LockoutMinutes { get; set; } = 15;

    /// <summary>Sign-out after this many minutes without activity.</summary>
    [Range(5, 120)] public int SessionIdleTimeoutMinutes { get; set; } = 15;

    /// <summary>Maximum session length regardless of activity.</summary>
    [Range(1, 24)] public int SessionAbsoluteTimeoutHours { get; set; } = 8;

    /// <summary>How often an active session is re-checked against the database (disabled user, role or password change).</summary>
    [Range(1, 60)] public int SecurityStampValidationMinutes { get; set; } = 5;

    /// <summary>Length of temporary passwords issued by administrators.</summary>
    [Range(12, 64)] public int TemporaryPasswordLength { get; set; } = 16;
}
