using System.Net;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Controllers;
using AMGH.ITInventory.Web.Infrastructure;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class BackupScheduleTests
{
    [Fact]
    public void Off_is_never_due() => Assert.False(new BackupSchedule().IsDue(new DateTime(2026, 10, 20, 12, 0, 0)));

    [Fact]
    public void Daily_is_due_after_the_time_until_it_has_run()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00" };
        Assert.False(s.IsDue(new DateTime(2026, 10, 20, 1, 0, 0)) && s.LastRunLocal is not null);
        Assert.True(s.IsDue(new DateTime(2026, 10, 20, 2, 1, 0)));
        s.LastRunLocal = new DateTime(2026, 10, 20, 2, 1, 5);
        Assert.False(s.IsDue(new DateTime(2026, 10, 20, 23, 0, 0)));
        Assert.True(s.IsDue(new DateTime(2026, 10, 21, 2, 0, 0)));
    }

    [Fact]
    public void Weekly_uses_the_chosen_day()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Weekly, Day = 0, Time = "03:30" }; // Sunday
        Assert.Equal(new DateTime(2026, 10, 18, 3, 30, 0), s.LastDueSlot(new DateTime(2026, 10, 20, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 10, 11, 3, 30, 0), s.LastDueSlot(new DateTime(2026, 10, 18, 3, 0, 0)));
    }

    [Fact]
    public void Malformed_time_is_not_due()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "25:99" };
        Assert.Null(s.LastDueSlot(DateTime.Now));
    }

    [Theory]
    [InlineData("short1A!", false)]
    [InlineData("alllowercaseletters", false)]
    [InlineData("Correct-Horse-9", true)]
    public void Zip_password_rule(string password, bool ok) =>
        Assert.Equal(ok, CompanyBackupController.PasswordProblem(password) is null);
}

public class BackupOverdueAndBlobTests
{
    [Fact]
    public void A_missed_backup_is_overdue_only_after_the_grace_time()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00" };
        Assert.False(s.IsOverdue(new DateTime(2026, 10, 20, 5, 0, 0), TimeSpan.FromHours(6)));
        Assert.True(s.IsOverdue(new DateTime(2026, 10, 20, 9, 0, 0), TimeSpan.FromHours(6)));
        s.LastSuccessLocal = new DateTime(2026, 10, 20, 2, 10, 0);
        Assert.False(s.IsOverdue(new DateTime(2026, 10, 20, 9, 0, 0), TimeSpan.FromHours(6)));
        Assert.False(new BackupSchedule().IsOverdue(DateTime.Now, TimeSpan.Zero)); // schedule off
    }

    private sealed class FakeBlobService : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Blocks { get; } = new();
        public string? CommittedList { get; private set; }
        public List<string> Urls { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            if (query["comp"] == "block") Blocks[query["blockid"]!] = body;
            else if (query["comp"] == "blocklist") CommittedList = System.Text.Encoding.UTF8.GetString(body);
            return new HttpResponseMessage(Status) { Content = new StringContent("<Error><Code>AuthenticationFailed</Code></Error>") };
        }
    }

    [Fact]
    public async Task A_file_is_uploaded_in_blocks_and_committed_in_order()
    {
        var data = new byte[20 * 1024 * 1024 + 123];
        new Random(7).NextBytes(data);
        var fake = new FakeBlobService();
        await AzureBlobUploader.UploadAsync("https://acct.blob.core.windows.net/backups?sv=1&sig=abc", "ITInv_Tenant_Acme_20261020_020000_auto.zip", new MemoryStream(data), fake, default);

        Assert.Equal(3, fake.Blocks.Count);
        var ids = System.Text.RegularExpressions.Regex.Matches(fake.CommittedList!, "<Latest>(.*?)</Latest>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(data, ids.SelectMany(id => fake.Blocks[id]).ToArray());
        Assert.All(fake.Urls, u => Assert.Contains("/backups/ITInv_Tenant_Acme_20261020_020000_auto.zip?sv=1&sig=abc&comp=", u));
    }

    [Fact]
    public async Task A_refusal_from_azure_is_reported_with_its_reason()
    {
        var fake = new FakeBlobService { Status = HttpStatusCode.Forbidden };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AzureBlobUploader.UploadAsync("https://acct.blob.core.windows.net/backups?sv=1&sig=abc", "x.zip", new MemoryStream(new byte[10]), fake, default));
        Assert.Contains("403", ex.Message);
        Assert.Contains("AuthenticationFailed", ex.Message);
    }

    [Theory]
    [InlineData("http://example.com/backups?sig=1")]
    [InlineData("https://acct.blob.core.windows.net/backups")]
    public async Task Unsafe_or_incomplete_blob_urls_are_refused(string url) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => AzureBlobUploader.UploadAsync(url, "x.zip", new MemoryStream(new byte[1]), new FakeBlobService(), default));
}

