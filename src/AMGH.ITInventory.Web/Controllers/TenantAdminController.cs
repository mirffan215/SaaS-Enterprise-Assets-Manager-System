using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Operator page: the person running the installation creates customer companies (each with its own database), backs them up and restores them.
/// The operator key ("Tenancy:OperatorKey") is entered once and opens a 30-minute operator session; the page is rate-limited and exists only while a
/// key is configured. It is not a company screen: customers never see it.
/// </summary>
[AllowAnonymous]
[Route("Tenant/Admin")]
public sealed class TenantAdminController : Controller
{
    private readonly TenancyOptions _options;
    private readonly ITenantCatalog? _catalog;
    private readonly ITenantProvisioningService _provisioning;
    private readonly ITenantBackupService _backups;
    private readonly ITenantMigrationService _migrations;
    private readonly OperatorSession _session;
    private readonly IServiceProvider _services;
    private readonly PlatformMailer _mailer;

    public TenantAdminController(IOptions<TenancyOptions> options, IServiceProvider services, ITenantProvisioningService provisioning,
        ITenantBackupService backups, ITenantMigrationService migrations, OperatorSession session, PlatformMailer mailer)
    {
        _options = options.Value;
        _catalog = services.GetService<ITenantCatalog>();
        _provisioning = provisioning;
        _backups = backups;
        _migrations = migrations;
        _session = session;
        _services = services;
        _mailer = mailer;
    }

    private bool Enabled => _catalog is not null && !string.IsNullOrWhiteSpace(_options.OperatorKey);
    private bool SignedIn => _session.IsActive(HttpContext);

