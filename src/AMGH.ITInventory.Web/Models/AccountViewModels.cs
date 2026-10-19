using System.ComponentModel.DataAnnotations;

namespace AMGH.ITInventory.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your user name or e-mail.")]
    [StringLength(256)]
    [Display(Name = "User name or e-mail")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [StringLength(128)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public bool SessionExpired { get; set; }

    /// <summary>The workspace being signed in to (chosen on the company picker or remembered). Empty = the person's
    /// default company is used after the password is verified (Release 2.11, direct sign-in).</summary>
    [StringLength(10)]
    public string? CompanyCode { get; set; }

    // display only (re-read from the directory on every request; never trusted from the form)
    public string CompanyName { get; set; } = string.Empty;
    public bool HasLogo { get; set; }
    public string? LoginTagline { get; set; }
    public bool CanChangeCompany { get; set; }
    public string? NameArabic { get; set; }
    /// <summary>Logo shown: the company's uploaded logo, else the group emblem.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Remember the user name (not the session) on this computer.</summary>
    public bool RememberMe { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    public string? CompanyCode { get; set; }
    public string CompanyName { get; set; } = AMGH.ITInventory.Shared.AppInfo.Owner;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public sealed class WorkspacePickerViewModel
{
    public IReadOnlyList<AMGH.ITInventory.Security.Platform.WorkspaceCard> Companies { get; init; } = Array.Empty<AMGH.ITInventory.Security.Platform.WorkspaceCard>();
    public string? ReturnUrl { get; init; }
    public string? Message { get; init; }
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "The new password must be at least 12 characters.")]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Re-enter the new password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool IsForced { get; set; }
}
