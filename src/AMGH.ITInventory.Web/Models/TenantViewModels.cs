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
}

public sealed record BackupRow(string FileName, long SizeBytes, DateTime CreatedUtc);

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
}

/// <summary>Company self-registration (Tenancy:AllowSelfRegistration).</summary>
public sealed class RegisterViewModel
{
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(256)] public string? AdminEmail { get; set; }
    [DataType(DataType.Password)] [StringLength(128)] public string? AdminPassword { get; set; }
    public string? Message { get; set; }
}
