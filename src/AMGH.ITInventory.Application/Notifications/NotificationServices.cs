using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.Notifications;

// ============================================================================ models

/// <summary>Who receives a notification: a user (in-app + e-mail) or an e-mail address only (e.g. an employee without an account).</summary>
public sealed record Recipient(int? UserId, string? Email, string? Name);

public sealed record NotificationRequest(string RuleKey, IReadOnlyDictionary<string, string> Values, IReadOnlyList<Recipient> Recipients,
    NotificationSeverity Severity = NotificationSeverity.Info, string? RelatedEntity = null, string? RelatedId = null, string? ActionPath = null,
    string? DeduplicationKey = null);

public sealed record InboxItem(long Id, string Title, string Message, string Severity, string? ActionUrl, bool IsRead, DateTime CreatedUtc);

public sealed record QueueItemView(long Id, string Recipient, string Subject, string Status, int Attempts, DateTime NextAttemptUtc,
    DateTime? SentUtc, string? LastError, DateTime CreatedUtc);

public sealed class MailSettingsForm
{
    public bool IsEnabled { get; set; }
    public MailMode Mode { get; set; } = MailMode.ConnectorRelay;
    public string? Host { get; set; }
    public int Port { get; set; } = 25;
    public MailSecurity Security { get; set; } = MailSecurity.StartTls;
    public string? CertificateThumbprint { get; set; }
    public string? Username { get; set; }
    /// <summary>Only when entering or replacing it; never sent back to the page.</summary>
    public string? Secret { get; set; }
    public bool HasSecret { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }
    public string? AppBaseUrl { get; set; }
    public DateTime? LastTestUtc { get; set; }
    public string? LastTestResult { get; set; }
}

// ============================================================================ rendering

/// <summary>{{Name}} placeholders; values are HTML-encoded in bodies and stripped of line breaks in subjects.</summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\s*\}\}")] private static partial Regex Placeholder();

    public static string Subject(string template, IReadOnlyDictionary<string, string> values)
    {
        var s = Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : string.Empty);
        s = new string(s.Where(c => c is not ('\r' or '\n')).ToArray()).Trim();
        return s.Length > 200 ? s[..200] : s;
    }

    public static string Body(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? WebUtility.HtmlEncode(v) : string.Empty);

    /// <summary>Plain-text version for the text/plain part and in-app messages.</summary>
    public static string ToText(string html)
    {
        var t = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/tr|/h[1-6])\s*/?>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", string.Empty);
        t = WebUtility.HtmlDecode(t);
        return Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>Branded e-mail layout (table-based, inline styles: what e-mail clients support). The logo is cid:logo.</summary>
    public static string Layout(string bodyHtml, string companyName, string headerColor, bool hasLogo, string? link, string? linkText)
    {
        var color = Regex.IsMatch(headerColor ?? string.Empty, "^#[0-9A-Fa-f]{6}$") ? headerColor : "#0E3B43";
        var name = WebUtility.HtmlEncode(companyName);
        var button = string.IsNullOrEmpty(link) ? string.Empty
            : $"<p style=\"margin:24px 0 8px\"><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"background:{color};color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:4px;display:inline-block;font-weight:600\">{WebUtility.HtmlEncode(linkText ?? "Open in " + Shared.AppInfo.ShortName)}</a></p>";
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"></head>" +
               "<body style=\"margin:0;padding:0;background:#f3f6f6;font-family:Segoe UI,Arial,sans-serif;color:#1d2b2e\">" +
               "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f6f6;padding:24px 0\"><tr><td align=\"center\">" +
               "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;width:100%;background:#ffffff;border-radius:8px;overflow:hidden\">" +
               $"<tr><td style=\"background:{color};padding:16px 24px;color:#ffffff\">" +
               (hasLogo ? "<img src=\"cid:logo\" alt=\"\" height=\"36\" style=\"height:36px;vertical-align:middle;margin-right:12px;background:#ffffff;border-radius:4px\">" : string.Empty) +
               $"<span style=\"font-size:16px;font-weight:600;vertical-align:middle\">{name} · {Shared.AppInfo.ShortName}</span></td></tr>" +
               $"<tr><td style=\"padding:24px;font-size:14px;line-height:1.55\">{bodyHtml}{button}</td></tr>" +
               $"<tr><td style=\"padding:12px 24px;background:#f8fafa;color:#6b7b7e;font-size:12px\">Automatic message from the {name} {Shared.AppInfo.Name}. " +
               "You receive it because of your role or an asset assigned to you. Change e-mail choices under Notifications in the application.</td></tr>" +
               "</table></td></tr></table></body></html>";
    }
}

// ============================================================================ notify + inbox

