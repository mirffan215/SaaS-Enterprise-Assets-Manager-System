using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Mfa;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Web.Infrastructure;

public enum GateResult { Allowed, IpDenied, Challenge, SetupRequired }

/// <summary>
/// What happens after a correct password (or single sign-on): the company's address list is checked, a person with two-step verification
/// is asked for the code, and a person whose company requires it but who has not set it up yet is sent to set it up.
/// Runs in a scope of its own inside the company's tenant, so the plain and the "no company chosen" sign-in behave alike.
/// </summary>
public sealed class SignInGate
{
    private readonly IServiceScopeFactory _scopes;

    public SignInGate(IServiceScopeFactory scopes) => _scopes = scopes;

    public static bool IsAdministrator(bool platformAdmin, IEnumerable<string> permissions) =>
        platformAdmin || permissions.Any(p => p is Permissions.Administration.UsersManage or Permissions.Administration.RolesManage);

    public static bool Required(CompanySecurity security, bool administrator) =>
        security.MfaPolicy == 2 || (security.MfaPolicy == 1 && administrator);

    public async Task<GateResult> EvaluateAsync(TenantInfo? tenant, AuthenticatedUser user, string? ipAddress, bool viaSso, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        using var entered = tenant is null ? null : scope.ServiceProvider.GetRequiredService<ITenantAccessor>().Enter(tenant);
        var security = await scope.ServiceProvider.GetRequiredService<ICompanySecurityService>().GetAsync(user.CompanyId, ct);
        if (security is null) return GateResult.Allowed;
        if (!IpRules.Allows(ipAddress, security.AllowedIpRanges)) return GateResult.IpDenied;
        if (viaSso) return GateResult.Allowed; // the provider has already asked for its own second step
        var state = await scope.ServiceProvider.GetRequiredService<IMfaService>().GetStateAsync(user.UserId, ct);
        if (state.Enabled) return GateResult.Challenge;
        return Required(security, IsAdministrator(user.IsPlatformAdmin, user.Permissions)) ? GateResult.SetupRequired : GateResult.Allowed;
    }
}
