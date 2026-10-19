using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

// No class-level [AllowAnonymous]: in ASP.NET Core it would override [Authorize] on every action. Public actions are marked one by one.
public sealed class AccountController : Controller
{
    private static readonly Dictionary<string, string> PasswordFieldMap = new(StringComparer.Ordinal)
    {
        ["current_password"] = nameof(ChangePasswordViewModel.CurrentPassword),
        ["new_password"] = nameof(ChangePasswordViewModel.NewPassword)
    };

    /// <summary>Remembers the last company (its code only) to skip the picker next time. Not used for authorisation.</summary>
    internal const string WorkspaceCookie = "AMGH.Workspace";
    /// <summary>"Remember me": the user name only (never a password or session), HttpOnly, 90 days.</summary>
    internal const string RememberUserCookie = "AMGH.RememberUser";

    private readonly IAccountService _accounts;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AccountController> _logger;
    private readonly ICompanyDirectory _directory;
    private readonly ITenantAccessor _tenants;
    private readonly TenancyOptions _tenancy;

    public AccountController(IAccountService accounts, IDateTimeProvider clock, ILogger<AccountController> logger, ICompanyDirectory directory,
        ITenantAccessor tenants, IOptions<TenancyOptions> tenancy)
    {
        _tenants = tenants;
        _tenancy = tenancy.Value;
        _directory = directory;
        _accounts = accounts;
        _clock = clock;
        _logger = logger;
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string UserAgent => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Step 1: choose the company (picker; skipped when only one company exists or one is remembered).
    /// Step 2: the company-branded sign-in form.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, bool expired = false, string? company = null, bool change = false,
        bool general = false, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(HomeController.Index), "Home");
        var companies = await _directory.ListActiveAsync(cancellationToken);
        // "Choose a company" (change) shows the picker; otherwise the remembered workspace, else direct sign-in
        if (change) return View("Workspace", new WorkspacePickerViewModel { Companies = companies, ReturnUrl = returnUrl });
        var code = company ?? (general ? null : Request.Cookies[WorkspaceCookie]);
        if (code is null && companies.Count == 1) code = companies[0].CompanyCode;
        if (code is null)
        {
            // Direct sign-in: user name and password only; the person's default company is used (Release 2.11).
            var direct = new LoginViewModel { ReturnUrl = returnUrl, SessionExpired = expired, CanChangeCompany = companies.Count > 1 };
            DescribeProduct(direct);
            Remembered(direct);
            ApplyDemoOptions(direct);
            return View(direct);
        }