    private bool KeyIsValid(string? supplied) =>
        !string.IsNullOrEmpty(supplied) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(_options.OperatorKey!)));

    // ---------------------------------------------------------------- sign in / out
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (!Enabled) return NotFound();
        var model = new TenantAdminViewModel { LoggedIn = SignedIn };
        return model.LoggedIn ? await ConsoleAsync(model, fresh: false) : View(model);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(TenantAdminViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        if (!KeyIsValid(model.OperatorKey))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await LogAsync(null, "Operator sign-in refused", "wrong key");
            return View("Index", new TenantAdminViewModel { Message = "The operator key is not correct." });
        }
        _session.Start(HttpContext);
        await LogAsync(null, "Operator signed in", null);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        OperatorSession.End(HttpContext);
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- companies
    [HttpPost("create")]
    public async Task<IActionResult> Create(TenantAdminViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));

        // Not tied to the browser: closing the tab must not leave a half-built database behind (it takes a minute or two).
        var result = await _provisioning.ProvisionTenantAsync(model.Code ?? string.Empty, model.Name ?? string.Empty, model.AdminEmail ?? string.Empty,
            model.AdminPassword ?? string.Empty, isDemo: false, includeErrorDetail: true, cancellationToken: CancellationToken.None);
        model.AdminPassword = null;
        model.Succeeded = result.IsSuccess;
        model.Message = result.IsSuccess
            ? $"Company {result.Value.Code} created. Its administrator signs in as \"admin\" (or with the e-mail) at {TenantUrls.For(Request, _options, result.Value.Code, "/Account/Login")}"
            : string.Join(" ", result.Errors.Select(e => e.Message));
        await LogAsync(result.IsSuccess ? result.Value.Code : model.Code, result.IsSuccess ? "Company created" : "Company creation failed", result.IsSuccess ? model.Name : model.Message);
        if (result.IsSuccess) { model.Code = null; model.Name = null; model.AdminEmail = null; }
        return await ConsoleAsync(model);
    }

    /// <summary>Creates the demo company right now (uses Tenancy:DemoAdminPassword) and shows the exact reason when it fails.</summary>
    [HttpPost("create-demo")]
    public async Task<IActionResult> CreateDemo(TenantAdminViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        if (string.IsNullOrWhiteSpace(_options.DemoAdminPassword))
        {
            model.Message = "Set Tenancy:DemoAdminPassword in appsettings.Production.json first (the demo administrator's password; Set-OperatorAccess.ps1 does it), then try again.";
            return await ConsoleAsync(model);
        }
        if (await _catalog!.ExistsAsync(TenancyOptions.DemoCode, CancellationToken.None))
        {
            model.Succeeded = true;
            model.Message = "The demo company already exists.";
            return await ConsoleAsync(model);
        }
        var result = await _provisioning.ProvisionTenantAsync(TenancyOptions.DemoCode, "Demo Company", "demo.admin@example.invalid", _options.DemoAdminPassword,
            isDemo: true, includeErrorDetail: true, cancellationToken: CancellationToken.None);
        await LogAsync(TenancyOptions.DemoCode, result.IsSuccess ? "Demo company created" : "Demo company creation failed", result.IsSuccess ? null : string.Join(" ", result.Errors.Select(e => e.Message)));
        model.Succeeded = result.IsSuccess;
        model.Message = result.IsSuccess ? "The demo company was created. Use Explore Demo Environment on the sign-in page."
                                         : string.Join(" ", result.Errors.Select(e => e.Message));
        return await ConsoleAsync(model);
    }

    /// <summary>Brings every company database up to the schema of the installed version.</summary>
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade(TenantAdminViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var status = await _migrations.UpgradeAllAsync(CancellationToken.None);
        var open = status.Where(s => !s.UpToDate).ToList();
        await LogAsync(null, open.Count == 0 ? "All companies upgraded" : "Upgrade incomplete", open.Count == 0 ? null : string.Join(", ", open.Select(s => s.Code)));
        model.Succeeded = open.Count == 0;
        model.Message = open.Count == 0 ? "Every company is up to date."
            : "Not every company could be upgraded: " + string.Join(" ", open.Select(s => $"{s.Code}: {s.Problem ?? s.Pending + " pending"}.")) + " Details are in the log.";
        return await ConsoleAsync(model);
    }

    /// <summary>Suspends a company: nobody can reach it until it is resumed; nothing is deleted. Signed-in users lose access on their next request.</summary>
    [HttpPost("suspend/{code}")]
    public Task<IActionResult> Suspend(string code) => SetActiveAsync(code, false);

    [HttpPost("resume/{code}")]
    public Task<IActionResult> Resume(string code) => SetActiveAsync(code, true);

    private async Task<IActionResult> SetActiveAsync(string code, bool active)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var store = _services.GetService<ITenantAdminStore>();
        var model = new TenantAdminViewModel();
        if (store is null || !await store.SetActiveAsync(code, active, CancellationToken.None))
            model.Message = "Unknown company.";
        else
        {
            model.Succeeded = true;
            model.Message = active ? $"{code} resumed." : $"{code} suspended: nobody can use it until you resume it. Its data is untouched.";
            await LogAsync(code, active ? "Company resumed" : "Company suspended", null);
        }
        return await ConsoleAsync(model);
    }

    /// <summary>Break-glass: clears the address list, the two-step requirement and single sign-on of a company, and resets the first administrator's two-step verification.</summary>
    [HttpPost("emergency/{code}")]
    public async Task<IActionResult> Emergency(string code)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var model = new TenantAdminViewModel();
        var done = _services.GetService<ITenantEmergencyService>() is { } emergency && await emergency.RestoreAccessAsync(code, CancellationToken.None);
        model.Succeeded = done;
        model.Message = done ? $"{code}: address list, two-step requirement and single sign-on cleared; the administrator \"admin\" is unlocked and has two-step verification reset (password unchanged). Tell them to set these up again."
                             : "Unknown company.";
        if (done) await LogAsync(code, "Emergency access restored", null);
        return await ConsoleAsync(model);
    }

    // ---------------------------------------------------------------- company keys and demo requests
    private string PublicBase => string.IsNullOrWhiteSpace(_options.PublicBaseUrl) ? $"{Request.Scheme}://{Request.Host}" : _options.PublicBaseUrl.TrimEnd('/');

    /// <summary>Makes a company key (optionally for a demo request) and shows it once; it can be e-mailed to the requester straight away.</summary>
    [HttpPost("keys/generate")]
    public async Task<IActionResult> GenerateKey(string? email, string? note, int? requestId, int days, bool sendEmail)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var keys = _services.GetService<ICompanyKeyStore>();
        var model = new TenantAdminViewModel();
        if (keys is null) { model.Message = "Company keys are not available (the catalog needs updating: run Install-SaaS.ps1 -Update)."; return await ConsoleAsync(model); }
        DemoRequestInfo? request = requestId is { } rid ? await keys.GetRequestAsync(rid) : null;
        var to = (email ?? request?.Email)?.Trim();
        if (!string.IsNullOrEmpty(to) && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(to)) { model.Message = "That e-mail address is not valid."; return await ConsoleAsync(model); }
        var (key, info) = await keys.IssueAsync(to, note ?? (request is null ? null : request.CompanyName), request?.Id, days <= 0 ? 14 : days, "operator " + HttpContext.Connection.RemoteIpAddress);
        if (request is not null) await keys.SetRequestStatusAsync(request.Id, DemoRequestStatus.KeySent, info.Id);
        await LogAsync(null, "Company key made", $"…{info.Hint} for {to ?? note ?? "nobody in particular"}, valid until {info.ExpiresAtUtc:yyyy-MM-dd}");
        model.NewKey = key;
        model.NewKeyFor = to;
        model.NewKeyExpires = info.ExpiresAtUtc;
        model.Succeeded = true;
        model.Message = "Company key made. Copy it now: it is shown only once.";
        if (sendEmail && !string.IsNullOrEmpty(to))
        {
            var sent = await SendKeyAsync(key, to, info.ExpiresAtUtc, request?.FullName);
            model.KeyEmailed = sent;
            model.Message += sent ? $" It was e-mailed to {to}." : " It could not be e-mailed (mail is not set up, or the server refused): send it yourself.";
        }
        return await ConsoleAsync(model);
    }

    /// <summary>E-mails an unused key (the operator pressed the button next to the key just made).</summary>
    [HttpPost("keys/send")]
    public async Task<IActionResult> SendKey(string? key, string? email)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var model = new TenantAdminViewModel();
        var keys = _services.GetService<ICompanyKeyStore>();
        if (keys is null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(email) || !await keys.IsValidAsync(key))
            model.Message = "That key cannot be sent (it is not valid any more).";
        else
        {
            var sent = await SendKeyAsync(key, email.Trim(), DateTime.UtcNow.AddDays(14), null);
            model.Succeeded = sent;
            model.Message = sent ? $"Key e-mailed to {email.Trim()}." : "It could not be e-mailed (mail is not set up, or the server refused): send it yourself.";
        }
        return await ConsoleAsync(model);
    }

    private async Task<bool> SendKeyAsync(string key, string to, DateTime expiresUtc, string? name)
    {
        var link = $"{PublicBase}/Register?key={Uri.EscapeDataString(key)}";
        var until = expiresUtc.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var text = $"Hello{(name is null ? "" : " " + name)},\n\nThank you for your interest in {AMGH.ITInventory.Shared.AppInfo.Name}. Here is your company key:\n\n    {key}\n\nOpen {link}\nor go to {PublicBase}/Register and enter the key, choose your company code and name, and set the administrator's e-mail and password.\nThe key can be used once and is valid until {until}.\n";
        var html = $"<p>Hello{(name is null ? "" : " " + PlatformMailer.Html(name))},</p><p>Thank you for your interest in {PlatformMailer.Html(AMGH.ITInventory.Shared.AppInfo.Name)}. Here is your company key:</p>" +
                   $"<p style=\"font-size:20px;font-family:monospace\"><strong>{PlatformMailer.Html(key)}</strong></p><p><a href=\"{PlatformMailer.Html(link)}\">Create your company</a>, or go to {PlatformMailer.Html(PublicBase)}/Register and enter the key, " +
                   $"choose your company code and name, and set the administrator's e-mail and password.</p><p>The key can be used once and is valid until {until}.</p>";
        return await _mailer.TrySendAsync(to, "Your company key for " + AMGH.ITInventory.Shared.AppInfo.Name, html, text, _mailer.OperatorEmail, CancellationToken.None);
    }

    [HttpPost("keys/revoke/{id:int}")]
    public async Task<IActionResult> RevokeKey(int id)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        var done = _services.GetService<ICompanyKeyStore>() is { } keys && await keys.RevokeAsync(id);
        if (done) await LogAsync(null, "Company key withdrawn", "key #" + id);
        return await ConsoleAsync(new TenantAdminViewModel { Succeeded = done, Message = done ? "The key was withdrawn." : "That key is already used or withdrawn." });
    }

    [HttpPost("requests/decline/{id:int}")]
    public async Task<IActionResult> DeclineRequest(int id)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        if (_services.GetService<ICompanyKeyStore>() is { } keys) { await keys.SetRequestStatusAsync(id, DemoRequestStatus.Declined, null); await LogAsync(null, "Demo request declined", "request #" + id); }
        return await ConsoleAsync(new TenantAdminViewModel { Succeeded = true, Message = "The request was marked as declined." });
    }

    // ---------------------------------------------------------------- backups
    [HttpGet("backups/{code}")]
    public async Task<IActionResult> Backups(string code)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        return await BackupsViewAsync(code, null, false);
    }

    [HttpPost("backups/{code}/create")]
    public async Task<IActionResult> BackupNow(string code)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        if (await CompanyAsync(code) is null) return NotFound();
        var result = await _backups.BackupAsync(code, cancellationToken: CancellationToken.None);
        await LogAsync(code, result.IsSuccess ? "Backup made" : "Backup failed", result.IsSuccess ? result.Value.FileName : result.FirstError!.Message);
        return await BackupsViewAsync(code, result.IsSuccess ? $"Backup created: {result.Value.FileName}" : result.FirstError!.Message, result.IsSuccess);
    }

    [HttpPost("backups/{code}/restore")]
    public async Task<IActionResult> Restore(string code, string? file, string? confirm)
    {
        if (!Enabled) return NotFound();
        if (!SignedIn) return RedirectToAction(nameof(Index));
        if (await CompanyAsync(code) is null) return NotFound();
        // replacing a company's data is not undoable by a slip of the mouse: the code of the company must be typed
        if (!string.Equals(confirm?.Trim(), code, StringComparison.OrdinalIgnoreCase))
            return await BackupsViewAsync(code, "Nothing was restored: type the company code to confirm.", false);
        var result = await _backups.RestoreAsync(code, file ?? string.Empty, CancellationToken.None);
        await LogAsync(code, result.IsSuccess ? "Company restored" : "Restore failed", file);
        return await BackupsViewAsync(code, result.IsSuccess
            ? $"Restored from {file}. The data of the company is now as it was in that backup (a backup of the state before the restore was taken first)."
            : result.FirstError!.Message, result.IsSuccess);
    }

    private async Task<TenantRow?> CompanyAsync(string code) =>
        (await TenantsAsync(HttpContext.RequestAborted)).FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    private async Task<IActionResult> BackupsViewAsync(string code, string? message, bool succeeded)
    {
        var company = await CompanyAsync(code);
        if (company is null) return NotFound();
        var model = new TenantBackupsViewModel { Code = company.Code, Name = company.Name, Message = message, Succeeded = succeeded };
        try { model.Files = (await _backups.ListAsync(company.Code, HttpContext.RequestAborted)).Select(f => new BackupRow(f.FileName, f.SizeBytes, f.CreatedUtc, f.VerifiedUtc, f.VerifyOk, f.VerifyNote, f.OffsiteUtc, f.OffsiteOk, f.OffsiteNote)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            model.CanList = false;
            model.Message ??= "The backup folder cannot be listed by the application (" + ex.Message.Split('\n')[0] + "). Backups can still be made; restoring needs the file list.";
        }
        return View("Backups", model);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<IActionResult> ConsoleAsync(TenantAdminViewModel model, bool fresh = true)
    {
        model.LoggedIn = true;
        model.OperatorKey = null;
        var overview = _services.GetRequiredService<PlatformOverview>();
        var snapshot = await overview.GetAsync(fresh, CancellationToken.None);
        model.Tenants = snapshot.Companies.Select(c => new TenantRow(c.Tenant.Code, c.Tenant.Name, c.Tenant.IsDemo, c.Tenant.IsActive, c.Tenant.CreatedAtUtc,
            TenantUrls.For(Request, _options, c.Tenant.Code, "/Account/Login"))).ToList();
        model.Snapshot = snapshot;
        if (_services.GetService<ICompanyKeyStore>() is { } keyStore)
        {
            try { model.Keys = await keyStore.ListAsync(30, CancellationToken.None); model.Requests = await keyStore.ListRequestsAsync(30, CancellationToken.None); }
            catch (Microsoft.Data.SqlClient.SqlException) { model.KeysUnavailable = true; }
        }
        model.MailReady = _mailer.IsConfigured;
        if (_services.GetService<ITenantAdminStore>() is { } store)
        {
            try { model.Events = await store.RecentEventsAsync(15, CancellationToken.None); }
            catch (Microsoft.Data.SqlClient.SqlException) { model.EventsUnavailable = true; } // the catalog has not been updated yet (run Install-SaaS.ps1 -Update)
        }
        return View("Console", model);
    }

    /// <summary>Writes one line to the operator log; a problem with the log itself never blocks the action.</summary>
    private async Task LogAsync(string? code, string action, string? detail)
    {
        try
        {
            if (_services.GetService<ITenantAdminStore>() is { } store)
                await store.RecordEventAsync(code, action, detail, "operator " + HttpContext.Connection.RemoteIpAddress, CancellationToken.None);
            if (_services.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>() is { } cache) PlatformOverview.Invalidate(cache);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException) { /* log table missing: see ConsoleAsync */ }
    }

    private async Task<IReadOnlyList<TenantRow>> TenantsAsync(CancellationToken ct) =>
        (await _catalog!.ListAsync(ct)).Select(t => new TenantRow(t.Code, t.Name, t.IsDemo, t.IsActive, t.CreatedAtUtc,
            TenantUrls.For(Request, _options, t.Code, "/Account/Login"))).ToList();
}
