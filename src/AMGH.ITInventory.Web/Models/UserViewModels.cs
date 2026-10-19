using System.ComponentModel.DataAnnotations;
using AMGH.ITInventory.Security.Accounts;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AMGH.ITInventory.Web.Models;

public sealed class UserListViewModel
{
    public IReadOnlyList<UserSummary> Users { get; init; } = Array.Empty<UserSummary>();
    public bool CanManage { get; init; }
}

public sealed class UserFormViewModel
{
    public int UserId { get; set; }

    [Required(ErrorMessage = "Enter a username.")]
    [RegularExpression(@"^[A-Za-z0-9._-]{3,100}$", ErrorMessage = "3-100 characters: letters, digits, '.', '_' or '-'.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the person's full name.")]
    [StringLength(200, MinimumLength = 2)]
    [Display(Name = "Full name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an e-mail address.")]
    [EmailAddress(ErrorMessage = "Enter a valid e-mail address.")]
    [StringLength(256)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Employee ID")]
    public int? EmployeeId { get; set; }

    [Display(Name = "Account enabled")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Roles")]
    public List<int> RoleIds { get; set; } = new();

    /// <summary>Base64 ROWVERSION for optimistic concurrency.</summary>
    public string? RowVersion { get; set; }

    // ---- display-only (edit page)
    [ValidateNever] public IReadOnlyList<RoleOption> AvailableRoles { get; set; } = Array.Empty<RoleOption>();
    [ValidateNever] public bool IsLockedOut { get; set; }
    [ValidateNever] public DateTime? LockoutEndUtc { get; set; }
    [ValidateNever] public bool MustChangePassword { get; set; }
    [ValidateNever] public DateTime? LastLoginDateUtc { get; set; }
    [ValidateNever] public string? LastLoginIp { get; set; }
    [ValidateNever] public DateTime? CreatedDateUtc { get; set; }
    [ValidateNever] public string? CreatedBy { get; set; }
    [ValidateNever] public bool IsEdit => UserId > 0;
}

public sealed class TemporaryPasswordViewModel
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string TemporaryPassword { get; init; } = string.Empty;
    public bool IsNewUser { get; init; }
}

public sealed class LoginHistoryViewModel
{
    public string? Username { get; set; }
    public string? EventType { get; set; }

    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }

    [ValidateNever] public IReadOnlyList<LoginAuditEntry> Entries { get; set; } = Array.Empty<LoginAuditEntry>();
    [ValidateNever] public int MaxRows { get; set; }
}

public sealed class HomeViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public int PermissionCount { get; init; }
    public int AssetCount { get; init; }
    public int LicenseCount { get; init; }
    public int? ActiveUserCount { get; init; }
    public IReadOnlyList<LoginAuditEntry> RecentSignIns { get; init; } = Array.Empty<LoginAuditEntry>();
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; init; }
    public int StatusCode { get; init; }
}
