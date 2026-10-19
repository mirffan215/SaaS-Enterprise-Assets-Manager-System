using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json.Serialization;
using AMGH.ITInventory.Application.Abstractions.Mail;

namespace AMGH.ITInventory.Infrastructure.Mail;

/// <summary>
/// SMTP client (RFC 5321) for the five sending modes: STARTTLS (RFC 3207) or implicit TLS, AUTH LOGIN, and
/// AUTH XOAUTH2 for Microsoft 365 OAuth. Written in-house because System.Net.Mail.SmtpClient cannot do OAuth.
/// One connection per message (low volume); 30-second timeouts; TLS 1.2 or later.
/// </summary>
internal sealed class SmtpMailSender : IMailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IOAuthTokenProvider _tokens;

    public SmtpMailSender(IOAuthTokenProvider tokens) => _tokens = tokens;

    public async Task SendAsync(MailServer server, MailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(message);
        var mime = MimeBuilder.Build(message, DateTimeOffset.UtcNow);
        await using var session = await SmtpSession.OpenAsync(server, _tokens, cancellationToken);
        await session.ExpectAsync(await session.CommandAsync($"MAIL FROM:<{MimeBuilder.Address(message.FromAddress)}>", cancellationToken), 250);
        foreach (var to in message.To)
            await session.ExpectAsync(await session.CommandAsync($"RCPT TO:<{MimeBuilder.Address(to)}>", cancellationToken), 250, 251);
        await session.ExpectAsync(await session.CommandAsync("DATA", cancellationToken), 354);
        await session.WriteDataAsync(mime, cancellationToken);
        await session.ExpectAsync(await session.ReadAsync(cancellationToken), 250);
        await session.QuitAsync(cancellationToken);
    }

    public async Task<string> TestConnectionAsync(MailServer server, CancellationToken cancellationToken = default)
    {
        await using var session = await SmtpSession.OpenAsync(server, _tokens, cancellationToken);
        var info = session.Description;
        await session.QuitAsync(cancellationToken);
        return info;
    }

    // ======================================================================== session

    private sealed class SmtpSession : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private Stream _stream;
        private StreamReader _reader;
        private readonly string _host;
        private HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
        public string Description { get; private set; } = string.Empty;

        private SmtpSession(TcpClient tcp, Stream stream, string host)
        {
            _tcp = tcp;
            _stream = stream;
            _reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            _host = host;
        }

        public static async Task<SmtpSession> OpenAsync(MailServer s, IOAuthTokenProvider tokens, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(s.Host)) throw new MailDeliveryException("No mail server is configured.", true);
            var tcp = new TcpClient { ReceiveTimeout = (int)Timeout.TotalMilliseconds, SendTimeout = (int)Timeout.TotalMilliseconds };
            try
            {
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(Timeout);
                    await tcp.ConnectAsync(s.Host, s.Port, connect.Token);
                }
                Stream stream = tcp.GetStream();
                if (s.Security == MailSecurity.SslOnConnect) stream = await TlsAsync(stream, s.Host, s.PinnedThumbprint, ct);
                var session = new SmtpSession(tcp, stream, s.Host);
                var greeting = await session.ReadAsync(ct);
                await session.ExpectAsync(greeting, 220);
                await session.EhloAsync(ct);
                if (s.Security == MailSecurity.StartTls)
                {
                    if (!session._capabilities.Contains("STARTTLS"))
                        throw new MailDeliveryException($"{s.Host} does not offer STARTTLS. Use 'SSL on connect' (port 465) or check the port.", true);
                    await session.ExpectAsync(await session.CommandAsync("STARTTLS", ct), 220);
                    session._stream = await TlsAsync(session._stream, s.Host, s.PinnedThumbprint, ct);
                    session._reader = new StreamReader(session._stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    await session.EhloAsync(ct);
                }
                await session.AuthenticateAsync(s, tokens, ct);
                session.Description = $"{s.Host}:{s.Port.ToString(CultureInfo.InvariantCulture)} · " +
                    (session._stream is SslStream ssl ? $"TLS {ssl.SslProtocol}" : "no encryption") +
                    (string.IsNullOrEmpty(s.Username) ? " · no sign-in" : $" · signed in as {s.Username}") +
                    (s.Mode == MailMode.SmtpOAuth ? " (OAuth 2.0)" : string.Empty);
                return session;
            }
            catch (MailDeliveryException)
            {
                tcp.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or AuthenticationException)
            {
                tcp.Dispose();
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
                throw new MailDeliveryException(ex switch
                {
                    AuthenticationException => $"TLS with {s.Host} failed: {ex.Message} (certificate not trusted?)",
                    OperationCanceledException => $"No answer from {s.Host}:{s.Port} within {Timeout.TotalSeconds:0} seconds.",
                    _ => $"Cannot connect to {s.Host}:{s.Port}: {ex.Message}"
                }, permanent: false, ex);
            }
        }

        private static async Task<Stream> TlsAsync(Stream inner, string host, string? pinned, CancellationToken ct)
        {
            var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Timeout);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                // Normal validation; additionally, a pinned private certificate (internal relay) is accepted - that exact certificate only.
                RemoteCertificateValidationCallback = (_, cert, _, errors) => errors == SslPolicyErrors.None || CertificatePin.Matches(cert, pinned)
            }, deadline.Token);
            return ssl;
        }

        private async Task EhloAsync(CancellationToken ct)
        {
            var reply = await CommandAsync("EHLO " + LocalName(), ct);
            await ExpectAsync(reply, 250);
            _capabilities = new HashSet<string>(reply.Lines.Skip(1).Select(l => l.Split(' ')[0]), StringComparer.OrdinalIgnoreCase);
            foreach (var l in reply.Lines.Where(l => l.StartsWith("AUTH ", StringComparison.OrdinalIgnoreCase)))
                foreach (var m in l.Split(' ').Skip(1)) _capabilities.Add("AUTH=" + m);
        }

        private async Task AuthenticateAsync(MailServer s, IOAuthTokenProvider tokens, CancellationToken ct)
        {
            if (s.Mode == MailMode.ConnectorRelay || string.IsNullOrEmpty(s.Username)) return;
            if (s.Mode == MailMode.SmtpOAuth)
            {
                if (string.IsNullOrEmpty(s.TenantId) || string.IsNullOrEmpty(s.ClientId) || string.IsNullOrEmpty(s.Secret))
                    throw new MailDeliveryException("OAuth needs the tenant ID, client ID and client secret.", true);
                var token = await tokens.GetTokenAsync(s.TenantId, s.ClientId, s.Secret, ct);
                var xoauth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"user={s.Username}\u0001auth=Bearer {token}\u0001\u0001"));
                var r = await CommandAsync("AUTH XOAUTH2 " + xoauth, ct, secret: true);
                if (r.Code == 334) r = await CommandAsync(string.Empty, ct); // server sent an error challenge: finish the exchange
                if (r.Code != 235) throw new MailDeliveryException($"OAuth sign-in refused by the server ({r.Code} {r.Text}). Check the app's SMTP.SendAsApp permission and the mailbox.", true);
                return;
            }
            var login = await CommandAsync("AUTH LOGIN", ct);
            await ExpectAsync(login, 334);
            await ExpectAsync(await CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Username)), ct, secret: true), 334);
            var done = await CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Secret ?? string.Empty)), ct, secret: true);
            if (done.Code != 235)
                throw new MailDeliveryException($"Sign-in refused ({done.Code} {done.Text}). Check the user name and password" +
                    (s.Host.Contains("office365", StringComparison.OrdinalIgnoreCase) ? ", and that SMTP AUTH is enabled for the mailbox in Exchange Online." : "."), true);
        }

        public async Task<Reply> CommandAsync(string line, CancellationToken ct, bool secret = false)
        {
            if (line.Contains('\r', StringComparison.Ordinal) || line.Contains('\n', StringComparison.Ordinal))
                throw new MailDeliveryException("Invalid SMTP command.", true);
            await WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), ct);
            return await ReadAsync(ct);
        }

        public async Task<Reply> ReadAsync(CancellationToken ct)
        {
            var lines = new List<string>();
            // TcpClient.ReceiveTimeout does not apply to asynchronous reads: every read gets its own deadline, so a
            // server that stops answering can never hang the sender.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Timeout);
            while (true)
            {
                string? line;
                try { line = await _reader.ReadLineAsync(deadline.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new MailDeliveryException($"{_host} stopped answering (no reply within {Timeout.TotalSeconds:0} seconds).", false);
                }
                if (line is null) throw new MailDeliveryException($"{_host} closed the connection.", false);
                if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                    throw new MailDeliveryException($"Unexpected answer from {_host}: {line}", false);
                lines.Add(line.Length > 4 ? line[4..] : string.Empty);
                if (line.Length == 3 || line[3] == ' ') return new Reply(code, lines);
            }
        }

        public Task ExpectAsync(Reply reply, params int[] codes)
        {
            if (codes.Contains(reply.Code)) return Task.CompletedTask;
            // 4xx: temporary (retry later); 5xx: permanent (e.g. unknown recipient, relay denied)
            throw new MailDeliveryException($"{_host} answered {reply.Code} {reply.Text}", permanent: reply.Code >= 500);
        }

        /// <summary>Message body with dot-stuffing (RFC 5321 4.5.2) and the closing "." line.</summary>
        public async Task WriteDataAsync(string mime, CancellationToken ct)
        {
            var sb = new StringBuilder(mime.Length + 64);
            foreach (var line in mime.Split("\r\n"))
            {
                if (line.StartsWith('.')) sb.Append('.');
                sb.Append(line).Append("\r\n");
            }
            sb.Append(".\r\n");
            await WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        }

        private async Task WriteAsync(byte[] bytes, CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(Timeout);
            try
            {
                await _stream.WriteAsync(bytes, deadline.Token);
                await _stream.FlushAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new MailDeliveryException($"Sending to {_host} timed out.", false);
            }
            catch (IOException ex)
            {
                throw new MailDeliveryException($"Connection to {_host} lost: {ex.Message}", false, ex);
            }
        }

        public async Task QuitAsync(CancellationToken ct)
        {
            try { await CommandAsync("QUIT", ct); } catch (MailDeliveryException) { /* the message is already accepted */ }
        }

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            await _stream.DisposeAsync();
            _tcp.Dispose();
        }

        private static string LocalName()
        {
            var name = System.Net.Dns.GetHostName();
            return string.IsNullOrWhiteSpace(name) || name.Any(c => c is ' ' or '\r' or '\n') ? "localhost" : name;
        }
    }

    internal sealed record Reply(int Code, IReadOnlyList<string> Lines)
    {
        public string Text => string.Join(" ", Lines);
    }
}

