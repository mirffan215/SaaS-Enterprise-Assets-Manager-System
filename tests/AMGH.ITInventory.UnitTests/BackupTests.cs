using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class BackupTests
{
    [Theory]
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_Test_20261020_101500.bak", true)]
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_test_20261020_101500.bak", true)]     // SQL Server names are not case-sensitive
    [InlineData("ITInv_Tenant_Test", "itinv_tenant_test_20261020_101500.bak", false)]    // the prefix is always written as ITInv_Tenant_
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_Other_20261020_101500.bak", false)]  // another company's backup
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_Test_20261020_101500.bak.exe", false)]
    [InlineData("ITInv_Tenant_Test", "..\\ITInv_Tenant_Test_20261020_101500.bak", false)] // no path tricks
    [InlineData("ITInv_Tenant_Test", "C:\\x\\ITInv_Tenant_Test_20261020_101500.bak", false)]
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_Test_2026_101500.bak", false)]
    [InlineData("ITInv_Tenant_Test", "", false)]
    [InlineData("ITInv_Tenant_Test", "ITInv_Tenant_Test_20261020_101500_extra.bak", false)]
    public void Only_a_companys_own_backup_files_are_accepted(string database, string file, bool expected) =>
        Assert.Equal(expected, TenantBackupService.IsBackupOf(database, file));

    private static DefaultHttpContext WithCookie(string? value)
    {
        var ctx = new DefaultHttpContext();
        if (value is not null) ctx.Request.Headers.Cookie = OperatorSession.CookieName + "=" + value;
        return ctx;
    }

    private static string CookieValueSet(DefaultHttpContext ctx)
    {
        var header = ctx.Response.Headers.SetCookie.ToString();
        var start = header.IndexOf('=') + 1;
        return Uri.UnescapeDataString(header[start..header.IndexOf(';')]);
    }

    [Fact]
    public void An_operator_session_lasts_thirty_minutes_and_not_longer()
    {
        var session = new OperatorSession(new EphemeralDataProtectionProvider());
        var start = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
        var issued = new DefaultHttpContext();
        session.Start(issued, start);
        var cookie = CookieValueSet(issued);

        Assert.True(session.IsActive(WithCookie(cookie), start.AddMinutes(29)));
        Assert.False(session.IsActive(WithCookie(cookie), start.AddMinutes(31)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-valid-protected-value")]
    public void Missing_or_forged_cookies_are_not_a_session(string? value) =>
        Assert.False(new OperatorSession(new EphemeralDataProtectionProvider()).IsActive(WithCookie(value)));

    [Fact]
    public void A_cookie_from_another_installation_is_refused()
    {
        var issued = new DefaultHttpContext();
        new OperatorSession(new EphemeralDataProtectionProvider()).Start(issued);
        var cookie = CookieValueSet(issued);
        Assert.False(new OperatorSession(new EphemeralDataProtectionProvider()).IsActive(WithCookie(cookie)));   // different keys
    }
}
