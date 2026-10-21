using System.Text;
using AMGH.ITInventory.Security.Mfa;
using AMGH.ITInventory.Security.Platform;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class SignInSecurityTests
{
    private static readonly byte[] Rfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_6238_test_vectors(long seconds, string expected) =>
        Assert.Equal(expected, Totp.Code(Rfc, seconds / 30));

    [Fact]
    public void A_code_is_accepted_once_and_within_one_step_of_clock_drift()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime;
        var step = Totp.StepOf(now);
        Assert.Equal(step, Totp.Verify(Rfc, "081804", now, null));
        Assert.Null(Totp.Verify(Rfc, "081804", now, step));                       // replay of the same step
        Assert.Equal(step - 1, Totp.Verify(Rfc, Totp.Code(Rfc, step - 1), now, null)); // phone a little behind
        Assert.Equal(step + 1, Totp.Verify(Rfc, Totp.Code(Rfc, step + 1), now, null)); // phone a little ahead
        Assert.Null(Totp.Verify(Rfc, Totp.Code(Rfc, step + 3), now, null));       // far off
        Assert.Null(Totp.Verify(Rfc, "12345", now, null));
        Assert.Null(Totp.Verify(Rfc, null, now, null));
        Assert.Equal(step, Totp.Verify(Rfc, "081 804", now, null));               // spaces typed from the app are fine
    }

    [Fact]
    public void Base32_round_trips_and_rejects_garbage()
    {
        var secret = Totp.NewSecret();
        Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
        Assert.Equal("MZXW6YTBOI", Totp.ToBase32(Encoding.ASCII.GetBytes("foobar"))); // RFC 4648 test vector
        Assert.Null(Totp.FromBase32("not*base32"));
        Assert.Equal(Totp.FromBase32("JBSW Y3DP"), Totp.FromBase32("jbswy3dp"));
    }

    [Fact]
    public void The_authenticator_address_names_the_issuer_and_account() =>
        Assert.StartsWith("otpauth://totp/EAMS:bob%40acme?secret=ABC&issuer=EAMS&algorithm=SHA1&digits=6&period=30", Totp.Uri("EAMS", "bob@acme", "ABC"));

    [Theory]
    [InlineData("abcde-23456", "ABCDE23456")]
    [InlineData(" ab cde 23456 ", "ABCDE23456")]
    [InlineData("", "")]
    public void Recovery_codes_are_compared_without_dashes_spaces_or_case(string typed, string expected) =>
        Assert.Equal(expected, MfaService.NormaliseRecovery(typed));

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7", true)]
    [InlineData("203.0.113.8", "203.0.113.7", false)]
    [InlineData("10.20.30.40", "10.20.0.0/16", true)]
    [InlineData("10.21.0.1", "10.20.0.0/16", false)]
    [InlineData("10.20.0.255", "10.20.0.0/25, 192.168.1.1", false)]
    [InlineData("192.168.1.1", "10.20.0.0/25, 192.168.1.1", true)]
    [InlineData("192.168.1.130", "192.168.1.0/25", false)]
    [InlineData("::1", "::1", true)]
    [InlineData("2001:db8::5", "2001:db8::/32", true)]
    [InlineData("2001:db9::5", "2001:db8::/32", false)]
    [InlineData("::ffff:10.20.1.1", "10.20.0.0/16", true)]      // an IPv4 address that arrives as IPv6
    [InlineData("1.2.3.4", "", true)]                           // empty list: anywhere
    [InlineData("1.2.3.4", null, true)]
    [InlineData("not-an-ip", "10.0.0.0/8", false)]
    [InlineData(null, "10.0.0.0/8", false)]
    public void The_address_list_allows_only_listed_addresses(string? ip, string? ranges, bool allowed) =>
        Assert.Equal(allowed, IpRules.Allows(ip, ranges));

    [Theory]
    [InlineData("banana", "banana")]
    [InlineData("10.0.0.0/33", "10.0.0.0/33")]
    [InlineData("10.0.0.0/8, 1.2.3", "1.2.3")]
    [InlineData("10.0.0.0/8/9", "10.0.0.0/8/9")]
    public void Bad_entries_are_named(string text, string bad) => Assert.Equal(bad, IpRules.Parse(text).BadEntry);

    [Fact]
    public void Lists_may_use_commas_spaces_and_new_lines() =>
        Assert.Equal(3, IpRules.Parse("1.1.1.1, 2.2.2.2\n3.3.3.3").Rules.Count);

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(0, false, false)]
    public void Administrators_include_the_platform_admin_and_people_who_manage_users(int policy, bool platform, bool required)
    {
        var admin = Web.Infrastructure.SignInGate.IsAdministrator(platform, Array.Empty<string>());
        var security = new CompanySecurity(1, "X", "X", (byte)policy, null, false, null, null, false, null);
        Assert.Equal(required, Web.Infrastructure.SignInGate.Required(security, admin));
        Assert.True(Web.Infrastructure.SignInGate.IsAdministrator(false, new[] { "Users.Manage" }));
        Assert.False(Web.Infrastructure.SignInGate.IsAdministrator(false, new[] { "Assets.View" }));
        Assert.True(Web.Infrastructure.SignInGate.Required(new CompanySecurity(1, "X", "X", 2, null, false, null, null, false, null), false));
    }
}