public interface INotificationService
{
    /// <summary>Creates in-app notifications and queues e-mails per the company's rule, the template and each user's choices.</summary>
    Task<int> NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="NotifyAsync"/> but never throws: notifications must not break the business action that triggered them.</summary>
    Task TryNotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Recipient>> UsersInRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipient>> UsersWithPermissionAsync(string permission, CancellationToken cancellationToken = default);
    Task<Recipient?> UserByNameAsync(string username, CancellationToken cancellationToken = default);
    Task<Recipient?> EmployeeAsync(int? employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxItem>> InboxAsync(int userId, bool unreadOnly, int max = 50, CancellationToken cancellationToken = default);
    Task<int> UnreadCountAsync(int userId, CancellationToken cancellationToken = default);
    Task MarkReadAsync(int userId, long? notificationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, bool>> EmailPreferencesAsync(int userId, CancellationToken cancellationToken = default);
    Task SaveEmailPreferencesAsync(int userId, IReadOnlyDictionary<string, bool> emailByRule, CancellationToken cancellationToken = default);
}

internal sealed class NotificationService : INotificationService
{
    public const int MaxAttempts = 6;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationSettingsService _settings;

    public NotificationService(IUnitOfWork uow, IDateTimeProvider clock, INotificationSettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<int> NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ct = cancellationToken;
        var def = NotificationCatalog.Find(request.RuleKey) ?? throw new ArgumentException("Unknown notification kind.", nameof(request));
        var rule = await _settings.RuleAsync(request.RuleKey, ct);
        if (!rule.IsEnabled || request.Recipients.Count == 0) return 0;
        if (!(await _settings.CompanyLookAsync(ct)).NotificationsEnabled) return 0;   // company switch (Company settings)

        var company = await _settings.CompanyLookAsync(ct);
        var values = new Dictionary<string, string>(request.Values, StringComparer.Ordinal) { ["CompanyName"] = company.Name };
        var mail = await _uow.Repository<CompanyMailSettings>().QueryAsync(q => q.Select(m => m.AppBaseUrl), ct);
        var baseUrl = mail.FirstOrDefault()?.TrimEnd('/');
        var link = request.ActionPath is null || string.IsNullOrEmpty(baseUrl) ? null : baseUrl + request.ActionPath;
        values["Link"] = link ?? string.Empty;

        var template = await _settings.TemplateAsync(request.RuleKey, ct);
        var subject = TemplateRenderer.Subject(template.Subject, values);
        var body = TemplateRenderer.Body(template.BodyHtml, values);
        var text = TemplateRenderer.ToText(body);
        var html = TemplateRenderer.Layout(body, company.Name, company.HeaderColor, company.HasLogo, link, "Open in " + Shared.AppInfo.ShortName);

        // e-mail opt-outs of the user recipients
        var userIds = request.Recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var optOut = (await _uow.Repository<NotificationPreference>().QueryAsync(q => q
            .Where(p => p.RuleKey == request.RuleKey && !p.EmailEnabled && userIds.Contains(p.UserId)).Select(p => p.UserId), ct)).ToHashSet();

        var created = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in request.Recipients)
        {
            var identity = r.UserId?.ToString(CultureInfo.InvariantCulture) ?? r.Email ?? string.Empty;
            if (identity.Length == 0 || !seen.Add(identity)) continue;
            // a repeated reminder (same key) is not shown twice in the app either
            var inAppDuplicate = request.DeduplicationKey is not null && r.UserId is { } du && await _uow.Repository<Notification>().AnyAsync(x => x.UserId == du
                && x.Title == subject && x.CreatedDate >= _clock.UtcNow.AddHours(-20), ct);
            if (rule.InAppEnabled && r.UserId is { } uid && !inAppDuplicate)
            {
                await _uow.Repository<Notification>().AddAsync(new Notification
                {
                    UserId = uid, Title = subject, Message = text.Length > 1000 ? text[..1000] : text, NotificationType = def.Type,
                    Severity = request.Severity, RelatedEntityName = request.RelatedEntity, RelatedEntityId = request.RelatedId,
                    ActionUrl = request.ActionPath, IsRead = false
                }, ct);
                created++;
            }
            if (rule.EmailEnabled && !string.IsNullOrWhiteSpace(r.Email) && !(r.UserId is { } u && optOut.Contains(u)))
            {
                var email = r.Email.Trim();
                var key = request.DeduplicationKey is null ? null : $"{request.DeduplicationKey}:{email}".ToLowerInvariant();
                if (key is { Length: > 200 }) key = key[..200];
                if (key is not null && await _uow.Repository<NotificationQueueItem>().AnyAsync(x => x.DeduplicationKey == key, ct)) continue;
                await _uow.Repository<NotificationQueueItem>().AddAsync(new NotificationQueueItem
                {
                    Channel = NotificationChannel.Email, RecipientAddress = email, RecipientUserId = r.UserId, Subject = subject, Body = html, IsHtml = true,
                    QueueStatus = QueueStatus.Pending, AttemptCount = 0, MaxAttempts = MaxAttempts, NextAttemptDate = _clock.UtcNow, DeduplicationKey = key
                }, ct);
                created++;
            }
        }
        if (created > 0) await _uow.SaveChangesAsync(ct);
        return created;
    }

    public async Task TryNotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        try { await NotifyAsync(request, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("Notification {0} could not be created: {1}", request.RuleKey, ex.Message);
        }
    }

    // ---------------------------------------------------------------- recipients (company members only)

    public async Task<IReadOnlyList<Recipient>> UsersInRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var names = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        if (names.Count == 0) return Array.Empty<Recipient>();
        var ids = await _uow.Repository<UserRole>().QueryAsync(q => q.Where(ur => names.Contains(ur.Role.RoleName) && ur.Role.IsActive)
            .Select(ur => ur.UserId).Distinct(), cancellationToken);
        return await ActiveMembersAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<Recipient>> UsersWithPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var ids = await _uow.Repository<RolePermission>().QueryAsync(q => q.Where(rp => rp.Permission.PermissionCode == permission && rp.Role.IsActive)
            .SelectMany(rp => rp.Role.UserRoles.Select(ur => ur.UserId)).Distinct(), cancellationToken);
        return await ActiveMembersAsync(ids, cancellationToken);
    }