        var model = new LoginViewModel { ReturnUrl = returnUrl, SessionExpired = expired, CompanyCode = code };
        if (!await DescribeCompanyAsync(model, companies.Count, cancellationToken))
        {
            Response.Cookies.Delete(WorkspaceCookie);
            return View("Workspace", new WorkspacePickerViewModel { Companies = companies, ReturnUrl = returnUrl,
                Message = "That company is not available. Choose your company." });
        }
        Remembered(model);
        ApplyDemoOptions(model);
        return View(model);
    }

    /// <summary>Demo hooks of the sign-in page: the "Explore Demo Environment" button, and, inside the demo tenant, the sandbox credentials.</summary>
    private void ApplyDemoOptions(LoginViewModel model)
    {
        var current = _tenants.CurrentTenant;
        model.ShowExploreDemo = _tenancy.Enabled && _tenancy.DemoEnabled && current is not { IsDemo: true };
        if (current is { IsDemo: true } && _tenancy.ShowDemoCredentials && !string.IsNullOrEmpty(_tenancy.DemoAdminPassword))
        {
            model.DemoUser = TenantProvisioningService.AdminUsername;
            model.DemoPassword = _tenancy.DemoAdminPassword;
        }
    }

    /// <summary>One-click demo: continues to the sign-in page of the "demo" workspace (sample data, destructive actions disabled).</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ExploreDemo()
    {
        if (!_tenancy.Enabled || !_tenancy.DemoEnabled) return NotFound();
        return Redirect(TenantUrls.For(Request, _tenancy, TenancyOptions.DemoCode, "/Account/Login"));
    }

    /// <summary>Pre-fills the user name remembered on this computer.</summary>
    private void Remembered(LoginViewModel model)
    {
        var remembered = Request.Cookies[RememberUserCookie];
        if (string.IsNullOrWhiteSpace(remembered) || remembered.Length > 256) return;
        model.Username = remembered;
        model.RememberMe = true;
    }

    /// <summary>Direct sign-in page with no company chosen: neutral product name and badge.</summary>
    private void DescribeProduct(LoginViewModel model)
    {
        model.CompanyName = AMGH.ITInventory.Shared.AppInfo.Name;
        model.LogoUrl = Url.Action("DefaultLogo", "Branding");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(string? company, CancellationToken cancellationToken)
    {
        var b = string.IsNullOrWhiteSpace(company) ? null : await _directory.GetBrandingAsync(company, cancellationToken);
        if (b is not null) ViewData["BrandingCode"] = b.CompanyCode;
        return View(new ForgotPasswordViewModel
        {
            CompanyCode = string.IsNullOrWhiteSpace(company) ? null : b?.CompanyCode,
            CompanyName = string.IsNullOrWhiteSpace(company) || b is null ? AMGH.ITInventory.Shared.AppInfo.Owner : b.DisplayName,
            ContactEmail = b?.ContactEmail, ContactPhone = b?.Phone
        });
    }

    /// <summary>Company-specific sign-in link, e.g. https://server/w/AMGH.</summary>
    [HttpGet("/w/{code}")]
    [AllowAnonymous]
    public IActionResult Workspace(string code) => RedirectToAction(nameof(Login), new { company = code });

    private async Task<bool> DescribeCompanyAsync(LoginViewModel model, int companyCount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyCode)) return false;
        var b = await _directory.GetBrandingAsync(model.CompanyCode, ct);
        if (b is null) return false;
        model.CompanyCode = b.CompanyCode;
        model.CompanyName = b.DisplayName;
        model.HasLogo = b.HasLogo;
        model.LoginTagline = b.LoginTagline;
        model.CanChangeCompany = companyCount > 1;
        model.NameArabic = b.NameArabic;
        model.LogoUrl = Url.Action("Logo", "Branding", new { code = b.CompanyCode }); // an uploaded logo, else the generated badge
        ViewData["BrandingCode"] = b.CompanyCode;
        return true;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var companies = await _directory.ListActiveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(model.CompanyCode))
        {
            model.CompanyCode = null;                       // direct sign-in: the default company is used
            model.CanChangeCompany = companies.Count > 1;
            DescribeProduct(model);
        }
        else if (!await DescribeCompanyAsync(model, companies.Count, cancellationToken))
            return View("Workspace", new WorkspacePickerViewModel { Companies = companies, ReturnUrl = model.ReturnUrl,
                Message = "That company is not available. Choose your company." });
        ApplyDemoOptions(model);
        if (!ModelState.IsValid) return View(model);

        var outcome = await _accounts.SignInAsync(model.Username, model.Password, ClientIp, UserAgent, model.CompanyCode, cancellationToken);
        model.Password = string.Empty;

        if (!outcome.Succeeded)
        {
            var message = outcome.Status == SignInStatus.LockedOut && outcome.LockedUntilUtc is { } until
                ? $"{outcome.Message} Try again in {Math.Max(1, (int)Math.Ceiling((until - _clock.UtcNow).TotalMinutes))} minute(s), or ask the IT department to unlock it."
                : outcome.Message ?? "Sign-in failed.";
            ModelState.AddModelError(string.Empty, message);
            return View(model);
        }

        await IssueCookieAsync(outcome.User!);
        // "Remember me": keep only the user name as typed (name or e-mail) for next time; unticked removes it
        if (model.RememberMe)
            Response.Cookies.Append(RememberUserCookie, model.Username.Trim(), new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(90)
            });
        else Response.Cookies.Delete(RememberUserCookie);
        _logger.LogInformation("User {User} signed in to company {Company}", outcome.User!.Username, outcome.User.CompanyCode);
        Response.Cookies.Append(WorkspaceCookie, outcome.User.CompanyCode, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        // People with access to several companies: say where they are and how to go elsewhere.
        var others = (await _accounts.GetMembershipsAsync(outcome.User.UserId, cancellationToken))
            .Where(m => m.CompanyId != outcome.User.CompanyId).Select(m => m.CompanyName).ToList();
        if (others.Count > 0)
            TempData["StatusMessage"] = $"Signed in to {outcome.User.CompanyName}. You also have access to {string.Join(", ", others)}: use Switch company in your profile menu.";

        if (outcome.User.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _accounts.LogEventAsync(User.GetUserId(), User.GetCompanyId(), User.Identity?.Name ?? string.Empty, LoginEventType.Logout,
            ClientIp, UserAgent, cancellationToken: cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Switching company signs out and returns to the sign-in page of the chosen company: access to another
    /// company always goes through authentication again.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SwitchCompany(string? company, CancellationToken cancellationToken)
    {
        await _accounts.LogEventAsync(User.GetUserId(), User.GetCompanyId(), User.Identity?.Name ?? string.Empty, LoginEventType.Logout,
            ClientIp, UserAgent, "Switching company", cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return string.IsNullOrWhiteSpace(company)
            ? RedirectToAction(nameof(Login), new { change = true })
            : RedirectToAction(nameof(Login), new { company });
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() =>
        View(new ChangePasswordViewModel { IsForced = User.MustChangePassword() });

    [BlockInDemo]
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.IsForced = User.MustChangePassword();
        if (!ModelState.IsValid) return View(model);

        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (userId is null || companyId is null) return Challenge();

        var result = await _accounts.ChangePasswordAsync(userId.Value, companyId.Value, model.CurrentPassword, model.NewPassword,
            ClientIp, UserAgent, cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddErrors(result.Errors, PasswordFieldMap);
            return View(new ChangePasswordViewModel { IsForced = model.IsForced });
        }

        // The security stamp changed: issue a fresh cookie (this also ends other sessions of this user).
        await IssueCookieAsync(result.Value);
        TempData["StatusMessage"] = "Your password has been changed.";
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Called by the session-timeout dialog to keep an active session alive.</summary>
    [HttpGet]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult KeepAlive() => NoContent();

    private Task IssueCookieAsync(AuthenticatedUser user)
    {
        var now = _clock.UtcNow;
        var principal = PrincipalFactory.Create(user, CookieAuthenticationDefaults.AuthenticationScheme, now, _tenants.CurrentTenant?.Code);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true, IssuedUtc = now });
    }
}
