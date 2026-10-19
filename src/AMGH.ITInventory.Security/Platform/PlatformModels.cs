namespace AMGH.ITInventory.Security.Platform;

/// <summary>What the public company picker may show: name and logo only.</summary>
public sealed record WorkspaceCard(string CompanyCode, string DisplayName, bool HasLogo);

/// <summary>Public branding of a workspace (sign-in page, header, labels).</summary>
public sealed record WorkspaceBranding(int CompanyId, string CompanyCode, string DisplayName, string LegalName, string? Phone,
    string HeaderColor, string PrimaryColor, string AccentColor, string? LoginTagline, bool HasLogo,
    string? NameArabic = null, string? ContactEmail = null);

public sealed record CompanyLogo(byte[] Content, string ContentType, string? SourceUrl = null);

public sealed record CompanyListItem(int CompanyId, string CompanyCode, string DisplayName, string LegalName, string Status,
    int ActiveMembers, DateTime CreatedDateUtc, bool SetupComplete);

/// <summary>Company details edited in the wizard and in company management.</summary>
public sealed class CompanyDetailsForm
{
    public string CompanyCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? RegistrationNo { get; set; }
    public string? Industry { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string Country { get; set; } = "Saudi Arabia";
    public string? City { get; set; }
    public string? AddressLine { get; set; }
    public string TimeZone { get; set; } = "Arab Standard Time";
    public string LanguageCode { get; set; } = "en";
    public int? BaseCurrencyId { get; set; }
    public string AssetNumberPrefix { get; set; } = string.Empty;
    public string HeaderColor { get; set; } = Domain.Entities.BrandingDefaults.Header;
    public string PrimaryColor { get; set; } = Domain.Entities.BrandingDefaults.Primary;
    public string AccentColor { get; set; } = Domain.Entities.BrandingDefaults.Accent;
    public string? LoginTagline { get; set; }
    public bool RemoveLogo { get; set; }
    /// <summary>Load the logo from this https address instead of a file (downloaded once and stored like an upload).</summary>
    public string? LogoUrl { get; set; }
    public string? LogoSourceUrl { get; set; }
    // Release 2.8
    public string? NameArabic { get; set; }
    public int? ParentCompanyId { get; set; }
    public string? CompanyType { get; set; }
    public string? VatNumber { get; set; }
    public string? Region { get; set; }
    public string DateFormat { get; set; } = "dd MMM yyyy";
    public bool NotificationsEnabled { get; set; } = true;
    public bool AssetAuditEnabled { get; set; } = true;
    public static readonly IReadOnlyList<string> DateFormats = new[] { "dd MMM yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };
    public static readonly IReadOnlyList<string> CompanyTypes = new[] { "Holding", "Subsidiary", "Branch", "Joint venture", "Affiliate", "Other" };
    // Release 2.13: document numbering
    public string DocNumberingMode { get; set; } = Application.Documents.NumberingModes.Continuous;
    public int FiscalYearStartMonth { get; set; } = 1;
    public string DocYearFormat { get; set; } = Application.Documents.YearFormats.Year;
    public int DocNumberDigits { get; set; } = 6;
    public string? RowVersion { get; set; }
}

/// <summary>Everything the setup wizard collects (details, branding, first structure, administrator).</summary>
public sealed class CompanySetupRequest
{
    public CompanyDetailsForm Details { get; set; } = new();
    /// <summary>Optional first location and department, created in the new company.</summary>
    public string? HeadOfficeName { get; set; }
    public string? FirstDepartmentName { get; set; }
    /// <summary>"existing" (an account that already exists) or "new".</summary>
    public string AdminMode { get; set; } = "new";
    public string? ExistingUsername { get; set; }
    public string? NewUsername { get; set; }
    public string? NewDisplayName { get; set; }
    public string? NewEmail { get; set; }
}

public sealed record CompanySetupResult(int CompanyId, string CompanyCode, string AdminUsername, string? TemporaryPassword,
    IReadOnlyList<string> Copied);
