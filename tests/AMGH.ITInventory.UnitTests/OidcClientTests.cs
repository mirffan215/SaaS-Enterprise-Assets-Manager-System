using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMGH.ITInventory.Web.Infrastructure.Sso;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class OidcClientTests
{
    private const string Authority = "https://idp.test";
    private const string Client = "eams-client";

    private sealed class FakeProvider : HttpMessageHandler
    {
        public RsaSecurityKey SigningKey { get; } = new(RSA.Create(2048)) { KeyId = "k1" };
        public RsaSecurityKey KeyInJwks { get; set; }
        public Func<IDictionary<string, object>, IDictionary<string, object>> Claims { get; set; } = c => c;
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public string? LastTokenRequest { get; private set; }

        public FakeProvider() => KeyInJwks = SigningKey;

        public string Mint(string nonce, Func<Dictionary<string, object>, Dictionary<string, object>>? tweak = null, RsaSecurityKey? signWith = null)
        {
            var claims = new Dictionary<string, object> { ["sub"] = "u1", ["email"] = "bob@acme.test", ["nonce"] = nonce, ["name"] = "Bob" };
            if (tweak is not null) claims = tweak(claims);
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Authority, Audience = Client, Claims = claims, Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(signWith ?? SigningKey, SecurityAlgorithms.RsaSha256)
            });
        }

        public string? NextIdToken { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("openid-configuration", StringComparison.Ordinal))
                return Json(new { issuer = Authority, authorization_endpoint = Authority + "/authorize", token_endpoint = Authority + "/token", jwks_uri = Authority + "/jwks" });
            if (path == "/jwks")
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(KeyInJwks.Rsa.ExportParameters(false)) { KeyId = "k1" });
                return Json(new { keys = new[] { new { kty = "RSA", kid = "k1", use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
            }
            if (path == "/token")
            {
                LastTokenRequest = await request.Content!.ReadAsStringAsync(ct);
                return TokenStatus == HttpStatusCode.OK ? Json(new { id_token = NextIdToken }) : new HttpResponseMessage(TokenStatus);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object o) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };
    }

    private static OidcSettings Settings(string? domain = null) => new(Authority, Client, "s3cret", domain);

    [Fact]
    public async Task The_authorize_address_carries_pkce_state_and_nonce()
    {
        var client = new OidcClient(new FakeProvider());
        var (_, challenge) = OidcClient.NewPkce();
        var url = await client.BuildAuthorizeUrlAsync(Settings(), "https://app/Account/SsoCallback", "st", "no", challenge, default);
        Assert.StartsWith("https://idp.test/authorize?", url.ToString());
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(Client, q["client_id"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal(challenge, q["code_challenge"]);
        Assert.Equal("st", q["state"]);
        Assert.Equal("no", q["nonce"]);
        Assert.Equal("openid email profile", q["scope"]);
    }

    [Fact]
    public async Task A_valid_token_gives_the_email_and_sends_the_pkce_verifier_and_secret()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = idp.Mint("n1");
        var result = await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "the-code", "the-verifier", "n1", default);
        Assert.True(result.IsSuccess);
        Assert.Equal("bob@acme.test", result.Value.Email);
        Assert.Contains("code_verifier=the-verifier", idp.LastTokenRequest);
        Assert.Contains("client_secret=s3cret", idp.LastTokenRequest);
        Assert.Contains("code=the-code", idp.LastTokenRequest);
    }

    [Fact]
    public async Task A_token_for_another_login_attempt_is_refused()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = idp.Mint("someone-elses-nonce");
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = idp.Mint("n1", signWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" });
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);
    }

    [Fact]
    public async Task A_token_for_another_application_is_refused()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority, Audience = "someone-else", Claims = new Dictionary<string, object> { ["email"] = "bob@acme.test", ["nonce"] = "n1" },
            Expires = DateTime.UtcNow.AddMinutes(5), SigningCredentials = new SigningCredentials(idp.SigningKey, SecurityAlgorithms.RsaSha256)
        });
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority, Audience = Client, Claims = new Dictionary<string, object> { ["email"] = "bob@acme.test", ["nonce"] = "n1" },
            NotBefore = DateTime.UtcNow.AddHours(-2), Expires = DateTime.UtcNow.AddHours(-1), SigningCredentials = new SigningCredentials(idp.SigningKey, SecurityAlgorithms.RsaSha256)
        });
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);
    }

    [Fact]
    public async Task An_unverified_email_or_a_wrong_domain_is_refused()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = idp.Mint("n1", c => { c["email_verified"] = false; return c; });
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);

        idp.NextIdToken = idp.Mint("n1");
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings("other.test"), "https://app/cb", "c", "v", "n1", default)).IsFailure);
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings("acme.test"), "https://app/cb", "c", "v", "n1", default)).IsSuccess);
    }

    [Fact]
    public async Task The_work_email_claim_is_used_when_there_is_no_email_claim()
    {
        var idp = new FakeProvider();
        idp.NextIdToken = idp.Mint("n1", c => { c.Remove("email"); c["preferred_username"] = "bob@acme.test"; return c; });
        Assert.Equal("bob@acme.test", (await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).Value.Email);
    }

    [Fact]
    public async Task A_refusal_by_the_token_endpoint_is_a_failed_result_not_an_exception()
    {
        var idp = new FakeProvider { TokenStatus = HttpStatusCode.BadRequest };
        Assert.True((await new OidcClient(idp).RedeemAsync(Settings(), "https://app/cb", "c", "v", "n1", default)).IsFailure);
    }

    [Fact]
    public void Pkce_challenge_is_the_sha256_of_the_verifier()
    {
        var (verifier, challenge) = OidcClient.NewPkce();
        Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'), challenge);
        Assert.NotEqual(OidcClient.RandomToken(), OidcClient.RandomToken());
    }
}
