namespace AMGH.ITInventory.Domain.Entities;

/// <summary>A company workspace (dbo.tblCompany, created by script 10). Platform-wide, not company-filtered.</summary>
public class Company
{
    public int CompanyId { get; set; }
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
    /// <summary>Active, Suspended or Archived.</summary>
    public string Status { get; set; } = CompanyStatuses.Active;

    // Document numbering (Release 2.13, script 18)
    /// <summary>"Continuous" (AMGH-MOV-000123) or "Yearly" (AMGH-MOV-2026-000123), restarting each fiscal year.</summary>
    public string DocNumberingMode { get; set; } = "Continuous";
    /// <summary>Month the fiscal year starts (1-12); with a start other than January the year label is the year it ends in.</summary>
    public byte FiscalYearStartMonth { get; set; } = 1;
    /// <summary>"Year" (2026) or "FY" (FY26).</summary>
    public string DocYearFormat { get; set; } = "Year";
    public byte DocNumberDigits { get; set; } = 6;
    // Release 2.8: profile and system settings (script 15)
    public string? NameArabic { get; set; }
    public int? ParentCompanyId { get; set; }
    public string? CompanyType { get; set; }
    public string? VatNumber { get; set; }
    public string? Region { get; set; }
    public string DateFormat { get; set; } = "dd MMM yyyy";
    // Sign-in security (migration 0001)
    /// <summary>0 optional, 1 required for administrators, 2 required for everyone.</summary>
    public byte MfaPolicy { get; set; }
    /// <summary>Addresses or CIDR ranges that may use the company; null or empty means anywhere.</summary>
    public string? AllowedIpRanges { get; set; }
    public bool SsoEnabled { get; set; }
    public string? SsoAuthority { get; set; }
    public string? SsoClientId { get; set; }
    /// <summary>Protected by the application.</summary>
    public string? SsoClientSecret { get; set; }
    public string? SsoAllowedDomain { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool AssetAuditEnabled { get; set; } = true;
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ICollection<CompanyUser> Members { get; set; } = new List<CompanyUser>();
    public virtual CompanyBranding? Branding { get; set; }
}

public static class CompanyStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Archived = "Archived";
}

/// <summary>
/// Membership of a user in a company (dbo.tblCompanyUser). Roles are held per membership. Not filtered by
/// Row-Level Security (sign-in must find a user's companies), so every query on it filters explicitly.
/// </summary>
public class CompanyUser
{
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual Company Company { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}

/// <summary>Branding of a company workspace (dbo.tblCompanyBranding, script 11). Public: shown before sign-in.</summary>
public class CompanyBranding
{
    public int CompanyId { get; set; }
    public byte[]? LogoImage { get; set; }
    public string? LogoContentType { get; set; }
    public string HeaderColor { get; set; } = BrandingDefaults.Header;
    public string PrimaryColor { get; set; } = BrandingDefaults.Primary;
    public string AccentColor { get; set; } = BrandingDefaults.Accent;
    public string? LoginTagline { get; set; }
    public string? LogoSourceUrl { get; set; }
    public string ModifiedBy { get; set; } = "SYSTEM";
    public DateTime ModifiedDate { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual Company Company { get; set; } = null!;
}

public static class BrandingDefaults
{
    public const string Header = "#0E3B43";
    public const string Primary = "#13707A";
    public const string Accent = "#E8604C";
}
