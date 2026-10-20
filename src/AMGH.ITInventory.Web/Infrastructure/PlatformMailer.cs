using System.Net;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.Tenancy;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>The platform operator's own e-mail (demo-request alerts, company keys for customers); not the mail settings of any company.</summary>
public sealed class PlatformMailer
{
    private readonly TenancyOptions _options;
    private readonly IMailSender _sender;
    private readonly ILogger<PlatformMailer> _logger;

    public PlatformMailer(IOptions<TenancyOptions> options, IMailSender sender, ILogger<PlatformMailer> logger)
    {
        _options = options.Value;
        _sender = sender;
        _logger = logger;
    }

    public bool IsConfigured => _options.Smtp.IsConfigured;

    public string? OperatorEmail => string.IsNullOrWhiteSpace(_options.OperatorEmail) ? null : _options.OperatorEmail.Trim();

    /// <summary>Sends one message; false (and a log line) when mail is not set up or the server refuses: callers never fail because of e-mail.</summary>
    public async Task<bool> TrySendAsync(string to, string subject, string html, string text, string? replyTo, CancellationToken ct)
    {
        var smtp = _options.Smtp;
        if (!smtp.IsConfigured || string.IsNullOrWhiteSpace(to)) return false;
        try
        {
            var security = Enum.TryParse<MailSecurity>(smtp.Security, ignoreCase: true, out var s) ? s : MailSecurity.StartTls;
            var server = new MailServer(string.IsNullOrWhiteSpace(smtp.Username) ? MailMode.CustomSmtp : MailMode.SmtpAuth, smtp.Host!, smtp.Port, security, null,
                string.IsNullOrWhiteSpace(smtp.Username) ? null : smtp.Username, string.IsNullOrWhiteSpace(smtp.Password) ? null : smtp.Password, null, null);
            await _sender.SendAsync(server, new MailMessage(smtp.FromAddress!, smtp.FromName, replyTo, new[] { to }, subject, html, text, Array.Empty<MailInline>()), ct);
            return true;
        }
        catch (Exception ex) when (ex is MailDeliveryException or IOException or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException or System.Security.Authentication.AuthenticationException)
        {
            _logger.LogWarning(ex, "The platform e-mail to {To} could not be sent", to);
            return false;
        }
    }

    public static string Html(string text) => WebUtility.HtmlEncode(text);
}