/// <summary>Certificate pinning for internal relays: the SHA-256 thumbprint must match exactly (spaces/colons ignored).</summary>
internal static class CertificatePin
{
    public static string? Normalize(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint) ? null : new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    public static bool Matches(System.Security.Cryptography.X509Certificates.X509Certificate? cert, string? pinned)
    {
        var expected = Normalize(pinned);
        if (cert is null || expected is null || expected.Length != 64) return false;
        var actual = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected));
    }
}

// ============================================================================ MIME

/// <summary>Builds an RFC 5322 / MIME message: UTF-8 headers (RFC 2047), HTML + plain text, inline images.</summary>
internal static class MimeBuilder
{
    public static string Build(MailMessage m, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        void H(string name, string value) => sb.Append(name).Append(": ").Append(value).Append("\r\n");
        var domain = Address(m.FromAddress).Split('@').Last();
        H("From", Mailbox(m.FromName, m.FromAddress));
        H("To", string.Join(", ", m.To.Select(t => "<" + Address(t) + ">")));
        if (!string.IsNullOrWhiteSpace(m.ReplyTo)) H("Reply-To", "<" + Address(m.ReplyTo) + ">");
        H("Subject", Encode(m.Subject));
        H("Date", now.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture));
        H("Message-ID", $"<{Guid.NewGuid():N}@{domain}>");
        H("MIME-Version", "1.0");
        H("Auto-Submitted", "auto-generated"); // RFC 3834: no out-of-office loops
        var alt = "alt_" + Guid.NewGuid().ToString("N");
        if (m.Inline.Count > 0)
        {
            var rel = "rel_" + Guid.NewGuid().ToString("N");
            H("Content-Type", $"multipart/related; boundary=\"{rel}\"; type=\"multipart/alternative\"");
            sb.Append("\r\n--").Append(rel).Append("\r\n");
            Alternative(sb, alt, m);
            foreach (var img in m.Inline)
            {
                sb.Append("\r\n--").Append(rel).Append("\r\n");
                sb.Append("Content-Type: ").Append(img.ContentType).Append("\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n");
                sb.Append("Content-ID: <").Append(img.ContentId).Append(">\r\n");
                sb.Append("Content-Disposition: inline\r\n\r\n");
                sb.Append(Base64Lines(img.Content));
            }
            sb.Append("\r\n--").Append(rel).Append("--\r\n");
        }
        else
        {
            Alternative(sb, alt, m, headerOnly: false, topLevel: true);
        }
        return sb.ToString();
    }

    private static void Alternative(StringBuilder sb, string boundary, MailMessage m, bool headerOnly = false, bool topLevel = false)
    {
        sb.Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary).Append("\"\r\n");
        if (headerOnly) return;
        sb.Append("\r\n--").Append(boundary).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Base64Lines(Encoding.UTF8.GetBytes(m.TextBody)));
        sb.Append("\r\n--").Append(boundary).Append("\r\n");
        sb.Append("Content-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Base64Lines(Encoding.UTF8.GetBytes(m.HtmlBody)));
        sb.Append("\r\n--").Append(boundary).Append("--\r\n");
        _ = topLevel;
    }

    private static string Base64Lines(byte[] data)
    {
        var b64 = Convert.ToBase64String(data);
        var sb = new StringBuilder(b64.Length + b64.Length / 76 * 2 + 2);
        for (var i = 0; i < b64.Length; i += 76) sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>RFC 2047 encoded-word when needed (Arabic names and subjects); header injection is impossible (no CR/LF survives).</summary>
    public static string Encode(string value)
    {
        value = Clean(value);
        if (value.All(c => c is >= ' ' and <= '~')) return value;
        return "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static string Mailbox(string? name, string address) =>
        string.IsNullOrWhiteSpace(name) ? "<" + Address(address) + ">" : Encode(name.Replace("\"", "'", StringComparison.Ordinal)) + " <" + Address(address) + ">";

    /// <summary>An address safe for SMTP commands and headers: one '@', no spaces, angle brackets or control characters.</summary>
    public static string Address(string address)
    {
        var a = (address ?? string.Empty).Trim();
        if (a.Length is < 3 or > 254 || a.Count(c => c == '@') != 1 || a.Any(c => c <= ' ' || c is '<' or '>' or ',' or ';' or '"' or '\u007f'))
            throw new MailDeliveryException($"Invalid e-mail address: {Clean(a)}", permanent: true);
        return a;
    }

    private static string Clean(string value) => new((value ?? string.Empty).Where(c => c is not ('\r' or '\n' or '\0')).ToArray());
}

// ============================================================================ OAuth 2.0 tokens (Microsoft Entra ID)

public interface IOAuthTokenProvider
{
    Task<string> GetTokenAsync(string tenantId, string clientId, string clientSecret, CancellationToken cancellationToken);
}

/// <summary>Client-credentials token for SMTP (scope https://outlook.office365.com/.default), cached until shortly before expiry.</summary>
internal sealed class EntraTokenProvider : IOAuthTokenProvider
{
    // one long-lived client for the singleton (the recommended HttpClient pattern; no socket exhaustion)
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _cache = new();

    public async Task<string> GetTokenAsync(string tenantId, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var key = tenantId + "|" + clientId;
        if (_cache.TryGetValue(key, out var c) && c.Expires > DateTimeOffset.UtcNow.AddMinutes(5)) return c.Token;
        if (!Guid.TryParse(tenantId, out _) && !tenantId.Contains('.', StringComparison.Ordinal))
            throw new MailDeliveryException("The tenant ID is not valid.", true);
        var client = Client;
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["client_secret"] = clientSecret,
            ["scope"] = "https://outlook.office365.com/.default", ["grant_type"] = "client_credentials"
        });
        try
        {
            using var response = await client.PostAsync(new Uri($"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token"), body, cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(token?.AccessToken))
                throw new MailDeliveryException($"Microsoft Entra refused the token request ({(int)response.StatusCode} {token?.ErrorDescription?.Split('\n')[0]}).", true);
            _cache[key] = (token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn)));
            return token.AccessToken;
        }
        catch (HttpRequestException ex)
        {
            throw new MailDeliveryException("Cannot reach login.microsoftonline.com (outbound HTTPS needed for OAuth): " + ex.Message, false, ex);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
}