    /// <summary>Active users with an active membership in this company (memberships are not company-filtered, so filter explicitly).</summary>
    private async Task<IReadOnlyList<Recipient>> ActiveMembersAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return Array.Empty<Recipient>();
        var company = await _settings.CompanyLookAsync(ct);
        return await _uow.Repository<CompanyUser>().QueryAsync(q => q
            .Where(m => m.CompanyId == company.Id && m.IsActive && m.User.IsActive && userIds.Contains(m.UserId))
            .Select(m => new Recipient(m.UserId, m.User.Email, m.User.DisplayName)), ct);
    }

    public async Task<Recipient?> UserByNameAsync(string username, CancellationToken cancellationToken = default)
    {
        var company = await _settings.CompanyLookAsync(cancellationToken);
        return (await _uow.Repository<CompanyUser>().QueryAsync(q => q
            .Where(m => m.CompanyId == company.Id && m.IsActive && m.User.IsActive && m.User.Username == username)
            .Select(m => new Recipient(m.UserId, m.User.Email, m.User.DisplayName)), cancellationToken)).FirstOrDefault();
    }

    public async Task<Recipient?> EmployeeAsync(int? employeeId, CancellationToken cancellationToken = default)
    {
        if (employeeId is null) return null;
        var rows = await _uow.Repository<Employee>().QueryAsync(q => q.Where(e => e.EmployeeId == employeeId)
            .Select(e => new { e.Email, Name = e.FirstName + " " + e.LastName }), cancellationToken);
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(rows[0].Email)) return null;
        // the employee's own account (if any) also gets the in-app notification
        var company = await _settings.CompanyLookAsync(cancellationToken);
        var user = (await _uow.Repository<CompanyUser>().QueryAsync(q => q.Where(m => m.CompanyId == company.Id && m.EmployeeId == employeeId && m.IsActive)
            .Select(m => (int?)m.UserId), cancellationToken)).FirstOrDefault();
        return new Recipient(user, rows[0].Email, rows[0].Name);
    }

    // ---------------------------------------------------------------- inbox

    public async Task<IReadOnlyList<InboxItem>> InboxAsync(int userId, bool unreadOnly, int max = 50, CancellationToken cancellationToken = default) =>
        await _uow.Repository<Notification>().QueryAsync(q =>
        {
            q = q.Where(n => n.UserId == userId);
            if (unreadOnly) q = q.Where(n => !n.IsRead);
            return q.OrderByDescending(n => n.NotificationId).Take(Math.Clamp(max, 1, 500))
                .Select(n => new InboxItem(n.NotificationId, n.Title, n.Message, n.Severity.ToString(), n.ActionUrl, n.IsRead, n.CreatedDate));
        }, cancellationToken);

    public Task<int> UnreadCountAsync(int userId, CancellationToken cancellationToken = default) =>
        _uow.Repository<Notification>().CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

    public async Task MarkReadAsync(int userId, long? notificationId, CancellationToken cancellationToken = default)
    {
        var items = await _uow.Repository<Notification>().ListAsync(n => n.UserId == userId && !n.IsRead
            && (notificationId == null || n.NotificationId == notificationId), cancellationToken);
        foreach (var n in items) { n.IsRead = true; n.ReadDate = _clock.UtcNow; }
        if (items.Count > 0) await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, bool>> EmailPreferencesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var stored = (await _uow.Repository<NotificationPreference>().QueryAsync(q => q.Where(p => p.UserId == userId), cancellationToken))
            .ToDictionary(p => p.RuleKey, p => p.EmailEnabled);
        return NotificationCatalog.Rules.ToDictionary(r => r.Key, r => !stored.TryGetValue(r.Key, out var on) || on);
    }

    public async Task SaveEmailPreferencesAsync(int userId, IReadOnlyDictionary<string, bool> emailByRule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emailByRule);
        var repo = _uow.Repository<NotificationPreference>();
        var stored = (await repo.ListAsync(p => p.UserId == userId, cancellationToken)).ToDictionary(p => p.RuleKey);
        foreach (var r in NotificationCatalog.Rules)
        {
            var on = !emailByRule.TryGetValue(r.Key, out var v) || v;
            if (stored.TryGetValue(r.Key, out var p)) { p.EmailEnabled = on; p.ModifiedDate = _clock.UtcNow; }
            else if (!on) await repo.AddAsync(new NotificationPreference { UserId = userId, RuleKey = r.Key, EmailEnabled = false, ModifiedDate = _clock.UtcNow }, cancellationToken);
        }
        await _uow.SaveChangesAsync(cancellationToken);
    }
}

// ============================================================================ company settings: rules, templates, mail

public sealed record CompanyLook(int Id, string Name, string HeaderColor, bool HasLogo, byte[]? Logo, string? LogoType, bool NotificationsEnabled = true, bool AssetAuditEnabled = true);

public interface INotificationSettingsService
{
    Task<CompanyLook> CompanyLookAsync(CancellationToken cancellationToken = default);
    Task<RuleSettings> RuleAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleSettings>> RulesAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveRuleAsync(RuleSettings settings, CancellationToken cancellationToken = default);
    Task<TemplateText> TemplateAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> IsCustomTemplateAsync(string key, CancellationToken cancellationToken = default);
    Task<Result> SaveTemplateAsync(string key, string subject, string bodyHtml, CancellationToken cancellationToken = default);
    Task ResetTemplateAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>The complete e-mail for a template with sample values (preview and test messages).</summary>
    Task<(string Subject, string Html)> PreviewAsync(string key, CancellationToken cancellationToken = default);
    Task<MailSettingsForm> MailSettingsAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveMailSettingsAsync(MailSettingsForm form, CancellationToken cancellationToken = default);
    Task<Result<string>> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<Result> SendTestAsync(string to, string templateKey, CancellationToken cancellationToken = default);
    /// <summary>The decrypted connection settings (null when e-mail is not configured or disabled).</summary>
    Task<(MailServer Server, CompanyMailSettings Settings)?> ServerAsync(bool requireEnabled, CancellationToken cancellationToken = default);
}

internal sealed partial class NotificationSettingsService : INotificationSettingsService
{
    [GeneratedRegex(@"^[^@\s<>]+@[^@\s<>]+\.[^@\s<>]+$")] private static partial Regex EmailPattern();
    [GeneratedRegex("^[A-Za-z0-9.-]{1,255}$")] private static partial Regex HostPattern();

    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    private readonly ISecretProtector _secrets;
    private readonly IMailSender _mail;
    private CompanyLook? _look;

    public NotificationSettingsService(IUnitOfWork uow, ICompanyContext company, ICurrentUserService user, IDateTimeProvider clock,
        ISecretProtector secrets, IMailSender mail)
    {
        _uow = uow;
        _company = company;
        _user = user;
        _clock = clock;
        _secrets = secrets;
        _mail = mail;
    }

