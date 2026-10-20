using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Passwords;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMGH.ITInventory.Security;

public static class DependencyInjection
{
    /// <summary>Registers password, account, user-administration and permission-authorisation services.</summary>
    public static IServiceCollection AddSecurityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddSingleton<Application.Tenancy.ITenantAdminPasswordHasher, TenantAdminPasswordHasher>();
        services.AddScoped<AccountService>();
        services.AddScoped<IAccountService>(sp => sp.GetRequiredService<AccountService>());
        services.AddScoped<Mfa.IMfaService, Mfa.MfaService>();
        services.AddScoped<Platform.ICompanySecurityService, Platform.CompanySecurityService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IRoleAdministrationService, RoleAdministrationService>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<Platform.ICompanyDirectory, Platform.CompanyDirectory>();
        services.AddScoped<Platform.ICompanyAdministrationService, Platform.CompanyAdministrationService>();
        // Platform screens: only the Platform Super Administrator (a flag on the account, never a company role).
        services.AddAuthorizationBuilder().AddPolicy(Platform.PlatformPolicies.SuperAdministrator,
            p => p.RequireAuthenticatedUser().RequireClaim(Claims.AppClaimTypes.PlatformAdmin, "true"));

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        return services;
    }
}
