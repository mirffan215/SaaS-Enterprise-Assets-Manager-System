using System.ComponentModel.DataAnnotations;

namespace AMGH.ITInventory.Web.Models;

public sealed class TenantSelectViewModel
{
    [Required(ErrorMessage = "Enter your company code.")]
    [StringLength(10)]
    public string? Code { get; set; }

    /// <summary>Offer the one-click demo environment (display only).</summary>
    public bool ShowExploreDemo { get; set; }
}

public sealed record TenantRow(string Code, string Name, bool IsDemo, bool IsActive, DateTime CreatedUtc, string Url);

/// <summary>The operator page: sign in with the operator key, then create companies, list them and reach their backups.</summary>
public sealed class TenantAdminViewModel
{
    [DataType(DataType.Password)] [StringLength(200)] public string? OperatorKey { get; set; }
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(256)] public string? AdminEmail { get; set; }
    [DataType(DataType.Password)] [StringLength(128)] public string? AdminPassword { get; set; }

    public bool LoggedIn { get; set; }
    public string? Message { get; set; }
    public bool Succeeded { get; set; }
    public IReadOnlyList<TenantRow> Tenants { get; set; } = Array.Empty<TenantRow>();
    public AMGH.ITInventory.Web.Infrastructure.PlatformSnapshot? Snapshot { get; set; }
    public IReadOnlyList<AMGH.ITInventory.Application.Tenancy.TenantEvent> Events { get; set; } = Array.Empty<AMGH.ITInventory.Application.Tenancy.TenantEvent>();
    public bool EventsUnavailable { get; set; }

    // company keys and demo requests (Super Admin page)
    public IReadOnlyList<AMGH.ITInventory.Application.Tenancy.CompanyKeyInfo> Keys { get; set; } = Array.Empty<AMGH.ITInventory.Application.Tenancy.CompanyKeyInfo>();
    public IReadOnlyList<AMGH.ITInventory.Application.Tenancy.DemoRequestInfo> Requests { get; set; } = Array.Empty<AMGH.ITInventory.Application.Tenancy.DemoRequestInfo>();
    public bool KeysUnavailable { get; set; }
    public bool MailReady { get; set; }
    /// <summary>The key just made: shown once, never stored in a form that can be read back.</summary>
    public string? NewKey { get; set; }
    public string? NewKeyFor { get; set; }
    public DateTime? NewKeyExpires { get; set; }
    public bool KeyEmailed { get; set; }
}

public sealed record BackupRow(string FileName, long SizeBytes, DateTime CreatedUtc, DateTime? VerifiedUtc = null, bool VerifyOk = false, string? VerifyNote = null,
    DateTime? OffsiteUtc = null, bool OffsiteOk = false, string? OffsiteNote = null);

/// <summary>Backups of one company (operator page).</summary>
public sealed class TenantBackupsViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<BackupRow> Files { get; set; } = Array.Empty<BackupRow>();
    public string? Message { get; set; }
    public bool Succeeded { get; set; }
    /// <summary>True when the backup folder can be listed (otherwise files made earlier are not shown).</summary>
    public bool CanList { get; set; } = true;
    /// <summary>An off-site target is configured (copies are made after scheduled backups and can be made by hand).</summary>
    public bool OffsiteConfigured { get; set; }
    /// <summary>The workspace's backup schedule (workspace page only).</summary>
    public AMGH.ITInventory.Web.Infrastructure.BackupSchedule Schedule { get; set; } = new();
    public IReadOnlyList<AMGH.ITInventory.Web.Infrastructure.BackgroundJobInfo> Jobs { get; set; } = Array.Empty<AMGH.ITInventory.Web.Infrastructure.BackgroundJobInfo>();
}

/// <summary>Company self-registration (Tenancy:AllowSelfRegistration).</summary>
public sealed class RegisterViewModel
{
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(256)] public string? AdminEmail { get; set; }
    [DataType(DataType.Password)] [StringLength(128)] public string? AdminPassword { get; set; }
    /// <summary>The company key from the operator (EAMS-XXXX-XXXX-XXXX).</summary>
    [StringLength(40)] public string? CompanyKey { get; set; }
    /// <summary>False when open sign-up is on and the key is optional (display only).</summary>
    public bool KeyRequired { get; set; } = true;
    public string? Message { get; set; }
}