    public async Task<CompanyLook> CompanyLookAsync(CancellationToken cancellationToken = default)
    {
        var id = _company.CompanyId ?? throw new InvalidOperationException("No company is active.");
        if (_look?.Id == id) return _look;
        var rows = await _uow.Repository<Company>().QueryAsync(q => q.Where(c => c.CompanyId == id).Select(c => new
        {
            c.DisplayName, c.NotificationsEnabled, c.AssetAuditEnabled, Header = c.Branding == null ? null : c.Branding.HeaderColor, Logo = c.Branding == null ? null : c.Branding.LogoImage,
            Type = c.Branding == null ? null : c.Branding.LogoContentType
        }), cancellationToken);
        var r = rows.Single();
        return _look = new CompanyLook(id, r.DisplayName, r.Header ?? "#0E3B43", r.Logo is { Length: > 0 }, r.Logo, r.Type, r.NotificationsEnabled, r.AssetAuditEnabled);
    }

    // ---------------------------------------------------------------- rules

    public async Task<RuleSettings> RuleAsync(string key, CancellationToken cancellationToken = default)
    {
        var def = NotificationCatalog.Find(key) ?? throw new ArgumentException("Unknown notification kind.", nameof(key));
        var row = await _uow.Repository<NotificationRule>().FirstOrDefaultAsync(r => r.RuleKey == key, cancellationToken);
        return row is null
            ? new RuleSettings(key, true, true, true, ParseDays(def.DefaultDays), SplitRoles(def.DefaultRoles), null)
            : new RuleSettings(key, row.IsEnabled, row.EmailEnabled, row.InAppEnabled, ParseDays(row.DaysBefore ?? def.DefaultDays),
                SplitRoles(row.RecipientRoles ?? def.DefaultRoles), row.LastRunDate);
    }

    public async Task<IReadOnlyList<RuleSettings>> RulesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<RuleSettings>();
        foreach (var r in NotificationCatalog.Rules) list.Add(await RuleAsync(r.Key, cancellationToken));
        return list;
    }

    public async Task<Result> SaveRuleAsync(RuleSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var def = NotificationCatalog.Find(settings.Key);
        if (def is null) return Result.Failure(Error.Validation("Unknown notification kind."));
        if (settings.DaysBefore.Any(d => d is < 0 or > 365) || settings.DaysBefore.Count > 10)
            return Result.Failure(Error.Validation("Days must be between 0 and 365 (at most 10 values).", "DaysBefore"));
        var known = (await _uow.Repository<Role>().QueryAsync(q => q.Where(r => r.IsActive).Select(r => r.RoleName), cancellationToken)).ToHashSet();
        var unknown = settings.RecipientRoles.FirstOrDefault(r => !known.Contains(r));
        if (unknown is not null) return Result.Failure(Error.Validation($"Unknown role '{unknown}'.", "RecipientRoles"));
        var row = await _uow.Repository<NotificationRule>().FirstOrDefaultAsync(r => r.RuleKey == settings.Key, cancellationToken);
        if (row is null)
        {
            row = new NotificationRule { RuleKey = settings.Key };
            await _uow.Repository<NotificationRule>().AddAsync(row, cancellationToken);
        }
        row.IsEnabled = settings.IsEnabled;
        row.EmailEnabled = settings.EmailEnabled;
        row.InAppEnabled = settings.InAppEnabled;
        row.DaysBefore = def.Scheduled ? string.Join(",", settings.DaysBefore.Distinct().OrderByDescending(d => d)) : null;
        row.RecipientRoles = def.DefaultRoles is null && settings.RecipientRoles.Count == 0 ? null : string.Join(",", settings.RecipientRoles);
        row.ModifiedBy = _user.UserName;
        row.ModifiedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public static IReadOnlyList<int> ParseDays(string? csv) => RuleDays.Parse(csv);
}

/// <summary>"90, 60, 30" to [90, 60, 30]: distinct whole days 0-365, largest first; anything else is ignored.</summary>
public static class RuleDays
{
    public static IReadOnlyList<int> Parse(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : -1).Where(d => d >= 0).Distinct()
            .OrderByDescending(d => d).ToList();
}

internal sealed partial class NotificationSettingsService
{
    private static List<string> SplitRoles(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------------------------------------------------------------- templates

    public async Task<TemplateText> TemplateAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _uow.Repository<NotificationTemplate>().FirstOrDefaultAsync(t => t.TemplateKey == key, cancellationToken);
        return row is null ? NotificationCatalog.DefaultTemplate(key) : new TemplateText(row.Subject, row.BodyHtml);
    }

    public Task<bool> IsCustomTemplateAsync(string key, CancellationToken cancellationToken = default) =>
        _uow.Repository<NotificationTemplate>().AnyAsync(t => t.TemplateKey == key, cancellationToken);

