using System.ComponentModel.DataAnnotations;
using AMGH.ITInventory.Security.Mfa;

namespace AMGH.ITInventory.Web.Models;

public sealed class MfaViewModel
{
    [Required(ErrorMessage = "Enter the code.")]
    [StringLength(20)]
    [Display(Name = "Code")]
    public string? Code { get; set; }
}

public sealed class MfaSetupViewModel
{
    public string Secret { get; set; } = string.Empty;
    public string QrSvg { get; set; } = string.Empty;
    public bool Forced { get; set; }
    [StringLength(20)] public string? Code { get; set; }
}

public sealed class MfaCodesViewModel
{
    public IReadOnlyList<string> Codes { get; set; } = Array.Empty<string>();
    public string Heading { get; set; } = string.Empty;
}

public sealed class SecurityViewModel
{
    public MfaState State { get; set; } = new(false, false, 0);
    public bool RequiredByCompany { get; set; }
}

public sealed class SecuritySettingsViewModel
{
    [Range(0, 2)] public byte MfaPolicy { get; set; }
    [StringLength(1000)] public string? AllowedIpRanges { get; set; }
    public bool SsoEnabled { get; set; }
    [StringLength(300)] public string? SsoAuthority { get; set; }
    [StringLength(200)] public string? SsoClientId { get; set; }
    [StringLength(500)] [DataType(DataType.Password)] public string? SsoClientSecret { get; set; }
    [StringLength(200)] public string? SsoAllowedDomain { get; set; }

    public bool SsoSecretSet { get; set; }
    public string? CurrentIp { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public IReadOnlyList<MfaUserRow> People { get; set; } = Array.Empty<MfaUserRow>();
    public string? Message { get; set; }
    public bool Succeeded { get; set; }
}
