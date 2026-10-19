namespace AMGH.ITInventory.Security.Claims;

/// <summary>Custom claim types carried in the authentication cookie.</summary>
public static class AppClaimTypes
{
    public const string Permission = "amgh:perm";
    public const string DisplayName = "amgh:name";
    public const string SecurityStamp = "amgh:stamp";
    public const string MustChangePassword = "amgh:mcp";
    public const string AuthTime = "amgh:auth_time";
    /// <summary>The company (workspace) this session works in. Source of truth for data isolation.</summary>
    public const string CompanyId = "amgh:cid";
    public const string CompanyCode = "amgh:ccode";
    public const string CompanyName = "amgh:cname";
    public const string PlatformAdmin = "amgh:padmin";
}
