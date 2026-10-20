using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
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

    /// <summary>"Not your organisation?": forget the remembered company and go to the plain sign-in page.</summary>
    [HttpGet("Forget")]
    public IActionResult Forget()
    {
        Response.Cookies.Delete(Infrastructure.TenantResolutionMiddleware.TenantCookie);
        return Redirect("/Account/Login");
    }

    [HttpGet("Select")]
    public IActionResult Select(string? notfound)
    {
        if (_catalog is null) return NotFound();
        var model = new TenantSelectViewModel { ShowExploreDemo = _options.DemoEnabled };
        if (!string.IsNullOrWhiteSpace(notfound) && CodePattern().IsMatch(notfound))
            ModelState.AddModelError(nameof(model.Code), notfound.Equals(TenancyOptions.DemoCode, StringComparison.OrdinalIgnoreCase)
                ? "The demo company is not ready yet. It is created in the background after the site starts; try again in a few minutes."
                : $"We could not find the company \"{notfound}\". Check the company code.");
        return View(model);
    }

    [HttpPost("Select")]
    public async Task<IActionResult> Select(TenantSelectViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_catalog is null) return NotFound();
        var code = model.Code?.Trim() ?? string.Empty;
        if (!CodePattern().IsMatch(code) || await _catalog.FindActiveAsync(code, cancellationToken) is not { } tenant)
        {
            ModelState.AddModelError(nameof(model.Code), "We could not find that company. Check the company code.");
            model.ShowExploreDemo = _options.DemoEnabled;
            return View(model);
        }
        return Redirect(TenantUrls.For(Request, _options, tenant.Code));
    }
}