    public async Task<Result> SaveTemplateAsync(string key, string subject, string bodyHtml, CancellationToken cancellationToken = default)
    {
        if (NotificationCatalog.Find(key) is null && key != NotificationCatalog.TestTemplate) return Result.Failure(Error.Validation("Unknown template."));
        subject = (subject ?? string.Empty).Trim();
        bodyHtml = (bodyHtml ?? string.Empty).Trim();
        if (subject.Length is < 3 or > 200) return Result.Failure(Error.Validation("Subject: 3-200 characters.", "Subject"));
        if (bodyHtml.Length is < 3 or > 20000) return Result.Failure(Error.Validation("Message: 3-20,000 characters.", "BodyHtml"));
        // e-mail clients do not run scripts, but a template is administrator content shown in a preview: refuse active content
        if (Regex.IsMatch(bodyHtml, @"<\s*(script|iframe|object|embed|form)\b|\son[a-z]+\s*=|javascript:", RegexOptions.IgnoreCase))
            return Result.Failure(Error.Validation("Scripts, frames, forms and event handlers are not allowed in templates.", "BodyHtml"));
        var row = await _uow.Repository<NotificationTemplate>().FirstOrDefaultAsync(t => t.TemplateKey == key, cancellationToken);
        if (row is null)
        {
            row = new NotificationTemplate { TemplateKey = key };
            await _uow.Repository<NotificationTemplate>().AddAsync(row, cancellationToken);
        }
        row.Subject = subject;
        row.BodyHtml = bodyHtml;
        row.ModifiedBy = _user.UserName;
        row.ModifiedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task ResetTemplateAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _uow.Repository<NotificationTemplate>().FirstOrDefaultAsync(t => t.TemplateKey == key, cancellationToken);
        if (row is null) return;
        _uow.Repository<NotificationTemplate>().Remove(row);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<(string Subject, string Html)> PreviewAsync(string key, CancellationToken cancellationToken = default)
    {
        var look = await CompanyLookAsync(cancellationToken);
        var t = await TemplateAsync(key, cancellationToken);
        var values = NotificationCatalog.SampleValues(look.Name);
        return (TemplateRenderer.Subject(t.Subject, values),
            TemplateRenderer.Layout(TemplateRenderer.Body(t.BodyHtml, values), look.Name, look.HeaderColor, look.HasLogo, "#", "Open in " + Shared.AppInfo.ShortName));
    }

    // ---------------------------------------------------------------- mail settings

    public async Task<MailSettingsForm> MailSettingsAsync(CancellationToken cancellationToken = default)
    {
        var s = (await _uow.Repository<CompanyMailSettings>().QueryAsync(q => q, cancellationToken)).FirstOrDefault();
        if (s is null)
        {
            var d = MailModeDefaults.For(MailMode.ConnectorRelay);
            return new MailSettingsForm { Mode = MailMode.ConnectorRelay, Host = d.Host, Port = d.Port, Security = d.Security };
        }
        return new MailSettingsForm
        {
            IsEnabled = s.IsEnabled, Mode = Enum.Parse<MailMode>(s.Mode), Host = s.Host, Port = s.Port, Security = Enum.Parse<MailSecurity>(s.Security),
            CertificateThumbprint = s.CertificateThumbprint, Username = s.Username, HasSecret = s.SecretProtected is not null, TenantId = s.TenantId,
            ClientId = s.ClientId, FromAddress = s.FromAddress, FromName = s.FromName, ReplyTo = s.ReplyTo, AppBaseUrl = s.AppBaseUrl,
            LastTestUtc = s.LastTestDate, LastTestResult = s.LastTestResult
        };
    }

    public async Task<Result> SaveMailSettingsAsync(MailSettingsForm f, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(f);
        var errors = new List<Error>();
        var needs = MailModeDefaults.For(f.Mode);
        var host = f.Host?.Trim() ?? string.Empty;
        if (!HostPattern().IsMatch(host)) errors.Add(Error.Validation("Enter the mail server name.", nameof(f.Host)));
        if (f.Port is < 1 or > 65535) errors.Add(Error.Validation("Port: 1-65535.", nameof(f.Port)));
        if (string.IsNullOrWhiteSpace(f.FromAddress) || !EmailPattern().IsMatch(f.FromAddress.Trim())) errors.Add(Error.Validation("Enter the sender address.", nameof(f.FromAddress)));
        if (!string.IsNullOrWhiteSpace(f.ReplyTo) && !EmailPattern().IsMatch(f.ReplyTo.Trim())) errors.Add(Error.Validation("Reply-to must be an e-mail address.", nameof(f.ReplyTo)));
        if (needs.NeedsUser && string.IsNullOrWhiteSpace(f.Username)) errors.Add(Error.Validation("This mode needs the mailbox / user name.", nameof(f.Username)));
        if (needs.NeedsOAuth && (string.IsNullOrWhiteSpace(f.TenantId) || string.IsNullOrWhiteSpace(f.ClientId)))
            errors.Add(Error.Validation("OAuth needs the tenant ID and the application (client) ID.", nameof(f.TenantId)));
        if (!string.IsNullOrWhiteSpace(f.AppBaseUrl) && !(Uri.TryCreate(f.AppBaseUrl.Trim(), UriKind.Absolute, out var u) && u.Scheme == "https"))
            errors.Add(Error.Validation("Application address: the full https:// address users open (for links in e-mails).", nameof(f.AppBaseUrl)));
        var pin = string.IsNullOrWhiteSpace(f.CertificateThumbprint) ? null : new string(f.CertificateThumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (pin is not null && pin.Length != 64) errors.Add(Error.Validation("Certificate thumbprint: the SHA-256 fingerprint (64 hexadecimal characters).", nameof(f.CertificateThumbprint)));
        if (f.Security == MailSecurity.None && (needs.NeedsSecret || !string.IsNullOrWhiteSpace(f.Secret)))
            errors.Add(Error.Validation("A password or secret is never sent without encryption: choose STARTTLS or SSL.", nameof(f.Security)));
        if (f.IsEnabled && f.Mode == MailMode.SmtpAuth) { /* allowed; the page warns about Microsoft's end-2026 default change */ }

        var repo = _uow.Repository<CompanyMailSettings>();
        var s = (await repo.ListAsync(null, cancellationToken)).FirstOrDefault();
        var hasSecret = !string.IsNullOrWhiteSpace(f.Secret) || s?.SecretProtected is not null;
        if (needs.NeedsSecret && !hasSecret) errors.Add(Error.Validation(needs.NeedsOAuth ? "Enter the client secret." : "Enter the password.", nameof(f.Secret)));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());

        if (s is null)
        {
            s = new CompanyMailSettings { CompanyId = _company.CompanyId ?? throw new InvalidOperationException("No company is active.") };
            await repo.AddAsync(s, cancellationToken);
        }
        s.IsEnabled = f.IsEnabled; s.Mode = f.Mode.ToString(); s.Host = host; s.Port = f.Port; s.Security = f.Security.ToString();
        s.CertificateThumbprint = pin; s.Username = Clean(f.Username); s.TenantId = Clean(f.TenantId); s.ClientId = Clean(f.ClientId);
        s.FromAddress = f.FromAddress!.Trim(); s.FromName = Clean(f.FromName); s.ReplyTo = Clean(f.ReplyTo); s.AppBaseUrl = Clean(f.AppBaseUrl)?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(f.Secret)) s.SecretProtected = _secrets.Protect(f.Secret);
        if (!needs.NeedsSecret && f.Mode is MailMode.ConnectorRelay) s.SecretProtected = null; // relay: no secret is kept
        s.ModifiedBy = _user.UserName;
        s.ModifiedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<(MailServer Server, CompanyMailSettings Settings)?> ServerAsync(bool requireEnabled, CancellationToken cancellationToken = default)
    {
        var s = (await _uow.Repository<CompanyMailSettings>().ListAsync(null, cancellationToken)).FirstOrDefault();
        if (s is null || (requireEnabled && !s.IsEnabled)) return null;
        var secret = s.SecretProtected is null ? null : _secrets.Unprotect(s.SecretProtected);
        return (new MailServer(Enum.Parse<MailMode>(s.Mode), s.Host, s.Port, Enum.Parse<MailSecurity>(s.Security), s.CertificateThumbprint,
            s.Username, secret, s.TenantId, s.ClientId), s);
    }

    public async Task<Result<string>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var server = await ServerAsync(false, cancellationToken);
        if (server is null) return Result<string>.Failure(Error.Validation("Save the e-mail settings first."));
        string result;
        bool ok;
        try { result = "Connected: " + await _mail.TestConnectionAsync(server.Value.Server, cancellationToken); ok = true; }
        catch (MailDeliveryException ex) { result = "Failed: " + ex.Message; ok = false; }
        server.Value.Settings.LastTestDate = _clock.UtcNow;
        server.Value.Settings.LastTestResult = result.Length > 500 ? result[..500] : result;
        await _uow.SaveChangesAsync(cancellationToken);
        return ok ? Result<string>.Success(result) : Result<string>.Failure(Error.BusinessRule(result));
    }

