using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Notifications;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>The signed-in user's notifications and e-mail choices.</summary>
[Route("notifications")]
public sealed class NotificationsController : Controller
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    private int Me => User.GetUserId() ?? 0;

    [HttpGet("")]
    public async Task<IActionResult> Index(bool unread, CancellationToken ct)
    {
        ViewData["Unread"] = unread;
        return View(new NotificationCentreViewModel
        {
            Items = await _notifications.InboxAsync(Me, unread, 200, ct),
            Preferences = await _notifications.EmailPreferencesAsync(Me, ct)
        });
    }

    /// <summary>Marks the notification read and opens its page (only paths inside this site).</summary>
    [HttpGet("{id:long}/open")]
    public async Task<IActionResult> Open(long id, CancellationToken ct)
    {
        var item = (await _notifications.InboxAsync(Me, false, 500, ct)).FirstOrDefault(n => n.Id == id);
        if (item is null) return NotFound();
        await _notifications.MarkReadAsync(Me, id, ct);
        return item.ActionUrl is { } url && Url.IsLocalUrl(url) ? Redirect(url) : RedirectToAction(nameof(Index));
    }

    [HttpPost("read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkReadAsync(Me, null, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("preferences")]
    public async Task<IActionResult> Preferences(string[] email, CancellationToken ct)
    {
        var on = (email ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        await _notifications.SaveEmailPreferencesAsync(Me, NotificationCatalog.Rules.ToDictionary(r => r.Key, r => on.Contains(r.Key)), ct);
        TempData["StatusMessage"] = "Your e-mail choices are saved. In-app notifications are always shown.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Company e-mail and notification settings (Notifications.Manage).</summary>
[HasPermission(Permissions.Administration.NotificationsManage)]
[Route("settings/notifications")]
public sealed class NotificationSettingsController : Controller
{
    private readonly INotificationSettingsService _settings;
    private readonly IMailQueue _queue;
    private readonly INotificationScheduler _scheduler;
    private readonly IUserAdministrationService _users;

    public NotificationSettingsController(INotificationSettingsService settings, IMailQueue queue, INotificationScheduler scheduler, IUserAdministrationService users)
    {
        _settings = settings;
        _queue = queue;
        _scheduler = scheduler;
        _users = users;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tab, QueueStatus? status, CancellationToken ct)
    {
        var templates = new List<(RuleDefinition Rule, bool Custom)>();
        foreach (var r in NotificationCatalog.Rules) templates.Add((r, await _settings.IsCustomTemplateAsync(r.Key, ct)));
        return View(new NotificationSettingsViewModel
        {
            Tab = tab ?? "email", Mail = await _settings.MailSettingsAsync(ct), Rules = await _settings.RulesAsync(ct),
            Roles = (await _users.ListRolesAsync(ct)).Select(r => r.RoleName).ToList(), Templates = templates,
            Queue = await _queue.ListAsync(status, 200, ct), QueueCounts = await _queue.CountsAsync(ct), QueueFilter = status
        });
    }

    [BlockInDemo]
    [HttpPost("mail")]
    public async Task<IActionResult> SaveMail([Bind(Prefix = "Mail")] MailSettingsForm form, CancellationToken ct)
    {
        var r = await _settings.SaveMailSettingsAsync(form, ct);
        TempData["StatusMessage"] = r.IsSuccess ? "E-mail settings saved." : string.Join(" ", r.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Index), new { tab = "email" });
    }

    [HttpPost("mail/test")]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        var r = await _settings.TestConnectionAsync(ct);
        TempData["StatusMessage"] = r.IsSuccess ? r.Value : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { tab = "email" });
    }

    [HttpPost("mail/send-test")]
    public async Task<IActionResult> SendTest(string to, string? template, CancellationToken ct)
    {
        var r = await _settings.SendTestAsync(to, template ?? NotificationCatalog.TestTemplate, ct);
        TempData["StatusMessage"] = r.IsSuccess ? $"Test message sent to {to}." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { tab = template is null ? "email" : "templates" });
    }

    [HttpPost("rules/{key}")]
    public async Task<IActionResult> SaveRule(string key, bool isEnabled, bool emailEnabled, bool inAppEnabled, string? days, string[]? roles, CancellationToken ct)
    {
        var r = await _settings.SaveRuleAsync(new RuleSettings(key, isEnabled, emailEnabled, inAppEnabled,
            RuleDays.Parse(days), (roles ?? Array.Empty<string>()).ToList(), null), ct);
        TempData["StatusMessage"] = r.IsSuccess ? "Notification settings saved." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { tab = "rules" });
    }

    [HttpPost("run-reminders")]
    public async Task<IActionResult> RunReminders(CancellationToken ct)
    {
        var n = await _scheduler.RunDailyAsync(force: true, ct);
        var (sent, failed, retrying) = await _queue.ProcessAsync(100, ct);
        TempData["StatusMessage"] = $"Reminders checked: {n} notification(s) created; e-mail sent {sent}, failed {failed}, retrying {retrying}. Reminders already sent before are not repeated.";
        return RedirectToAction(nameof(Index), new { tab = "rules" });
    }

    [HttpGet("templates/{key}")]
    public async Task<IActionResult> Template(string key, CancellationToken ct)
    {
        var def = NotificationCatalog.Find(key);
        if (def is null) return NotFound();
        var t = await _settings.TemplateAsync(key, ct);
        return View("TemplateEdit", new TemplateEditViewModel { Rule = def, Subject = t.Subject, BodyHtml = t.BodyHtml, IsCustom = await _settings.IsCustomTemplateAsync(key, ct) });
    }

    [HttpPost("templates/{key}")]
    public async Task<IActionResult> SaveTemplate(string key, string subject, string bodyHtml, CancellationToken ct)
    {
        var r = await _settings.SaveTemplateAsync(key, subject, bodyHtml, ct);
        if (r.IsFailure)
        {
            ModelState.AddModelError(string.Empty, r.FirstError!.Message);
            var def = NotificationCatalog.Find(key);
            if (def is null) return NotFound();
            return View("TemplateEdit", new TemplateEditViewModel { Rule = def, Subject = subject, BodyHtml = bodyHtml, IsCustom = true });
        }
        TempData["StatusMessage"] = "Template saved.";
        return RedirectToAction(nameof(Template), new { key });
    }

    [HttpPost("templates/{key}/reset")]
    public async Task<IActionResult> ResetTemplate(string key, CancellationToken ct)
    {
        await _settings.ResetTemplateAsync(key, ct);
        TempData["StatusMessage"] = "Template reset to the built-in text.";
        return RedirectToAction(nameof(Template), new { key });
    }

    /// <summary>
    /// The e-mail as recipients will see it, with sample values. Shown in a sandboxed frame with its own strict policy:
    /// no scripts, no outside content; the logo is inlined as data.
    /// </summary>
    [HttpGet("templates/{key}/preview")]
    public async Task<IActionResult> Preview(string key, CancellationToken ct)
    {
        var (subject, html) = await _settings.PreviewAsync(key, ct);
        var look = await _settings.CompanyLookAsync(ct);
        if (look.Logo is not null) html = html.Replace("cid:logo", $"data:{look.LogoType ?? "image/png"};base64,{Convert.ToBase64String(look.Logo)}", StringComparison.Ordinal);
        html = html.Replace("<body", $"<body data-subject=\"{System.Net.WebUtility.HtmlEncode(subject)}\"", StringComparison.Ordinal);
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; frame-ancestors 'self'; sandbox";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        Response.Headers.CacheControl = "no-store";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("queue/{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken ct)
    {
        var r = await _queue.RetryAsync(id, ct);
        TempData["StatusMessage"] = r.IsSuccess ? "The message will be sent again within a minute." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { tab = "queue" });
    }

    [HttpPost("queue/{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var r = await _queue.CancelAsync(id, ct);
        TempData["StatusMessage"] = r.IsSuccess ? "Message cancelled." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { tab = "queue" });
    }
}
