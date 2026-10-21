using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Application.Abstractions.QrCodes;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Security.Mfa;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Infrastructure.Sso;
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
    internal const string WorkspaceCookie = "EAMS.Workspace";
    /// <summary>"Remember me": the user name only (never a password or session), HttpOnly, 90 days.</summary>
    internal const string RememberUserCookie = "EAMS.RememberUser";

    private readonly IAccountService _accounts;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AccountController> _logger;
    private readonly ICompanyDirectory _directory;
    private readonly ITenantAccessor _tenants;
    private readonly TenancyOptions _tenancy;
    private readonly TenantSignInService _globalSignIn;
    private readonly IMfaService _mfa;
    private readonly SignInGate _gate;
    private readonly MfaPending _pending;
    private readonly IQrCodeGenerator _qr;
    private readonly ICompanySecurityService _security;
    private readonly OidcClient _oidc;
    private readonly SsoState _sso;

    public AccountController(IAccountService accounts, IDateTimeProvider clock, ILogger<AccountController> logger, ICompanyDirectory directory,
        ITenantAccessor tenants, IOptions<TenancyOptions> tenancy, TenantSignInService globalSignIn, IMfaService mfa, SignInGate gate, MfaPending pending,
        IQrCodeGenerator qr, ICompanySecurityService security, OidcClient oidc, SsoState sso)
    {
        _oidc = oidc;
        _sso = sso;
        _mfa = mfa;
        _gate = gate;
        _pending = pending;
        _qr = qr;
        _security = security;
        _globalSignIn = globalSignIn;
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
        // Multi-tenant and no company known yet: plain user name and password; the company is found from them.
        if (IsGlobalSignIn) return GlobalLoginView(returnUrl, expired);
        if (NoCompanyYet) return RedirectToAction("Select", "Tenant");   // company first: the company-code page
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
        model.ShowOtherOrganisation = _tenancy.Enabled && current is not null;
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
        model.ShowSso = await SsoReadyAsync(b.CompanyCode, ct);
        ViewData["BrandingCode"] = b.CompanyCode;
        return true;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (IsGlobalSignIn) return await GlobalLoginAsync(model, cancellationToken);
        if (NoCompanyYet) return RedirectToAction("Select", "Tenant");
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

        return await ContinueSignInAsync(outcome.User!, _tenants.CurrentTenant, null, model.Username, model.RememberMe, model.ReturnUrl, model, cancellationToken);
    }

    /// <summary>
    /// After a correct password: the company's address list, then the second step (code) or its setup when the company requires it, then the session.
    /// <paramref name="tenantCookieFor"/> is set for the "no company chosen" sign-in, which remembers the company it found.
    /// </summary>
    private async Task<IActionResult> ContinueSignInAsync(AuthenticatedUser user, TenantInfo? tenant, TenantInfo? tenantCookieFor, string typedUsername, bool remember,
        string? returnUrl, LoginViewModel model, CancellationToken ct)
    {
        var gate = await _gate.EvaluateAsync(tenant ?? tenantCookieFor, user, ClientIp, viaSso: false, ct);
        if (gate == GateResult.IpDenied)
        {
            await LogInTenantAsync(tenant ?? tenantCookieFor, user, "Blocked: address not in the company's allow-list", ct);
            ModelState.AddModelError(string.Empty, "Sign-in from this network is not allowed for this company. Connect from an approved network or contact your administrator.");
            return View(model);
        }
        if (tenantCookieFor is not null)
            Response.Cookies.Append(TenantResolutionMiddleware.TenantCookie, tenantCookieFor.Code, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(90)
            });
        if (gate == GateResult.Challenge)
        {
            _pending.Start(HttpContext, new MfaPendingState(user.UserId, user.CompanyId, user.CompanyCode, (tenant ?? tenantCookieFor)?.Code, typedUsername, remember,
                Url.IsLocalUrl(returnUrl) ? returnUrl : null));
            return RedirectToAction(nameof(Mfa));
        }
        return await FinishSignInAsync(user, tenantCookieFor?.Code, typedUsername, remember, returnUrl, gate == GateResult.SetupRequired, ct);
    }

    private async Task LogInTenantAsync(TenantInfo? tenant, AuthenticatedUser user, string reason, CancellationToken ct)
    {
        // the audit row belongs in the company's own database (the request may not be inside it yet in the "no company chosen" sign-in)
        var scopes = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopes.CreateScope();
        using var entered = tenant is null ? null : scope.ServiceProvider.GetRequiredService<ITenantAccessor>().Enter(tenant);
        await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .LogEventAsync(user.UserId, user.CompanyId, user.Username, LoginEventType.LoginFailed, ClientIp, UserAgent, reason, ct);
    }

    /// <summary>Issues the session and the convenience cookies, then goes to the right first page.</summary>
    private async Task<IActionResult> FinishSignInAsync(AuthenticatedUser user, string? tenantCode, string typedUsername, bool remember, string? returnUrl,
        bool mfaSetupRequired, CancellationToken ct)
    {
        await IssueCookieAsync(user, tenantCode, mfaSetupRequired);
        // "Remember me": keep only the user name as typed (name or e-mail) for next time; unticked removes it
        if (remember)
            Response.Cookies.Append(RememberUserCookie, typedUsername.Trim(), new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(90)
            });
        else Response.Cookies.Delete(RememberUserCookie);
        _logger.LogInformation("User {User} signed in to company {Company}", user.Username, user.CompanyCode);
        Response.Cookies.Append(WorkspaceCookie, user.CompanyCode, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        // People with access to several companies: say where they are and how to go elsewhere (not in the "no company chosen" sign-in: that request is outside the company).
        if (tenantCode is null)
        {
            var others = (await _accounts.GetMembershipsAsync(user.UserId, ct)).Where(m => m.CompanyId != user.CompanyId).Select(m => m.CompanyName).ToList();
            if (others.Count > 0)
                TempData["StatusMessage"] = $"Signed in to {user.CompanyName}. You also have access to {string.Join(", ", others)}: use Switch company in your profile menu.";
        }

        if (user.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        if (mfaSetupRequired) return RedirectToAction(nameof(MfaSetup));
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>Multi-tenant mode with no company chosen yet (no sub-domain, no remembered company).</summary>
    private bool NoCompanyYet => _tenants.IsMultiTenant && _tenants.CurrentTenant is null;

    private bool IsGlobalSignIn => NoCompanyYet && _tenancy.GlobalSignIn;

    private IActionResult GlobalLoginView(string? returnUrl, bool expired)
    {
        var model = new LoginViewModel { ReturnUrl = returnUrl, SessionExpired = expired };
        PrepareGlobal(model);
        Remembered(model);
        return View(model);
    }

    private void PrepareGlobal(LoginViewModel model)
    {
        DescribeProduct(model);
        model.CompanyCode = null;
        model.GlobalSignIn = true;
        model.ShowRegister = _tenancy.Enabled;
        ApplyDemoOptions(model);
    }

    /// <summary>Checks the user name and password in every company and signs the person in to the one where they are correct.</summary>
    private async Task<IActionResult> GlobalLoginAsync(LoginViewModel model, CancellationToken cancellationToken)
    {
        PrepareGlobal(model);
        if (!ModelState.IsValid) return View(model);

        var result = await _globalSignIn.SignInAsync(model.Username, model.Password, ClientIp, UserAgent, cancellationToken);
        model.Password = string.Empty;
        if (result.TooManyCompanies)
        {
            TempData["ErrorMessage"] = "Please sign in through your company's own address, or enter your company code.";
            return RedirectToAction("Select", "Tenant");
        }
        if (result.Successes.Count == 0)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }
        if (result.Successes.Count > 1)
        {
            TempData["ErrorMessage"] = "Those sign-in details work in more than one company. Enter the code of the company you want.";
            return RedirectToAction("Select", "Tenant");
        }

        var win = result.Successes[0];
        return await ContinueSignInAsync(win.User, null, win.Tenant, model.Username, model.RememberMe, model.ReturnUrl, model, cancellationToken);
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
        await IssueCookieAsync(result.Value, null, User.MfaSetupRequired());
        TempData["StatusMessage"] = "Your password has been changed.";
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    // ---------------------------------------------------------------- single sign-on (OpenID Connect)
    private async Task<bool> SsoReadyAsync(string companyCode, CancellationToken ct) =>
        await _security.GetByCodeAsync(companyCode, ct) is { SsoEnabled: true, SsoAuthority: not null, SsoClientId: not null, SsoSecretSet: true };

    private string SsoRedirectUri => $"{Request.Scheme}://{Request.Host}/Account/SsoCallback";

    /// <summary>Sends the browser to the company's identity provider.</summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Sso(string? company, string? returnUrl, CancellationToken cancellationToken)
    {
        if (NoCompanyYet) return RedirectToAction("Select", "Tenant");
        var code = company ?? Request.Cookies[WorkspaceCookie];
        if (string.IsNullOrWhiteSpace(code)) { var all = await _directory.ListActiveAsync(cancellationToken); if (all.Count == 1) code = all[0].CompanyCode; }
        var security = string.IsNullOrWhiteSpace(code) ? null : await _security.GetByCodeAsync(code, cancellationToken);
        if (security is not { SsoEnabled: true, SsoAuthority: not null, SsoClientId: not null } || await _security.GetSsoSecretAsync(security.CompanyId, cancellationToken) is not { } secret)
        {
            TempData["ErrorMessage"] = "Single sign-on is not set up for this company.";
            return RedirectToAction(nameof(Login), new { company });
        }
        var state = OidcClient.RandomToken();
        var nonce = OidcClient.RandomToken();
        var (verifier, challenge) = OidcClient.NewPkce();
        Uri url;
        try { url = await _oidc.BuildAuthorizeUrlAsync(new OidcSettings(security.SsoAuthority, security.SsoClientId, secret, security.SsoAllowedDomain), SsoRedirectUri, state, nonce, challenge, cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Single sign-on provider of {Company} could not be reached", security.CompanyCode);
            TempData["ErrorMessage"] = "The single sign-on provider could not be reached. Try again, or sign in with your password.";
            return RedirectToAction(nameof(Login), new { company = security.CompanyCode });
        }
        _sso.Start(HttpContext, new SsoStateData(state, nonce, verifier, _tenants.CurrentTenant?.Code, security.CompanyCode, Url.IsLocalUrl(returnUrl) ? returnUrl : null));
        return Redirect(url.ToString());
    }

    /// <summary>The provider sends the person back here with a one-time code.</summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> SsoCallback(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        var saved = _sso.Take(HttpContext);
        if (saved is null || string.IsNullOrEmpty(state) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(saved.State), System.Text.Encoding.UTF8.GetBytes(state))
            || !string.Equals(saved.TenantCode, _tenants.CurrentTenant?.Code, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "That single sign-on attempt expired or did not start here. Please try again.";
            return RedirectToAction(nameof(Login));
        }
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            TempData["ErrorMessage"] = "Single sign-on was cancelled or refused by your provider.";
            return RedirectToAction(nameof(Login), new { company = saved.CompanyCode });
        }
        var security = await _security.GetByCodeAsync(saved.CompanyCode, cancellationToken);
        var secret = security is null ? null : await _security.GetSsoSecretAsync(security.CompanyId, cancellationToken);
        if (security is not { SsoEnabled: true, SsoAuthority: not null, SsoClientId: not null } || secret is null)
        {
            TempData["ErrorMessage"] = "Single sign-on is not set up for this company.";
            return RedirectToAction(nameof(Login), new { company = saved.CompanyCode });
        }
        var identity = await _oidc.RedeemAsync(new OidcSettings(security.SsoAuthority, security.SsoClientId, secret, security.SsoAllowedDomain), SsoRedirectUri, code, saved.Verifier, saved.Nonce, cancellationToken);
        if (identity.IsFailure)
        {
            TempData["ErrorMessage"] = identity.FirstError!.Message;
            return RedirectToAction(nameof(Login), new { company = saved.CompanyCode });
        }
        var outcome = await _accounts.SignInExternalAsync(identity.Value.Email, saved.CompanyCode, ClientIp, UserAgent, cancellationToken);
        if (!outcome.Succeeded)
        {
            TempData["ErrorMessage"] = outcome.Message ?? "Sign-in failed.";
            return RedirectToAction(nameof(Login), new { company = saved.CompanyCode });
        }
        if (await _gate.EvaluateAsync(_tenants.CurrentTenant, outcome.User!, ClientIp, viaSso: true, cancellationToken) == GateResult.IpDenied)
        {
            await LogInTenantAsync(_tenants.CurrentTenant, outcome.User!, "Blocked: address not in the company's allow-list", cancellationToken);
            TempData["ErrorMessage"] = "Sign-in from this network is not allowed for this company. Connect from an approved network or contact your administrator.";
            return RedirectToAction(nameof(Login), new { company = saved.CompanyCode });
        }
        return await FinishSignInAsync(outcome.User!, null, identity.Value.Email, remember: false, saved.ReturnUrl, mfaSetupRequired: false, cancellationToken);
    }

    // ---------------------------------------------------------------- two-step verification
    /// <summary>The second step of sign-in: the code from the authenticator app (or a recovery code).</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Mfa() =>
        _pending.Read(HttpContext) is null ? RedirectToAction(nameof(Login)) : View(new MfaViewModel());

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Mfa(MfaViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var pending = _pending.Read(HttpContext);
        if (pending is null) return RedirectToAction(nameof(Login));
        // the code belongs to the company the password was entered for
        if (!string.Equals(pending.TenantCode, _tenants.CurrentTenant?.Code, StringComparison.OrdinalIgnoreCase)) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) return View(new MfaViewModel());

        var check = await _mfa.VerifyAsync(pending.UserId, pending.CompanyId, model.Code ?? string.Empty, ClientIp, UserAgent, cancellationToken);
        if (check.Result == MfaResult.Locked)
        {
            MfaPending.Clear(HttpContext);
            TempData["ErrorMessage"] = "Too many wrong codes. This account is locked for a while; try again later or ask your administrator to unlock it.";
            return RedirectToAction(nameof(Login));
        }
        if (check.Result == MfaResult.Wrong)
        {
            ModelState.AddModelError(nameof(MfaViewModel.Code), "That code is not correct. Enter the 6 digits shown in your authenticator app, or a recovery code.");
            return View(new MfaViewModel());
        }
        var user = await _accounts.CreateSessionAsync(pending.UserId, pending.CompanyId, cancellationToken);
        MfaPending.Clear(HttpContext);
        if (user is null) return RedirectToAction(nameof(Login));
        if (check.UsedRecoveryCode) TempData["StatusMessage"] = "You signed in with a recovery code. Make a new set under Two-step verification when you can.";
        return await FinishSignInAsync(user, null, pending.Username, pending.Remember, pending.ReturnUrl, mfaSetupRequired: false, cancellationToken);
    }

    /// <summary>The person's own two-step verification page.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var model = await SecurityModelAsync(cancellationToken);
        return model is null ? Challenge() : View(model);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> MfaSetup(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId) return Challenge();
        if ((await _mfa.GetStateAsync(userId, cancellationToken)).Enabled) return RedirectToAction(nameof(Security));
        return await SetupViewAsync(userId, null, cancellationToken);
    }

    [BlockInDemo]
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> MfaSetup(MfaViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (User.GetUserId() is not { } userId || User.GetCompanyId() is not { } companyId) return Challenge();
        var codes = ModelState.IsValid ? await _mfa.ConfirmEnrollmentAsync(userId, companyId, model.Code ?? string.Empty, ClientIp, UserAgent, cancellationToken) : null;
        if (codes is null) return await SetupViewAsync(userId, "That code is not correct. Check that the phone's clock is automatic, then enter the current 6 digits.", cancellationToken);

        // the security stamp changed: a fresh session for this browser, without the "set it up first" restriction
        var user = await _accounts.CreateSessionAsync(userId, companyId, cancellationToken);
        if (user is null) return Challenge();
        await IssueCookieAsync(user);
        return View("MfaCodes", new MfaCodesViewModel { Codes = codes, Heading = "Two-step verification is on" });
    }

    [BlockInDemo]
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> MfaCodes(MfaViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (User.GetUserId() is not { } userId || User.GetCompanyId() is not { } companyId) return Challenge();
        var check = await _mfa.VerifyAsync(userId, companyId, model.Code ?? string.Empty, ClientIp, UserAgent, cancellationToken);
        if (check.Result != MfaResult.Ok)
        {
            TempData["ErrorMessage"] = check.Result == MfaResult.Locked ? "Too many wrong codes; try again later." : "That code is not correct, so no new recovery codes were made.";
            return RedirectToAction(nameof(Security));
        }
        return View("MfaCodes", new MfaCodesViewModel { Codes = await _mfa.RegenerateRecoveryCodesAsync(userId, cancellationToken), Heading = "New recovery codes" });
    }

    [BlockInDemo]
    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> MfaDisable(MfaViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (User.GetUserId() is not { } userId || User.GetCompanyId() is not { } companyId) return Challenge();
        var security = await _security.GetAsync(companyId, cancellationToken);
        if (security is not null && SignInGate.Required(security, SignInGate.IsAdministrator(User.IsPlatformAdmin(), User.FindAll(AppClaimTypes.Permission).Select(c => c.Value))))
        {
            TempData["ErrorMessage"] = "Your company requires two-step verification, so it cannot be turned off.";
            return RedirectToAction(nameof(Security));
        }
        var check = await _mfa.VerifyAsync(userId, companyId, model.Code ?? string.Empty, ClientIp, UserAgent, cancellationToken);
        if (check.Result != MfaResult.Ok)
        {
            TempData["ErrorMessage"] = "That code is not correct, so two-step verification was left on.";
            return RedirectToAction(nameof(Security));
        }
        await _mfa.ResetAsync(userId, companyId, "the user", ClientIp, UserAgent, cancellationToken);
        var user = await _accounts.CreateSessionAsync(userId, companyId, cancellationToken);
        if (user is null) return Challenge();
        await IssueCookieAsync(user);
        TempData["StatusMessage"] = "Two-step verification is off.";
        return RedirectToAction(nameof(Security));
    }

    private async Task<IActionResult> SetupViewAsync(int userId, string? error, CancellationToken ct)
    {
        var enrollment = await _mfa.BeginEnrollmentAsync(userId, $"{User.Identity?.Name}@{User.GetCompanyCode()}", AMGH.ITInventory.Shared.AppInfo.Name, ct);
        if (enrollment is null) return RedirectToAction(nameof(Security));
        if (error is not null) ModelState.AddModelError(nameof(MfaViewModel.Code), error);
        return View("MfaSetup", new MfaSetupViewModel
        {
            Secret = string.Join(' ', Enumerable.Range(0, (enrollment.Secret.Length + 3) / 4).Select(i => enrollment.Secret.Substring(i * 4, Math.Min(4, enrollment.Secret.Length - i * 4)))),
            QrSvg = _qr.Generate(enrollment.Uri, 4).Svg,
            Forced = User.MfaSetupRequired()
        });
    }

    private async Task<SecurityViewModel?> SecurityModelAsync(CancellationToken ct)
    {
        if (User.GetUserId() is not { } userId || User.GetCompanyId() is not { } companyId) return null;
        var security = await _security.GetAsync(companyId, ct);
        var required = security is not null && SignInGate.Required(security, SignInGate.IsAdministrator(User.IsPlatformAdmin(), User.FindAll(AppClaimTypes.Permission).Select(c => c.Value)));
        return new SecurityViewModel { State = await _mfa.GetStateAsync(userId, ct), RequiredByCompany = required };
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Called by the session-timeout dialog to keep an active session alive.</summary>
    [HttpGet]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult KeepAlive() => NoContent();

    private Task IssueCookieAsync(AuthenticatedUser user, string? tenantCode = null, bool mfaSetupRequired = false)
    {
        var now = _clock.UtcNow;
        var principal = PrincipalFactory.Create(user, CookieAuthenticationDefaults.AuthenticationScheme, now, tenantCode ?? _tenants.CurrentTenant?.Code, mfaSetupRequired);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true, IssuedUtc = now });
    }
}
