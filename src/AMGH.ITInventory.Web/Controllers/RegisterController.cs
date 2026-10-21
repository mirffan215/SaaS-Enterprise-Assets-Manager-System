using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Company registration: someone creates their own company (its own database) and becomes its administrator.
/// They need a <b>company key</b> from the operator (given after a demo request) unless "Tenancy:AllowSelfRegistration" is true (open sign-up).
/// The key is used up when the company is created. Limited to a few attempts per address and hour.
/// </summary>
[AllowAnonymous]
[Route("Register")]
public sealed class RegisterController : Controller
{
    private readonly TenancyOptions _options;
    private readonly ITenantProvisioningService _provisioning;
    private readonly ICompanyKeyStore? _keys;

    public RegisterController(IOptions<TenancyOptions> options, ITenantProvisioningService provisioning, IServiceProvider services)
    {
        _options = options.Value;
        _provisioning = provisioning;
        _keys = services.GetService<ICompanyKeyStore>();
    }

    private bool Enabled => _options.Enabled && _keys is not null;

    /// <summary>Without open sign-up every registration needs a company key.</summary>
    private bool KeyRequired => !_options.AllowSelfRegistration;

    [HttpGet("")]
    public IActionResult Index(string? key = null) => Enabled ? View(new RegisterViewModel { CompanyKey = key, KeyRequired = KeyRequired }) : NotFound();

    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Create(RegisterViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();

        model.KeyRequired = KeyRequired;
        var keyGiven = !string.IsNullOrWhiteSpace(model.CompanyKey);
        if (KeyRequired && !keyGiven)
        {
            model.Message = "Enter the company key you received by e-mail. If you do not have one, request a demo first.";
            return View("Index", model);
        }
        var code = (model.Code ?? string.Empty).Trim();
        var reserved = false;
        if (keyGiven)
        {
            // the key is used up first, in one step, so it cannot be shared; it is given back below if the company cannot be created
            reserved = await _keys!.TryReserveAsync(model.CompanyKey!, code.Length == 0 ? "-" : code, CancellationToken.None);
            if (!reserved && KeyRequired)
            {
                model.AdminPassword = null;
                model.Message = "That company key is not valid: it may be mistyped, already used, withdrawn or expired. Ask for a new one.";
                return View("Index", model);
            }
        }

        // Not tied to the browser: closing the tab must not leave a half-built database behind (it takes a minute or two).
        var result = await _provisioning.ProvisionTenantAsync(code, model.Name ?? string.Empty, model.AdminEmail ?? string.Empty,
            model.AdminPassword ?? string.Empty, isDemo: false, cancellationToken: CancellationToken.None);
        model.AdminPassword = null;
        if (result.IsFailure)
        {
            if (reserved) await _keys!.ReleaseAsync(model.CompanyKey!, CancellationToken.None);
            model.Message = string.Join(" ", result.Errors.Select(e => e.Message));
            return View("Index", model);
        }
        TempData["StatusMessage"] = $"Company {result.Value.Name} is ready. Sign in with the administrator e-mail address (or the user name admin) and the password you chose.";
        return Redirect(TenantUrls.For(Request, _options, result.Value.Code, "/Account/Login"));
    }
}