    public async Task<Result> SendTestAsync(string to, string templateKey, CancellationToken cancellationToken = default)
    {
        to = (to ?? string.Empty).Trim();
        if (!EmailPattern().IsMatch(to)) return Result.Failure(Error.Validation("Enter the address to send the test to.", "to"));
        var server = await ServerAsync(false, cancellationToken);
        if (server is null) return Result.Failure(Error.Validation("Save the e-mail settings first."));
        var look = await CompanyLookAsync(cancellationToken);
        var (subject, html) = await PreviewAsync(templateKey, cancellationToken);
        if (!string.IsNullOrEmpty(server.Value.Settings.AppBaseUrl))
            html = html.Replace("href=\"#\"", $"href=\"{WebUtility.HtmlEncode(server.Value.Settings.AppBaseUrl)}\"", StringComparison.Ordinal);
        try
        {
            await _mail.SendAsync(server.Value.Server, new MailMessage(server.Value.Settings.FromAddress, server.Value.Settings.FromName ?? look.Name + " IT",
                server.Value.Settings.ReplyTo, new[] { to }, "[TEST] " + subject, html, TemplateRenderer.ToText(html), Inline(look, html)), cancellationToken);
            return Result.Success();
        }
        catch (MailDeliveryException ex)
        {
            return Result.Failure(Error.BusinessRule("Not sent: " + ex.Message));
        }
    }

    internal static IReadOnlyList<MailInline> Inline(CompanyLook look, string html) =>
        look.HasLogo && html.Contains("cid:logo", StringComparison.Ordinal) && look.Logo is not null
            ? new[] { new MailInline("logo", look.LogoType ?? "image/png", look.Logo) } : Array.Empty<MailInline>();

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

// ============================================================================ daily reminders

public interface INotificationScheduler
{
    /// <summary>Runs the scheduled reminders once per day for the current company (later calls the same day do nothing).</summary>
    Task<int> RunDailyAsync(bool force = false, CancellationToken cancellationToken = default);
}

internal sealed class NotificationScheduler : INotificationScheduler
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationService _notify;
    private readonly INotificationSettingsService _settings;
    private readonly ICurrentUserService _user;

    public NotificationScheduler(IUnitOfWork uow, IDateTimeProvider clock, INotificationService notify, INotificationSettingsService settings, ICurrentUserService user)
    {
        _uow = uow;
        _clock = clock;
        _notify = notify;
        _settings = settings;
        _user = user;
    }

