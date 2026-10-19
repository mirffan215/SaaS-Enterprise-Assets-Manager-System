using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Security.Authorization;

/// <summary>Requires the user to hold one permission code (a claim issued at sign-in).</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;
    public string Permission { get; }
}

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Every permission code is also a policy name, so controllers use
/// <c>[HasPermission(Permissions.Assets.Edit)]</c> without registering 45 policies by hand.
/// Unknown policy names fall through to the default provider (and fail loudly).
/// </summary>
internal sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private static readonly HashSet<string> Known = new(Permissions.All, StringComparer.Ordinal);

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (Known.Contains(policyName))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Restricts a controller or action to users holding the given permission.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) : base(permission) => Permission = permission;
    public string Permission { get; }
}
