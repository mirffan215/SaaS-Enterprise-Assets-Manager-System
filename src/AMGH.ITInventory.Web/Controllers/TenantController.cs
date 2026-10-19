using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Multi-tenant entry page: the person names their company (its code), then continues to that company's sign-in.</summary>
[AllowAnonymous]
[Route("Tenant")]
public sealed partial class TenantController : Controller
{
    [GeneratedRegex("^[A-Za-z0-9]{2,10}$")] private static partial Regex CodePattern();

    private readonly ITenantCatalog? _catalog;
    private readonly TenancyOptions _options;

    public TenantController(IOptions<TenancyOptions> options, IServiceProvider services)
    {
        _options = options.Value;
        _catalog = services.GetService<ITenantCatalog>();
    }

    [HttpGet("Select")]
    public IActionResult Select() => _catalog is null ? NotFound() : View(new TenantSelectViewModel());

    [HttpPost("Select")]
    public async Task<IActionResult> Select(TenantSelectViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_catalog is null) return NotFound();
        var code = model.Code?.Trim() ?? string.Empty;
        if (!CodePattern().IsMatch(code) || await _catalog.FindActiveAsync(code, cancellationToken) is not { } tenant)
        {
            ModelState.AddModelError(nameof(model.Code), "We could not find that company. Check the company code.");
            return View(model);
        }
        if (!string.IsNullOrWhiteSpace(_options.BaseDomain) && !_options.AllowQueryParameter)
        {
            var host = $"{tenant.Code}.{_options.BaseDomain.Trim().TrimStart('.')}";
            var port = Request.Host.Port is { } p and not 80 and not 443 ? ":" + p : string.Empty;
            return Redirect($"{Request.Scheme}://{host}{port}/");
        }
        return Redirect($"/?{Infrastructure.TenantResolutionMiddleware.QueryName}={Uri.EscapeDataString(tenant.Code)}");
    }
}