public class PlatformOverviewTests
{
    private static TenantRecord Tenant(bool demo = false, bool active = true) => new(1, "acme", "Acme", "Server=x;Database=ITInv_Tenant_acme", demo, active, DateTime.UtcNow);

    [Fact]
    public void A_healthy_company_has_no_warnings()
    {
        var schedule = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00", LastSuccessLocal = DateTime.Now, LastResult = "OK" };
        var warnings = PlatformOverview.Warnings(Tenant(), new TenantUsage("acme", 1, 1, 1, 0, null, null, null), new TenantSchemaStatus("acme", "Acme", 1, 0, null),
            schedule, DateTime.UtcNow.AddHours(-3), true, true, true);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Problems_are_named()
    {
        var warnings = PlatformOverview.Warnings(Tenant(active: false), new TenantUsage("acme", 0, 0, 0, 0, null, null, "boom"), new TenantSchemaStatus("acme", "Acme", 0, 2, null),
            new BackupSchedule(), null, null, null, backupsReadable: true);
        Assert.Contains("Suspended", warnings);
        Assert.Contains(warnings, w => w.StartsWith("Database unreadable", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("2 database change", StringComparison.Ordinal));
        Assert.Contains("No backup yet", warnings);
        Assert.Contains("No backup schedule", warnings);
    }

    [Fact]
    public void A_failed_check_and_a_failed_offsite_copy_are_warnings_and_the_demo_needs_no_backups()
    {
        var schedule = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00", LastSuccessLocal = DateTime.Now };
        var bad = PlatformOverview.Warnings(Tenant(), null, null, schedule, DateTime.UtcNow, false, false, true);
        Assert.Contains(bad, w => w.Contains("FAILED its test restore", StringComparison.Ordinal));
        Assert.Contains(bad, w => w.Contains("off-site", StringComparison.Ordinal));
        Assert.Empty(PlatformOverview.Warnings(Tenant(demo: true), null, null, new BackupSchedule(), null, null, null, true));
    }

    [Fact]
    public void Metrics_use_the_prometheus_text_format_and_escape_labels()
    {
        var company = new CompanyOverview(Tenant(), new TenantUsage("acme", 1000, 5, 4, 12, null, null, null), new TenantSchemaStatus("acme", "Acme", 1, 0, null), 500, "Daily", null,
            DateTime.UtcNow.AddSeconds(-90), true, null, true, new[] { "x" });
        var text = PlatformMetricsController.Render(new PlatformSnapshot(DateTime.UtcNow, new[] { company }, new[] { new DiskOverview("Backups", "/b", 10, 100) }));
        Assert.Contains("eams_companies 1\n", text);
        Assert.Contains("eams_company_database_bytes{company=\"acme\"} 1000\n", text);
        Assert.Contains("eams_company_last_backup_checked{company=\"acme\"} 1\n", text);
        Assert.Contains("eams_company_warnings{company=\"acme\"} 1\n", text);
        Assert.Contains("eams_disk_free_bytes{volume=\"Backups\"} 10\n", text);
        Assert.Equal("a\\\"b\\\\c", PlatformMetricsController.Escape("a\"b\\c"));
    }
}

public class ScheduleStatusTests
{
    [Fact]
    public void The_next_daily_run_is_today_or_tomorrow()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00", LastRunLocal = new DateTime(2026, 10, 20, 2, 0, 30) };
        Assert.Equal(new DateTime(2026, 10, 21, 2, 0, 0), s.NextRun(new DateTime(2026, 10, 20, 9, 0, 0)));
    }

    [Fact]
    public void A_due_backup_is_reported_as_now_and_the_weekly_run_waits_for_its_day()
    {
        var due = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00", LastRunLocal = new DateTime(2026, 10, 19, 2, 0, 30) };
        var now = new DateTime(2026, 10, 20, 2, 30, 0);
        Assert.Equal(now, due.NextRun(now));

        var weekly = new BackupSchedule { Mode = BackupSchedule.Weekly, Day = 5, Time = "03:00", LastRunLocal = new DateTime(2026, 10, 16, 3, 0, 30) }; // Friday
        Assert.Equal(new DateTime(2026, 10, 23, 3, 0, 0), weekly.NextRun(new DateTime(2026, 10, 20, 12, 0, 0)));
        Assert.Null(new BackupSchedule().NextRun(DateTime.Now));
    }

    [Fact]
    public void The_history_keeps_the_newest_twenty_runs_first()
    {
        var s = new BackupSchedule();
        for (var i = 0; i < 25; i++) s.AddRun(new BackupRun { StartedLocal = new DateTime(2026, 10, 1).AddDays(i), Summary = "run " + i });
        Assert.Equal(BackupSchedule.MaxHistory, s.History.Count);
        Assert.Equal("run 24", s.History[0].Summary);
        Assert.Equal("run 5", s.History[^1].Summary);
    }
}
