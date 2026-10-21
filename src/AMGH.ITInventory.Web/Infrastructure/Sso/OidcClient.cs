using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Shared.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AMGH.ITInventory.Web.Infrastructure.Sso;

public sealed record OidcSettings(string Authority, string ClientId, string ClientSecret, string? AllowedDomain);

public sealed record OidcIdentity(string Email, string? Name);

/// <summary>
/// OpenID Connect sign-in, authorization-code flow with PKCE (Microsoft Entra ID, Google Workspace, Okta, Keycloak and any other provider).
/// The provider's keys and addresses come from its discovery document (cached); the ID token's signature, issuer, audience, lifetime and
/// nonce are all checked before the e-mail address in it is believed.
/// </summary>
public sealed class OidcClient
{
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Managers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient _http;

    public OidcClient() => _http = SharedHttp;

    /// <summary>For tests: talks to a fake provider.</summary>
    internal OidcClient(HttpMessageHandler handler) => _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };

    private bool Insecure => _http != SharedHttp;

    public static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = RandomToken();
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<OpenIdConnectConfiguration> ConfigurationAsync(string authority, CancellationToken ct)
    {
        var key = authority.TrimEnd('/');
        var manager = Managers.GetOrAdd(_http == SharedHttp ? key : "test:" + key + ":" + _http.GetHashCode(), _ =>
            new ConfigurationManager<OpenIdConnectConfiguration>(key + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_http) { RequireHttps = !Insecure }) { AutomaticRefreshInterval = TimeSpan.FromHours(6) });
        return await manager.GetConfigurationAsync(ct);
    }

    public async Task<Uri> BuildAuthorizeUrlAsync(OidcSettings settings, string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct)
    {
        var config = await ConfigurationAsync(settings.Authority, ct);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        var separator = config.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri(config.AuthorizationEndpoint + separator + string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));
    }

    public async Task<Result<OidcIdentity>> RedeemAsync(OidcSettings settings, string redirectUri, string code, string codeVerifier, string expectedNonce, CancellationToken ct)
    {
        const string Failed = "Single sign-on did not complete. Try again, or sign in with your password.";
        try
        {
            var config = await ConfigurationAsync(settings.Authority, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                    ["code_verifier"] = codeVerifier
                })
            };
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Result<OidcIdentity>.Failure(Error.Validation(Failed, "sso"));
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            if (string.IsNullOrEmpty(token?.IdToken)) return Result<OidcIdentity>.Failure(Error.Validation(Failed, "sso"));

            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.IdToken, new TokenValidationParameters
            {
                ValidIssuer = config.Issuer,
                ValidAudience = settings.ClientId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            });
            if (!validation.IsValid) return Result<OidcIdentity>.Failure(Error.Validation(Failed, "sso"));
            var claims = validation.Claims;
            if (!claims.TryGetValue("nonce", out var nonce) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce?.ToString() ?? string.Empty), Encoding.UTF8.GetBytes(expectedNonce)))
                return Result<OidcIdentity>.Failure(Error.Validation(Failed, "sso"));
            if (claims.TryGetValue("email_verified", out var verified) && string.Equals(verified?.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                return Result<OidcIdentity>.Failure(Error.Validation("Your provider says this e-mail address is not verified.", "sso"));

            var email = First(claims, "email") ?? First(claims, "preferred_username") ?? First(claims, "upn");
            if (email is null || !email.Contains('@', StringComparison.Ordinal))
                return Result<OidcIdentity>.Failure(Error.Validation("Your provider did not send an e-mail address.", "sso"));
            if (!string.IsNullOrEmpty(settings.AllowedDomain) && !email.EndsWith("@" + settings.AllowedDomain, StringComparison.OrdinalIgnoreCase))
                return Result<OidcIdentity>.Failure(Error.Validation("That e-mail address is not from the domain this company accepts.", "sso"));
            return Result<OidcIdentity>.Success(new OidcIdentity(email, First(claims, "name")));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            return Result<OidcIdentity>.Failure(Error.Validation(Failed, "sso"));
        }
    }

    private static string? First(IDictionary<string, object> claims, string name) => claims.TryGetValue(name, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString() : null;

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }
}
