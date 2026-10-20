using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Shared.Abstractions;

namespace AMGH.ITInventory.Web.Infrastructure;

public sealed record TenantSignInSuccess(TenantInfo Tenant, AuthenticatedUser User);

/// <param name="Successes">Every company in which the user name (or e-mail) and password are correct.</param>
/// <param name="Message">What to tell the person when nothing matched (a lock-out notice, or the generic message).</param>
/// <param name="TooManyCompanies">More companies than a global sign-in will search; the person must use their company's address or code.</param>
public sealed record TenantSignInResult(IReadOnlyList<TenantSignInSuccess> Successes, string Message, bool TooManyCompanies);

/// <summary>
/// Sign-in without naming the company: the user name (or e-mail) and password are checked in each active company's database, one
/// after the other, each in its own tenant scope. Only a correct password counts, so nothing is revealed about which companies
/// exist or who is in them; the answer is the same generic message whatever failed. The sign-in page is rate-limited per address.
/// </summary>
public sealed class TenantSignInService
{
    /// <summary>Beyond this many companies a search on every sign-in is too slow: people then use their company's address or code.</summary>
    internal const int MaxCompanies = 100;
    internal const string GenericFailure = "User name or password is not correct.";

    private readonly IServiceScopeFactory _scopes;

    public TenantSignInService(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<TenantSignInResult> SignInAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantInfo> tenants;
        using (var scope = _scopes.CreateScope())
            tenants = (await scope.ServiceProvider.GetRequiredService<ITenantCatalog>().ListAsync(cancellationToken))
                .Where(t => t.IsActive && !t.IsDemo).Select(t => t.ToInfo()).ToList();
        if (tenants.Count > MaxCompanies) return new TenantSignInResult(Array.Empty<TenantSignInSuccess>(), GenericFailure, true);

        var successes = new List<TenantSignInSuccess>();
        string? lockout = null;
        foreach (var tenant in tenants)
        {
            // a scope of its own per company: the database context binds to the tenant when it is first used
            using var scope = _scopes.CreateScope();
            using var entered = scope.ServiceProvider.GetRequiredService<ITenantAccessor>().Enter(tenant);
            var outcome = await scope.ServiceProvider.GetRequiredService<IAccountService>()
                .SignInAsync(username, password, ipAddress, userAgent, null, cancellationToken);
            if (outcome.Succeeded && outcome.User is not null) successes.Add(new TenantSignInSuccess(tenant, outcome.User));
            else if (outcome.Status == SignInStatus.LockedOut && outcome.Message is not null) lockout = outcome.Message;
        }
        return new TenantSignInResult(successes, successes.Count == 0 ? lockout ?? GenericFailure : string.Empty, false);
    }
}
