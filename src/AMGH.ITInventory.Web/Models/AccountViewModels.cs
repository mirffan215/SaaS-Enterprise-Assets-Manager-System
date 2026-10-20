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
    /// <summary>Show the one-click "Explore Demo Environment" button (multi-tenant mode with a demo tenant; display only).</summary>
    public bool ShowExploreDemo { get; set; }
    /// <summary>Multi-tenant sign-in without a chosen company: the company is found from the user name and password (display only).</summary>
    public bool GlobalSignIn { get; set; }
    /// <summary>Offer "Register your company" (self-registration is on; display only).</summary>
    public bool ShowRegister { get; set; }
    /// <summary>Multi-tenant mode with a remembered company: offer "not your company?" (display only).</summary>
    public bool ShowOtherOrganisation { get; set; }
    /// <summary>Shown on the demo tenant's sign-in page: the sandbox user name and password (display only).</summary>
    public string? DemoUser { get; set; }
    public string? DemoPassword { get; set; }
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
