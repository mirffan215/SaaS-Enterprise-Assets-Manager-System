using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Company workspaces: directory, setup wizard, edit, suspend/archive. Platform Super Administrator only.</summary>
[Authorize(Policy = PlatformPolicies.SuperAdministrator)]
[Route("platform/companies")]
public sealed class PlatformController : Controller
{
    private readonly ICompanyAdministrationService _companies;
    private readonly ILookupService _lookups;
    private readonly ILogger<PlatformController> _logger;

    public PlatformController(ICompanyAdministrationService companies, ILookupService lookups, ILogger<PlatformController> logger)
    {
        _companies = companies;
        _lookups = lookups;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _companies.ListAsync(cancellationToken));

    // ------------------------------------------------------------------ wizard

    [HttpGet("new")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(await WizardModelAsync(new CompanySetupRequest(), cancellationToken));

    [BlockInDemo]
    [HttpPost("new")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Create([Bind(Prefix = "Setup")] CompanySetupRequest setup, IFormFile? logo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var file = await ReadLogoAsync(logo, cancellationToken);
        if (file is null && !string.IsNullOrWhiteSpace(setup.Details?.LogoUrl))
        {
            var fetched = await HttpContext.RequestServices.GetRequiredService<ILogoFetcher>().FetchAsync(setup.Details.LogoUrl, cancellationToken);
            if (fetched.IsFailure) { ModelState.AddModelError("Setup.Details.LogoUrl", fetched.FirstError!.Message); return View(await WizardModelAsync(setup, cancellationToken)); }
            file = fetched.Value;
        }
        var result = await _companies.CreateAsync(setup, file, cancellationToken);
        if (result.IsFailure)
        {
            ModelState.Clear();
            ModelState.AddErrors(result.Errors.Select(e => e with { Code = e.Code.Length > 0 && e.Code != "Logo" ? "Setup." + e.Code : e.Code }));
            return View(await WizardModelAsync(setup, cancellationToken));
        }
        _logger.LogWarning("Company {Code} created by {User}", result.Value.CompanyCode, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        return View("Created", result.Value);
    }

    // ------------------------------------------------------------------ edit / status

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var form = await _companies.GetAsync(id, cancellationToken);
        return form is null ? NotFound() : View(await EditModelAsync(id, form, cancellationToken));
    }

    [HttpPost("{id:int}")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Details")] CompanyDetailsForm details, IFormFile? logo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var file = await ReadLogoAsync(logo, cancellationToken);
        if (file is null && !string.IsNullOrWhiteSpace(details?.LogoUrl))
        {
            var fetched = await HttpContext.RequestServices.GetRequiredService<ILogoFetcher>().FetchAsync(details.LogoUrl, cancellationToken);
            if (fetched.IsFailure) { ModelState.AddModelError("Details.LogoUrl", fetched.FirstError!.Message); return View(await EditModelAsync(id, details, cancellationToken)); }
            file = fetched.Value;
        }
        var result = await _companies.UpdateAsync(id, details!, file, cancellationToken);
        if (result.IsSuccess) HttpContext.RequestServices.GetRequiredService<IDisplayTime>().Invalidate(id);
        if (result.IsSuccess)
        {
            TempData["StatusMessage"] = "Company details saved.";
            return RedirectToAction(nameof(Index));
        }
        ModelState.Clear();
        ModelState.AddErrors(result.Errors);
        var current = await _companies.GetAsync(id, cancellationToken);
        if (current is null) return NotFound();
        details!.CompanyCode = current.CompanyCode;
        details.AssetNumberPrefix = current.AssetNumberPrefix;
        return View(await EditModelAsync(id, details, cancellationToken));
    }

    [BlockInDemo]
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> Status(int id, string status, CancellationToken cancellationToken)
    {
        var result = await _companies.SetStatusAsync(id, status, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? $"Company status changed to {status}." : result.FirstError!.Message;
        if (result.IsSuccess) _logger.LogWarning("Company {Id} set to {Status} by {User}", id, status, User.Identity?.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/complete-setup")]
    public async Task<IActionResult> CompleteSetup(int id, CancellationToken cancellationToken)
    {
        var result = await _companies.CompleteSetupAsync(id, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "Setup completed: " + string.Join(", ", result.Value) + "." : result.FirstError!.Message;
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<CompanyWizardViewModel> WizardModelAsync(CompanySetupRequest setup, CancellationToken ct) => new()
    {
        Setup = setup,
        Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct),
        TimeZones = TimeZones(),
        Companies = (await _companies.ListAsync(ct)).Select(c => (c.CompanyId, c.DisplayName + " (" + c.CompanyCode + ")")).ToList()
    };

    private async Task<CompanyEditViewModel> EditModelAsync(int id, CompanyDetailsForm form, CancellationToken ct) => new()
    {
        CompanyId = id,
        Details = form,
        Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct),
        TimeZones = TimeZones(),
        Companies = (await _companies.ListAsync(ct)).Where(c => c.CompanyId != id).Select(c => (c.CompanyId, c.DisplayName + " (" + c.CompanyCode + ")")).ToList()
    };

    private static IReadOnlyList<(string Id, string Name)> TimeZones() =>
        TimeZoneInfo.GetSystemTimeZones().Select(z => (z.Id, z.DisplayName)).ToList();

    private static async Task<CompanyLogo?> ReadLogoAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > 2 * 1024 * 1024) return new CompanyLogo(new byte[CompanyLogoLimits.TooLarge], "application/octet-stream");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return new CompanyLogo(buffer.ToArray(), file.ContentType ?? "application/octet-stream");
    }
}

internal static class CompanyLogoLimits
{
    /// <summary>A buffer larger than the service limit, so the service reports "too large" without reading huge uploads.</summary>
    public const int TooLarge = 512 * 1024 + 1;
}
