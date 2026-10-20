using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Company self-registration: someone creates their own company (its own database) and becomes its administrator.
/// Exists only while "Tenancy:AllowSelfRegistration" is true, and is limited to a few registrations per address and hour.
/// </summary>
[AllowAnonymous]
[Route("Register")]
public sealed class RegisterController : Controller
{
    private readonly TenancyOptions _options;
    private readonly ITenantProvisioningService _provisioning;

    public RegisterController(IOptions<TenancyOptions> options, ITenantProvisioningService provisioning)
    {
        _options = options.Value;
        _provisioning = provisioning;
    }

    private bool Enabled => _options.Enabled && _options.AllowSelfRegistration;

    [HttpGet("")]
    public IActionResult Index() => Enabled ? View(new RegisterViewModel()) : NotFound();

    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Create(RegisterViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();

        // Not tied to the browser: closing the tab must not leave a half-built database behind (it takes a minute or two).
        var result = await _provisioning.ProvisionTenantAsync(model.Code ?? string.Empty, model.Name ?? string.Empty, model.AdminEmail ?? string.Empty,
            model.AdminPassword ?? string.Empty, isDemo: false, cancellationToken: CancellationToken.None);
        model.AdminPassword = null;
        if (result.IsFailure)
        {
            model.Message = string.Join(" ", result.Errors.Select(e => e.Message));
            return View("Index", model);
        }
        TempData["StatusMessage"] = $"Company {result.Value.Name} is ready. Sign in with the administrator e-mail address (or the user name admin) and the password you chose.";
        return Redirect(TenantUrls.For(Request, _options, result.Value.Code, "/Account/Login"));
    }
}
