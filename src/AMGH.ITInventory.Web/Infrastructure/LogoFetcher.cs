using System.Net;
using System.Net.Sockets;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Web.Infrastructure;

public interface ILogoFetcher
{
    Task<Result<CompanyLogo>> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// Downloads a company logo from an https address once (it is then stored like an upload; pages never load it from the
/// other site, so the Content-Security-Policy stays 'self'). Protected against server-side request forgery:
/// https on port 443 only, no redirects, 10 s, at most 512 KB, and every resolved address is checked AT CONNECT TIME
/// (loopback, private, link-local, CGNAT, multicast and unspecified addresses are refused - this also defeats DNS rebinding).
/// The image signature is verified again by the company service.
/// </summary>
internal sealed class LogoFetcher : ILogoFetcher
{
    private const int MaxBytes = 512 * 1024;
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var allowed = addresses.Where(IsPublic).ToArray();
            if (allowed.Length == 0 || allowed.Length != addresses.Length)
                throw new HttpRequestException("This address points to an internal or reserved network and is not allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    }) { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<Result<CompanyLogo>> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType is UriHostNameType.Unknown)
            return Fail("Enter a full https:// address of a PNG or JPEG image (port 443, no user name).");
        if (IPAddress.TryParse(uri.IdnHost, out var literal) && !IsPublic(literal))
            return Fail("This address points to an internal or reserved network and is not allowed.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("image/png, image/jpeg");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400) return Fail("The address redirects elsewhere; enter the image's final address.");
            if (!response.IsSuccessStatusCode) return Fail($"The address answered {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxBytes) return Fail("The image is larger than 512 KB.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return Fail("The image is larger than 512 KB.");
            }
            var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return Result<CompanyLogo>.Success(new CompanyLogo(buffer.ToArray(), type, uri.AbsoluteUri));
        }
        catch (HttpRequestException ex) { return Fail("The logo could not be downloaded: " + ex.Message); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Fail("The address did not answer within 10 seconds."); }
    }

    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) || b[0] >= 224);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);   // fc00::/7 unique local
        }
        return false;
    }

    private static Result<CompanyLogo> Fail(string message) => Result<CompanyLogo>.Failure(Error.Validation(message, "LogoUrl"));
}
