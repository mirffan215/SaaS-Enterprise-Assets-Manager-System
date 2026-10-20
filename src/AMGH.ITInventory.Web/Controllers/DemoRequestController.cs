using System.ComponentModel.DataAnnotations;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

public sealed class DemoRequestViewModel
{
    [Required(ErrorMessage = "Enter your name.")] [StringLength(150)] public string? FullName { get; set; }
    [Required(ErrorMessage = "Enter your company name.")] [StringLength(150)] public string? CompanyName { get; set; }
    [Required(ErrorMessage = "Enter your e-mail address.")] [EmailAddress(ErrorMessage = "Enter a valid e-mail address.")] [StringLength(256)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(1000)] public string? Message { get; set; }
    /// <summary>A field people never see: bots fill it in.</summary>
    [StringLength(100)] public string? Website { get; set; }
}

/// <summary>
/// "Request a demo" on the front page. The request is saved for the operator (Super Admin page) and mailed to Tenancy:OperatorEmail when mail is set up.
/// The operator answers with a company key. Rate-limited per address.
/// </summary>
[AllowAnonymous]
[Route("RequestDemo")]
public sealed class DemoRequestController : Controller
{
    private readonly TenancyOptions _options;
    private readonly ICompanyKeyStore? _store;
    private readonly PlatformMailer _mailer;

    public DemoRequestController(IOptions<TenancyOptions> options, IServiceProvider services, PlatformMailer mailer)
    {
        _options = options.Value;
        _store = services.GetService<ICompanyKeyStore>();
        _mailer = mailer;
    }

    private bool Enabled => _options.Enabled && _store is not null;

    [HttpGet("")]
    public IActionResult Index() => Enabled ? View(new DemoRequestViewModel()) : NotFound();

    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<IActionResult> Send(DemoRequestViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        if (!string.IsNullOrWhiteSpace(model.Website)) return RedirectToAction(nameof(Thanks)); // a bot: pretend it worked
        if (!ModelState.IsValid) return View("Index", model);

        await _store!.AddRequestAsync(model.FullName!, model.CompanyName!, model.Email!, model.Phone, model.Message, HttpContext.Connection.RemoteIpAddress?.ToString(), CancellationToken.None);
        if (_mailer.OperatorEmail is { } operatorEmail)
        {
            var text = $"New demo request\n\nName: {model.FullName}\nCompany: {model.CompanyName}\nE-mail: {model.Email}\nPhone: {model.Phone}\n\n{model.Message}\n\nGenerate a company key for them on the Super Admin page (/Tenant/Admin).";
            var html = "<p><strong>New demo request</strong></p><table>" +
                $"<tr><td>Name</td><td>{PlatformMailer.Html(model.FullName!)}</td></tr><tr><td>Company</td><td>{PlatformMailer.Html(model.CompanyName!)}</td></tr>" +
                $"<tr><td>E-mail</td><td>{PlatformMailer.Html(model.Email!)}</td></tr><tr><td>Phone</td><td>{PlatformMailer.Html(model.Phone ?? "")}</td></tr></table>" +
                $"<p>{PlatformMailer.Html(model.Message ?? "")}</p><p>Generate a company key for them on the Super Admin page (/Tenant/Admin).</p>";
            await _mailer.TrySendAsync(operatorEmail, "EAMS demo request: " + model.CompanyName, html, text, model.Email, CancellationToken.None);
        }
        return RedirectToAction(nameof(Thanks));
    }

    [HttpGet("Thanks")]
    public IActionResult Thanks() => Enabled ? View() : NotFound();
}
