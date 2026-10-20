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