    private static string When(int days) => days switch { 0 => "today", 1 => "tomorrow", _ => $"in {days} days" };
    private static string D(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public async Task<int> RunDailyAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var today = _clock.UtcToday;
        var marker = await _uow.Repository<NotificationRule>().FirstOrDefaultAsync(r => r.RuleKey == NotificationCatalog.DailyRun, ct);
        if (!force && marker?.LastRunDate == today) return 0;
        var look = await _settings.CompanyLookAsync(ct);
        if (!look.NotificationsEnabled) return 0;   // company switch: no reminders at all
        var sent = 0;

        // ---- warranty expiry
        var rule = await _settings.RuleAsync(NotificationCatalog.WarrantyExpiry, ct);
        if (rule.IsEnabled && rule.DaysBefore.Count > 0)
        {
            var targets = rule.DaysBefore.Select(d => today.AddDays(d)).ToList();
            var rows = await _uow.Repository<Warranty>().QueryAsync(q => q.Where(w => targets.Contains(w.EndDate) && w.Asset.AssetStatus.StatusCode != AssetStatusCodes.Disposed)
                .Select(w => new { w.WarrantyId, w.AssetId, w.Asset.AssetNumber, w.Asset.AssetName, w.EndDate, w.ContractNumber,
                    Provider = w.ProviderVendor == null ? null : w.ProviderVendor.VendorName }), ct);
            var to = await _notify.UsersInRolesAsync(rule.RecipientRoles, ct);
            foreach (var w in rows)
            {
                var days = w.EndDate.DayNumber - today.DayNumber;
                sent += await _notify.NotifyAsync(new NotificationRequest(NotificationCatalog.WarrantyExpiry, new Dictionary<string, string>
                {
                    ["AssetNumber"] = w.AssetNumber, ["AssetName"] = w.AssetName, ["EndDate"] = D(w.EndDate), ["DaysLeft"] = days == 0 ? "ends today" : "ends " + When(days),
                    ["Provider"] = w.Provider ?? "-", ["Contract"] = w.ContractNumber ?? "-"
                }, to, days <= 7 ? NotificationSeverity.Warning : NotificationSeverity.Info, "Asset", w.AssetId.ToString(CultureInfo.InvariantCulture),
                $"/Assets/Details/{w.AssetId}?tab=lifecycle", $"warranty:{w.WarrantyId}:{days}"), ct);
            }
        }

        // ---- licence expiry (owner included)
        rule = await _settings.RuleAsync(NotificationCatalog.LicenseExpiry, ct);
        if (rule.IsEnabled)
        {
            var maxDays = rule.DaysBefore.DefaultIfEmpty(0).Max();
            var horizon = today.AddDays(Math.Max(maxDays, 365));
            var rows = await _uow.Repository<SoftwareLicense>().QueryAsync(q => q.Where(l => l.IsActive && l.ExpiryDate != null && l.ExpiryDate >= today && l.ExpiryDate <= horizon)
                .Select(l => new { l.SoftwareLicenseId, l.SoftwareName, l.LicenseNumber, Expiry = l.ExpiryDate!.Value, l.RenewalReminderDays, l.TotalSeats,
                    Used = l.Allocations.Count(a => a.IsCurrent && !a.IsDeleted), l.ContractOwnerEmployeeId }), ct);
            var to = await _notify.UsersInRolesAsync(rule.RecipientRoles, ct);
            foreach (var l in rows)
            {
                var days = l.Expiry.DayNumber - today.DayNumber;
                // the licence's own reminder period counts as an extra reminder day
                if (!rule.DaysBefore.Contains(days) && days != l.RenewalReminderDays) continue;
                var recipients = to.ToList();
                if (await _notify.EmployeeAsync(l.ContractOwnerEmployeeId, ct) is { } owner) recipients.Add(owner);
                sent += await _notify.NotifyAsync(new NotificationRequest(NotificationCatalog.LicenseExpiry, new Dictionary<string, string>
                {
                    ["Software"] = l.SoftwareName, ["LicenseNumber"] = l.LicenseNumber, ["ExpiryDate"] = D(l.Expiry),
                    ["DaysLeft"] = days == 0 ? "expires today" : "expires " + When(days), ["SeatsUsed"] = l.Used.ToString(CultureInfo.InvariantCulture),
                    ["Seats"] = l.TotalSeats?.ToString(CultureInfo.InvariantCulture) ?? "unlimited"
                }, recipients, days <= 7 ? NotificationSeverity.Warning : NotificationSeverity.Info, "SoftwareLicense",
                l.SoftwareLicenseId.ToString(CultureInfo.InvariantCulture), $"/licenses/{l.SoftwareLicenseId}", $"licence:{l.SoftwareLicenseId}:{days}"), ct);
            }
        }

        // ---- maintenance due, and weekly escalation of overdue jobs
        rule = await _settings.RuleAsync(NotificationCatalog.MaintenanceDue, ct);
        if (rule.IsEnabled)
        {
            var targets = rule.DaysBefore.Select(d => today.AddDays(d)).ToList();
            var rows = await _uow.Repository<AssetMaintenance>().QueryAsync(q => q.Where(m => m.MaintenanceStatus == MaintenanceStatus.Scheduled
                    && (targets.Contains(m.ScheduledDate) || m.ScheduledDate < today))
                .Select(m => new { m.AssetMaintenanceId, m.AssetId, m.Asset.AssetNumber, m.Asset.AssetName, m.ScheduledDate, m.MaintenanceType, m.ProblemDescription }), ct);
            var to = await _notify.UsersInRolesAsync(rule.RecipientRoles, ct);
            var week = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));
            foreach (var m in rows)
            {
                var days = m.ScheduledDate.DayNumber - today.DayNumber;
                var overdue = days < 0;
                sent += await _notify.NotifyAsync(new NotificationRequest(NotificationCatalog.MaintenanceDue, new Dictionary<string, string>
                {
                    ["AssetNumber"] = m.AssetNumber, ["AssetName"] = m.AssetName, ["ScheduledDate"] = D(m.ScheduledDate),
                    ["MaintenanceType"] = Common.DisplayText.Humanize(m.MaintenanceType.ToString()), ["Problem"] = m.ProblemDescription,
                    ["Status"] = overdue ? $"OVERDUE by {-days} day(s)" : "due " + When(days)
                }, to, overdue ? NotificationSeverity.Critical : NotificationSeverity.Info, "Asset", m.AssetId.ToString(CultureInfo.InvariantCulture),
                "/maintenance" + (overdue ? "?view=Overdue" : string.Empty),
                overdue ? $"maint:{m.AssetMaintenanceId}:overdue:{today.Year}-{week}" : $"maint:{m.AssetMaintenanceId}:{days}"), ct);
            }
        }

        // ---- physical verification: weekly digest
        rule = await _settings.RuleAsync(NotificationCatalog.VerificationDue, ct);
        if (rule.IsEnabled && look.AssetAuditEnabled)
        {
            var within = today.AddDays(rule.DaysBefore.DefaultIfEmpty(30).Max());
            var dates = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed
                && a.NextAuditDueDate != null && a.NextAuditDueDate <= within).Select(a => a.NextAuditDueDate!.Value), ct);
            var overdue = dates.Count(d => d < today);
            if (dates.Count > 0)
            {
                var week = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue));
                sent += await _notify.NotifyAsync(new NotificationRequest(NotificationCatalog.VerificationDue, new Dictionary<string, string>
                {
                    ["DueCount"] = (dates.Count - overdue).ToString(CultureInfo.InvariantCulture), ["OverdueCount"] = overdue.ToString(CultureInfo.InvariantCulture)
                }, await _notify.UsersInRolesAsync(rule.RecipientRoles, ct), overdue > 0 ? NotificationSeverity.Warning : NotificationSeverity.Info,
                null, null, "/verification", $"verify:{today.Year}-{week}"), ct);
            }
        }

        // remember the run (bookkeeping row)
        if (marker is null)
        {
            marker = new NotificationRule { RuleKey = NotificationCatalog.DailyRun, IsEnabled = true, EmailEnabled = false, InAppEnabled = false };
            await _uow.Repository<NotificationRule>().AddAsync(marker, ct);
        }
        marker.LastRunDate = today;
        marker.ModifiedBy = _user.UserName;
        marker.ModifiedDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return sent;
    }
}

