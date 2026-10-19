namespace AMGH.ITInventory.Application.Abstractions.Mail;

/// <summary>The five ways a company can send e-mail (decision 1 of the multi-company plan).</summary>
public enum MailMode
{
    /// <summary>Exchange Online inbound connector (by static IP or certificate): no password. Recommended.</summary>
    ConnectorRelay,
    /// <summary>SMTP AUTH with user name and password (smtp.office365.com). Disabled by default in Exchange Online from end of 2026.</summary>
    SmtpAuth,
    /// <summary>SMTP AUTH with OAuth 2.0 (XOAUTH2), client-credentials token from Microsoft Entra ID.</summary>
    SmtpOAuth,
    /// <summary>Microsoft 365 High Volume Email (internal recipients only).</summary>
    HighVolumeEmail,
    /// <summary>Any other SMTP server or relay (on-premises Exchange, internal relay).</summary>
    CustomSmtp
}

public enum MailSecurity
{
    StartTls,
    SslOnConnect,
    None
}

/// <summary>Connection settings with the secret already decrypted (never stored in this form). PinnedThumbprint: for an internal relay with a private certificate, accept exactly this certificate (SHA-256).</summary>
public sealed record MailServer(MailMode Mode, string Host, int Port, MailSecurity Security, string? PinnedThumbprint,
    string? Username, string? Secret, string? TenantId, string? ClientId);

/// <summary>An image embedded in the message and referenced from the HTML as cid:ContentId (e.g. the company logo).</summary>
public sealed record MailInline(string ContentId, string ContentType, byte[] Content);

public sealed record MailMessage(string FromAddress, string? FromName, string? ReplyTo, IReadOnlyList<string> To, string Subject,
    string HtmlBody, string TextBody, IReadOnlyList<MailInline> Inline);

/// <summary>A failed delivery. <see cref="Permanent"/> (5xx) is not retried; temporary failures (4xx, network) are.</summary>
public sealed class MailDeliveryException : Exception
{
    public MailDeliveryException(string message, bool permanent, Exception? inner = null) : base(message, inner) => Permanent = permanent;
    public bool Permanent { get; }
}

public interface IMailSender
{
    Task SendAsync(MailServer server, MailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Connects, negotiates TLS and authenticates without sending; returns a short description of the server.</summary>
    Task<string> TestConnectionAsync(MailServer server, CancellationToken cancellationToken = default);
}

/// <summary>Standard settings per mode, offered as defaults on the settings page.</summary>
public static class MailModeDefaults
{
    public static (string Host, int Port, MailSecurity Security, bool NeedsUser, bool NeedsSecret, bool NeedsOAuth) For(MailMode mode) => mode switch
    {
        MailMode.ConnectorRelay => ("yourdomain-com.mail.protection.outlook.com", 25, MailSecurity.StartTls, false, false, false),
        MailMode.SmtpAuth => ("smtp.office365.com", 587, MailSecurity.StartTls, true, true, false),
        MailMode.SmtpOAuth => ("smtp.office365.com", 587, MailSecurity.StartTls, true, true, true),
        MailMode.HighVolumeEmail => ("smtp-hve.office365.com", 587, MailSecurity.StartTls, true, true, false),
        _ => ("", 25, MailSecurity.StartTls, false, false, false)
    };
}

/// <summary>Encrypts secrets for storage (ASP.NET Data Protection in the web host).</summary>
public interface ISecretProtector
{
    string Protect(string secret);
    string? Unprotect(string protectedSecret);
}