public class RfidEncodingTests
{
    [Fact]
    public void An_epc_is_24_hex_characters_and_unique_per_asset()
    {
        var a = Application.Labels.RfidEncoding.NewEpc(1, 10);
        var b = Application.Labels.RfidEncoding.NewEpc(1, 11);
        Assert.True(Application.Labels.RfidEncoding.IsEpc(a));
        Assert.StartsWith("A5000001" + "0000000A", a);
        Assert.StartsWith("A5000001" + "0000000B", b);
        Assert.NotEqual(Application.Labels.RfidEncoding.NewEpc(1, 10), a); // the random tail differs
    }

    [Theory]
    [InlineData("A50000010000000A1234ABCD", true)]
    [InlineData("a50000010000000a1234abcd", true)]
    [InlineData("A50000010000000A1234ABC", false)]
    [InlineData("A50000010000000A1234ABCG", false)]
    [InlineData(null, false)]
    public void Only_24_hex_characters_are_an_epc(string? text, bool expected) =>
        Assert.Equal(expected, Application.Labels.RfidEncoding.IsEpc(text));

    [Fact]
    public void The_csv_quotes_fields_that_need_it()
    {
        var csv = Application.Labels.RfidEncoding.Csv(new[] { new Application.Labels.RfidTag("ACME-IT-1", "Laptop, 14\"", "A50000010000000A1234ABCD", "https://x/a/ACME-IT-1") });
        Assert.StartsWith("AssetNumber,AssetName,EPC,Url\r\n", csv);
        Assert.Contains("ACME-IT-1,\"Laptop, 14\"\"\",A50000010000000A1234ABCD,https://x/a/ACME-IT-1\r\n", csv);
    }

    [Fact]
    public void The_zpl_writes_the_epc_and_cannot_be_hijacked_by_field_text()
    {
        var zpl = Application.Labels.RfidEncoding.Zpl(new[] { new Application.Labels.RfidTag("ACME-1", "Evil ^XZ~JA name", "A50000010000000A1234ABCD", "https://x/a/ACME-1") });
        Assert.Contains("^RFW,H,2,12,1^FDA50000010000000A1234ABCD^FS", zpl);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(zpl, @"\^XZ"));   // only the label's own end command
        Assert.DoesNotContain("~JA", zpl);
        Assert.StartsWith("^XA", zpl);
    }
}

public class CompanyKeyFormatTests
{
    [Fact]
    public void A_key_looks_like_EAMS_and_twelve_unambiguous_characters()
    {
        var key = Application.Tenancy.CompanyKeyFormat.Generate();
        Assert.Matches(@"^EAMS-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", key);
        Assert.True(Application.Tenancy.CompanyKeyFormat.IsWellFormed(key));
        Assert.NotEqual(key, Application.Tenancy.CompanyKeyFormat.Generate());
    }

    [Theory]
    [InlineData("EAMS-7K3Q-M9XD-4TWB")]
    [InlineData("eams 7k3q m9xd 4twb")]
    [InlineData("  7K3QM9XD4TWB ")]
    [InlineData("EAMS7K3QM9XD4TWB")]
    public void However_it_is_typed_the_same_key_gives_the_same_hash(string typed) =>
        Assert.Equal(Application.Tenancy.CompanyKeyFormat.Hash("EAMS-7K3Q-M9XD-4TWB"), Application.Tenancy.CompanyKeyFormat.Hash(typed));

    [Theory]
    [InlineData("EAMS-7K3Q-M9XD-4TW")]      // too short
    [InlineData("EAMS-7K3Q-M9XD-4TWBB")]    // too long
    [InlineData("EAMS-0000-1111-OOOO")]     // characters the alphabet leaves out
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_keys_are_rejected_before_the_database_is_asked(string? typed) =>
        Assert.False(Application.Tenancy.CompanyKeyFormat.IsWellFormed(typed));

    [Fact]
    public void Different_keys_have_different_hashes_and_the_hint_is_the_last_four()
    {
        Assert.NotEqual(Application.Tenancy.CompanyKeyFormat.Hash("EAMS-7K3Q-M9XD-4TWB"), Application.Tenancy.CompanyKeyFormat.Hash("EAMS-7K3Q-M9XD-4TWC"));
        Assert.Equal("4TWB", Application.Tenancy.CompanyKeyFormat.Hint("EAMS-7K3Q-M9XD-4TWB"));
        Assert.Equal(64, Application.Tenancy.CompanyKeyFormat.Hash("x").Length);
    }

    [Fact]
    public void Key_status_follows_use_withdrawal_and_expiry()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Application.Tenancy.CompanyKeyInfo Key(DateTime expires, DateTime? used = null, DateTime? revoked = null) =>
            new(1, "ABCD", null, null, null, now.AddDays(-1), "op", expires, used, used is null ? null : "acme", revoked);
        Assert.Equal("Unused", Key(now.AddDays(3)).Status(now));
        Assert.Equal("Expired", Key(now.AddMinutes(-1)).Status(now));
        Assert.Equal("Used", Key(now.AddDays(3), used: now).Status(now));
        Assert.Equal("Revoked", Key(now.AddDays(3), revoked: now).Status(now));
    }
}
