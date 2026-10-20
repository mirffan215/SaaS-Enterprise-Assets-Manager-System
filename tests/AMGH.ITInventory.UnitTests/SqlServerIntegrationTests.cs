using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Infrastructure.Archives;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

/// <summary>
/// Runs the real deployment scripts, provisioning and backup/restore against a SQL Server.
/// Set EAMS_TEST_SQL to a connection string whose login may create databases (for example the docker image
/// mcr.microsoft.com/mssql/server:2022-latest) and EAMS_TEST_BACKUPS to a folder both the test and that server can read and write.
/// Without them these tests are skipped.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EAMS_TEST_SQL"))) Skip = "Set EAMS_TEST_SQL to run the SQL Server tests.";
    }
}

public class SqlServerIntegrationTests
{
    private sealed class MemoryCatalog : ITenantCatalog
    {
        public List<TenantRecord> Rows { get; } = new();
        public Task<TenantInfo?> FindActiveAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))?.ToInfo());
        public Task<bool> ExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(Rows.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)));
        public Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TenantRecord>>(Rows);
        public Task<TenantRecord> AddAsync(string code, string name, string cs, bool demo, CancellationToken ct = default)
        {
            var row = new TenantRecord(Rows.Count + 1, code, name, cs, demo, true, DateTime.UtcNow);
            Rows.Add(row);
            return Task.FromResult(row);
        }
    }

    private sealed class FixedHasher : ITenantAdminPasswordHasher
    {
        public Result<string> HashNew(string username, string email, string password) => Result<string>.Success("PBKDF2$test$hash");
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "deployment"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repository root"), relative);
    }

    private static TenancyOptions Options()
    {
        var scripts = Path.Combine(Path.GetTempPath(), "eams-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scripts, "Demo"));
        foreach (var f in Directory.GetFiles(RepoFile("deployment/SQLScripts"), "*.sql")) File.Copy(f, Path.Combine(scripts, Path.GetFileName(f)));
        File.Copy(RepoFile("deployment/LoadTest/Generate-DemoData.sql"), Path.Combine(scripts, "Demo", "Generate-DemoData.sql"));
        return new TenancyOptions
        {
            Enabled = true,
            ServerConnectionString = Environment.GetEnvironmentVariable("EAMS_TEST_SQL"),
            ScriptsPath = scripts,
            BackupFolder = Environment.GetEnvironmentVariable("EAMS_TEST_BACKUPS") ?? Path.GetTempPath()
        };
    }

    private static string NewCode() => "T" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> ScalarAsync<T>(string database, string sql, TenancyOptions o)
    {
        var cs = new SqlConnectionStringBuilder(o.ServerConnectionString) { InitialCatalog = database }.ConnectionString;
        await using var c = new SqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("no row"), typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task DropAsync(TenancyOptions o, string code)
    {
        await using var c = new SqlConnection(new SqlConnectionStringBuilder(o.ServerConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        var db = TenantProvisioningService.DatabasePrefix + code;
        cmd.CommandText = $"IF DB_ID('{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private static TenantProvisioningService Provisioner(TenancyOptions o, MemoryCatalog catalog) =>
        new(catalog, new FixedHasher(), Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantProvisioningService>.Instance);

    [SqlServerFact]
    public async Task Provisioning_builds_a_complete_company_database()
    {
        var o = Options();
        var code = NewCode();
        var catalog = new MemoryCatalog();
        try
        {
            var result = await Provisioner(o, catalog).ProvisionTenantAsync(code, "Acme Test", "admin@acme.test", "Correct-Horse-9", includeErrorDetail: true);
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
            var db = TenantProvisioningService.DatabasePrefix + code;
            Assert.True(await ScalarAsync<int>(db, "SELECT COUNT(*) FROM sys.tables", o) > 30);
            Assert.Equal("Acme Test", await ScalarAsync<string>(db, "SELECT DisplayName FROM dbo.tblCompany WHERE CompanyId = 1", o));
            Assert.Equal(1, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.tblUser WHERE Username = 'admin'", o));
            Assert.Single(catalog.Rows);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task The_demo_company_loads_its_sample_estate()
    {
        var o = Options();
        var code = "demo" + Guid.NewGuid().ToString("N")[..4]; // the generator refuses databases without "demo" in the name
        try
        {
            var result = await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Demo Test", "demo@acme.test", "Correct-Horse-9", isDemo: true, includeErrorDetail: true);
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(e => e.Message)));
            Assert.True(await ScalarAsync<int>(TenantProvisioningService.DatabasePrefix + code, "SELECT COUNT(*) FROM dbo.tblAsset", o) > 10);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task A_failed_run_leaves_no_database_behind()
    {
        var o = Options();
        File.WriteAllText(Path.Combine(o.ScriptsPath, "19_CompanyDataExport.sql"), "RAISERROR('boom', 16, 1);");
        var code = NewCode();
        var result = await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Broken", "a@b.test", "Correct-Horse-9");
        Assert.True(result.IsFailure);
        await using var c = new SqlConnection(new SqlConnectionStringBuilder(o.ServerConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DB_ID('" + TenantProvisioningService.DatabasePrefix + code + "')";
        Assert.True(await cmd.ExecuteScalarAsync() is DBNull or null);
    }

    [SqlServerFact]
    public async Task Backup_restore_and_zip_round_trip()
    {
        var o = Options();
        var code = NewCode();
        var db = TenantProvisioningService.DatabasePrefix + code;
        try
        {
            Assert.True((await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Backup Test", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
            var backups = new TenantBackupService(Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantBackupService>.Instance, new AesZipFile());

            var made = await backups.BackupAsync(code);
            Assert.True(made.IsSuccess, string.Join("; ", made.Errors.Select(e => e.Message)));

            await ScalarAsync<int>(db, "UPDATE dbo.tblCompany SET DisplayName = N'Changed' WHERE CompanyId = 1; SELECT 1", o);
            var restored = await backups.RestoreAsync(code, made.Value.FileName);
            Assert.True(restored.IsSuccess, string.Join("; ", restored.Errors.Select(e => e.Message)));
            Assert.Equal("Backup Test", await ScalarAsync<string>(db, "SELECT DisplayName FROM dbo.tblCompany WHERE CompanyId = 1", o));

            // download as zip, change again, restore from the uploaded zip
            await using var source = await backups.OpenAsync(code, made.Value.FileName) ?? throw new InvalidOperationException("no file");
            using var zip = new MemoryStream();
            await new AesZipFile().WriteAsync(zip, "Correct-Horse-9", made.Value.FileName, source);
            await ScalarAsync<int>(db, "UPDATE dbo.tblCompany SET DisplayName = N'Changed again' WHERE CompanyId = 1; SELECT 1", o);
            zip.Position = 0;
            var fromZip = await backups.RestoreFromZipAsync(code, zip, "Correct-Horse-9");
            Assert.True(fromZip.IsSuccess, string.Join("; ", fromZip.Errors.Select(e => e.Message)));
            Assert.Equal("Backup Test", await ScalarAsync<string>(db, "SELECT DisplayName FROM dbo.tblCompany WHERE CompanyId = 1", o));

            // another company's backup is refused
            var other = NewCode();
            zip.Position = 0;
            Assert.True((await backups.RestoreFromZipAsync(other, zip, "Correct-Horse-9")).IsFailure);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task A_backup_passes_its_test_restore_and_a_damaged_one_does_not()
    {
        var o = Options();
        var code = NewCode();
        try
        {
            Assert.True((await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Verify Test", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
            var backups = new TenantBackupService(Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantBackupService>.Instance, new AesZipFile());
            var made = await backups.BackupAsync(code);
            Assert.True(made.IsSuccess);

            var verified = await backups.VerifyAsync(code, made.Value.FileName);
            Assert.True(verified.IsSuccess, string.Join("; ", verified.Errors.Select(e => e.Message)));
            Assert.True(verified.Value.VerifyOk, verified.Value.VerifyNote);
            Assert.Contains((await backups.ListAsync(code)), f => f.FileName == made.Value.FileName && f.VerifyOk);

            // the temporary database is gone again
            await using var c = new SqlConnection(new SqlConnectionStringBuilder(o.ServerConnectionString) { InitialCatalog = "master" }.ConnectionString);
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'ITInv_Verify_%'";
            Assert.Equal(0, Convert.ToInt32(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));

            // damage a copy of the file: it must be reported as failed
            var broken = made.Value.FileName.Replace(".bak", "x.bak", StringComparison.Ordinal);
            broken = TenantProvisioningService.DatabasePrefix + code + "_20000101_000000.bak";
            var bytes = await File.ReadAllBytesAsync(Path.Combine(o.BackupFolder!, made.Value.FileName));
            for (var i = bytes.Length / 2; i < bytes.Length / 2 + 4096 && i < bytes.Length; i++) bytes[i] ^= 0xFF;
            await File.WriteAllBytesAsync(Path.Combine(o.BackupFolder!, broken), bytes);
            var bad = await backups.VerifyAsync(code, broken);
            Assert.True(bad.IsSuccess);
            Assert.False(bad.Value.VerifyOk);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task Migrations_upgrade_old_and_new_companies_alike_and_report_edited_scripts()
    {
        var o = Options();
        var migrations = Path.Combine(o.ScriptsPath, "Migrations");
        Directory.CreateDirectory(migrations);
        const string first = "IF COL_LENGTH('dbo.tblCompany', 'TestFlag') IS NULL ALTER TABLE dbo.tblCompany ADD TestFlag INT NULL;";
        var code = NewCode();
        var catalog = new MemoryCatalog();
        try
        {
            // a company created before any migration existed
            Assert.True((await Provisioner(o, catalog).ProvisionTenantAsync(code, "Old Company", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
            var db = TenantProvisioningService.DatabasePrefix + code;
            Assert.Equal(0, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM sys.columns WHERE name = 'TestFlag'", o));

            File.WriteAllText(Path.Combine(migrations, "0001_AddTestFlag.sql"), first);
            var backups = new TenantBackupService(Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantBackupService>.Instance, new AesZipFile());
            var service = new TenantMigrationService(catalog, backups, Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantMigrationService>.Instance);

            var before = (await service.StatusAsync()).Single();
            Assert.Equal(1, before.Pending);

            var upgraded = await service.UpgradeAsync(code);
            Assert.True(upgraded.IsSuccess, string.Join("; ", upgraded.Errors.Select(e => e.Message)));
            Assert.Equal(1, upgraded.Value);
            Assert.Equal(1, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM sys.columns WHERE name = 'TestFlag'", o));
            Assert.True((await service.StatusAsync()).Single().UpToDate);
            Assert.Equal(0, (await service.UpgradeAsync(code)).Value); // nothing to do the second time
            Assert.NotEmpty(await backups.ListAsync(code));            // a safety backup was made first

            // a company created now gets baseline and migrations
            var fresh = NewCode();
            try
            {
                Assert.True((await Provisioner(o, catalog).ProvisionTenantAsync(fresh, "New Company", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
                Assert.Equal(1, await ScalarAsync<int>(TenantProvisioningService.DatabasePrefix + fresh, "SELECT COUNT(*) FROM sys.columns WHERE name = 'TestFlag'", o));
            }
            finally { await DropAsync(o, fresh); }

            // a script edited after it was applied is reported, not silently skipped
            File.WriteAllText(Path.Combine(migrations, "0001_AddTestFlag.sql"), first + "\n-- edited");
            var drift = (await service.StatusAsync()).First(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(drift.Problem);
            Assert.True((await service.UpgradeAsync(code)).IsFailure);
        }
        finally { await DropAsync(o, code); }
    }

    private sealed class RecordingBlobService : HttpMessageHandler
    {
        public SortedDictionary<string, byte[]> Blocks { get; } = new(StringComparer.Ordinal);
        public string? Name { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (q["comp"] == "block") { Blocks[System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(q["blockid"]!))] = await request.Content!.ReadAsByteArrayAsync(ct); Name = Path.GetFileName(request.RequestUri.AbsolutePath); }
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created);
        }
    }

    [SqlServerFact]
    public async Task An_offsite_copy_is_an_encrypted_zip_that_restores_to_the_same_backup()
    {
        var o = Options();
        o.OffsiteFolder = Path.Combine(Path.GetTempPath(), "eams-offsite-" + Guid.NewGuid().ToString("N"));
        o.OffsitePassword = "Offsite-Password-9";
        o.OffsiteBlobUrl = "https://acct.blob.core.windows.net/eams?sv=1&sig=abc";
        var blob = new RecordingBlobService();
        var code = NewCode();
        try
        {
            Assert.True((await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Offsite Test", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
            var backups = new TenantBackupService(Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantBackupService>.Instance, new AesZipFile(), blob);
            Assert.True(backups.OffsiteConfigured);
            var made = await backups.BackupAsync(code, scheduled: true);
            Assert.True(made.IsSuccess);

            var copy = await backups.CopyOffsiteAsync(code, made.Value.FileName, keepScheduled: 7);
            Assert.True(copy.IsSuccess && copy.Value.OffsiteOk, copy.IsSuccess ? copy.Value.OffsiteNote : copy.FirstError!.Message);
            Assert.Contains("folder and Azure", copy.Value.OffsiteNote);
            Assert.Contains(await backups.ListAsync(code), f => f.FileName == made.Value.FileName && f.OffsiteOk);

            var original = await File.ReadAllBytesAsync(Path.Combine(o.BackupFolder!, made.Value.FileName));
            foreach (var zipBytes in new[] { await File.ReadAllBytesAsync(Path.Combine(o.OffsiteFolder, Path.ChangeExtension(made.Value.FileName, ".zip"))), blob.Blocks.Values.SelectMany(b => b).ToArray() })
            {
                using var restored = new MemoryStream();
                var read = await new AesZipFile().ReadAsync(new MemoryStream(zipBytes), "Offsite-Password-9", restored, long.MaxValue);
                Assert.True(read.IsSuccess);
                Assert.Equal(original, restored.ToArray());
                Assert.True((await new AesZipFile().ReadAsync(new MemoryStream(zipBytes), "wrong-password-1", new MemoryStream(), long.MaxValue)).IsFailure);
            }
            Assert.EndsWith(".zip", blob.Name);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task An_unreachable_offsite_target_is_a_recorded_failure_not_an_exception()
    {
        var o = Options();
        o.OffsiteFolder = Path.Combine(Path.GetTempPath(), "eams-offsite-" + Guid.NewGuid().ToString("N"), "\0bad");
        o.OffsitePassword = "Offsite-Password-9";
        var code = NewCode();
        try
        {
            Assert.True((await Provisioner(o, new MemoryCatalog()).ProvisionTenantAsync(code, "Offsite Fail", "a@b.test", "Correct-Horse-9")).IsSuccess);
            var backups = new TenantBackupService(Microsoft.Extensions.Options.Options.Create(o), NullLogger<TenantBackupService>.Instance, new AesZipFile());
            var made = await backups.BackupAsync(code);
            var copy = await backups.CopyOffsiteAsync(code, made.Value.FileName);
            Assert.True(copy.IsSuccess);
            Assert.False(copy.Value.OffsiteOk);
        }
        finally { await DropAsync(o, code); }
    }

    [SqlServerFact]
    public async Task Usage_reports_size_users_and_assets_of_each_company()
    {
        var o = Options();
        var code = NewCode();
        var catalog = new MemoryCatalog();
        try
        {
            Assert.True((await Provisioner(o, catalog).ProvisionTenantAsync(code, "Usage Test", "a@b.test", "Correct-Horse-9", includeErrorDetail: true)).IsSuccess);
            var usage = (await new TenantUsageService(catalog, Microsoft.Extensions.Options.Options.Create(o)).GetAsync()).Single();
            Assert.Null(usage.Problem);
            Assert.True(usage.DatabaseBytes > 1_000_000);
            Assert.Equal(1, usage.Users);
            Assert.Equal(0, usage.Assets);
        }
        finally { await DropAsync(o, code); }
    }
}