// ============================================================================ e-mail queue

public interface IMailQueue
{
    /// <summary>Sends due e-mails of the current company. Returns (sent, failed, waiting).</summary>
    Task<(int Sent, int Failed, int Retrying)> ProcessAsync(int max = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueueItemView>> ListAsync(QueueStatus? status, int max = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<QueueStatus, int>> CountsAsync(CancellationToken cancellationToken = default);
    Task<Result> RetryAsync(long id, CancellationToken cancellationToken = default);
    Task<Result> CancelAsync(long id, CancellationToken cancellationToken = default);
}

internal sealed class MailQueue : IMailQueue
{
    /// <summary>Wait before attempt 2, 3, 4, 5, 6 (minutes).</summary>
    private static readonly int[] Backoff = { 1, 5, 15, 60, 240 };

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationSettingsService _settings;
    private readonly IMailSender _mail;

    public MailQueue(IUnitOfWork uow, IDateTimeProvider clock, INotificationSettingsService settings, IMailSender mail)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
        _mail = mail;
    }

    public async Task<(int Sent, int Failed, int Retrying)> ProcessAsync(int max = 50, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var server = await _settings.ServerAsync(requireEnabled: true, ct);
        if (server is null) return (0, 0, 0); // e-mail not configured or switched off: messages wait in the queue
        var look = await _settings.CompanyLookAsync(ct);
        var now = _clock.UtcNow;
        var due = await _uow.Repository<NotificationQueueItem>().ListAsync(x => x.Channel == NotificationChannel.Email
            && x.QueueStatus == QueueStatus.Pending && x.NextAttemptDate <= now, ct);
        int sent = 0, failed = 0, retrying = 0;
        foreach (var item in due.OrderBy(x => x.NextAttemptDate).Take(Math.Clamp(max, 1, 500)))
        {
            item.AttemptCount++;
            try
            {
                await _mail.SendAsync(server.Value.Server, new MailMessage(server.Value.Settings.FromAddress,
                    server.Value.Settings.FromName ?? look.Name + " IT", server.Value.Settings.ReplyTo, new[] { item.RecipientAddress },
                    item.Subject, item.Body, TemplateRenderer.ToText(item.Body), NotificationSettingsService.Inline(look, item.Body)), ct);
                item.QueueStatus = QueueStatus.Sent;
                item.SentDate = _clock.UtcNow;
                item.LastError = null;
                sent++;
            }
            catch (MailDeliveryException ex)
            {
                item.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (ex.Permanent || item.AttemptCount >= item.MaxAttempts)
                {
                    item.QueueStatus = QueueStatus.Failed;
                    failed++;
                }
                else
                {
                    item.NextAttemptDate = _clock.UtcNow.AddMinutes(Backoff[Math.Min(item.AttemptCount - 1, Backoff.Length - 1)]);
                    retrying++;
                }
            }
            await _uow.SaveChangesAsync(ct); // per message: a crash never re-sends what already went out
        }
        return (sent, failed, retrying);
    }

    public async Task<IReadOnlyList<QueueItemView>> ListAsync(QueueStatus? status, int max = 200, CancellationToken cancellationToken = default) =>
        await _uow.Repository<NotificationQueueItem>().QueryAsync(q =>
        {
            if (status is { } s) q = q.Where(x => x.QueueStatus == s);
            return q.OrderByDescending(x => x.NotificationQueueId).Take(Math.Clamp(max, 1, 1000)).Select(x => new QueueItemView(x.NotificationQueueId,
                x.RecipientAddress, x.Subject, x.QueueStatus.ToString(), x.AttemptCount, x.NextAttemptDate, x.SentDate, x.LastError, x.CreatedDate));
        }, cancellationToken);

    public async Task<IReadOnlyDictionary<QueueStatus, int>> CountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<NotificationQueueItem>().QueryAsync(q => q.GroupBy(x => x.QueueStatus).Select(g => new { g.Key, N = g.Count() }), cancellationToken);
        return Enum.GetValues<QueueStatus>().ToDictionary(s => s, s => rows.FirstOrDefault(r => r.Key == s)?.N ?? 0);
    }

    public async Task<Result> RetryAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = await _uow.Repository<NotificationQueueItem>().FirstOrDefaultAsync(x => x.NotificationQueueId == id, cancellationToken);
        if (item is null) return Result.Failure(Error.NotFound("Message not found."));
        if (item.QueueStatus is not (QueueStatus.Failed or QueueStatus.Cancelled)) return Result.Failure(Error.BusinessRule("Only failed or cancelled messages can be retried."));
        item.QueueStatus = QueueStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptDate = _clock.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = await _uow.Repository<NotificationQueueItem>().FirstOrDefaultAsync(x => x.NotificationQueueId == id, cancellationToken);
        if (item is null) return Result.Failure(Error.NotFound("Message not found."));
        if (item.QueueStatus != QueueStatus.Pending) return Result.Failure(Error.BusinessRule("Only waiting messages can be cancelled."));
        item.QueueStatus = QueueStatus.Cancelled;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
