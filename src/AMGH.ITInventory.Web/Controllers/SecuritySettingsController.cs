using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Security.Mfa;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Administration, Sign-in security: two-step verification policy, allowed networks and single sign-on for the company.</summary>
[HasPermission(Permissions.Administration.SystemSettings)]
[Route("admin/security")]
public sealed class SecuritySettingsController : Controller
{
    private readonly ICompanySecurityService _security;
    private readonly IMfaService _mfa;
    private readonly IMemoryCache _cache;
    private readonly ITenantAccessor _tenants;

    public SecuritySettingsController(ICompanySecurityService security, IMfaService mfa, IMemoryCache cache, ITenantAccessor tenants)
    {
        _security = security;
        _mfa = mfa;
        _cache = cache;
        _tenants = tenants;
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("")]
    public Task<IActionResult> Index() => ViewAsync(null, null, false);

    [BlockInDemo]
    [HttpPost("save")]
    public async Task<IActionResult> Save(SecuritySettingsViewModel form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (User.GetCompanyId() is not { } companyId) return Challenge();
        var result = await _security.SaveAsync(companyId, new CompanySecurityUpdate(form.MfaPolicy, form.AllowedIpRanges, form.SsoEnabled, form.SsoAuthority,
            form.SsoClientId, form.SsoClientSecret, form.SsoAllowedDomain, ClientIp), User.Identity?.Name ?? "unknown", cancellationToken);
        if (result.IsFailure) return await ViewAsync(form, result.FirstError!.Message, false);
        _cache.Remove(IpAllowListMiddleware.CacheKey(_tenants.CurrentTenant?.Code, companyId));
        return await ViewAsync(null, "Saved.", true);
    }

    [BlockInDemo]
    [HttpPost("reset/{userId:int}")]
    public async Task<IActionResult> Reset(int userId, CancellationToken cancellationToken)
    {
        if (User.GetCompanyId() is not { } companyId) return Challenge();
        // only people of this company
        var people = await _mfa.ListAsync(companyId, cancellationToken);
        if (!people.Any(p => p.UserId == userId)) return NotFound();
        await _mfa.ResetAsync(userId, companyId, User.Identity?.Name ?? "an administrator", ClientIp, Request.Headers.UserAgent.ToString(), cancellationToken);
        return await ViewAsync(null, "Two-step verification was reset for that person. They sign in with their password and set it up again.", true);
    }

    private async Task<IActionResult> ViewAsync(SecuritySettingsViewModel? form, string? message, bool succeeded)
    {
        if (User.GetCompanyId() is not { } companyId) return Challenge();
        var current = await _security.GetAsync(companyId);
        if (current is null) return NotFound();
        var model = form ?? new SecuritySettingsViewModel
        {
            MfaPolicy = current.MfaPolicy, AllowedIpRanges = current.AllowedIpRanges, SsoEnabled = current.SsoEnabled, SsoAuthority = current.SsoAuthority,
            SsoClientId = current.SsoClientId, SsoAllowedDomain = current.SsoAllowedDomain
        };
        model.SsoSecretSet = current.SsoSecretSet;
        model.CurrentIp = ClientIp;
        model.RedirectUri = $"{Request.Scheme}://{Request.Host}/Account/SsoCallback";
        model.People = await _mfa.ListAsync(companyId);
        model.Message = message;
        model.Succeeded = succeeded;
        return View("Index", model);
    }
}
