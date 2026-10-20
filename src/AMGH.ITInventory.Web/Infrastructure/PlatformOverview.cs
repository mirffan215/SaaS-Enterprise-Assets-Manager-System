using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Infrastructure.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>Everything the operator wants to know about one company on one screen.</summary>
public sealed record CompanyOverview(
    TenantRecord Tenant, TenantUsage? Usage, TenantSchemaStatus? Schema, long DocumentBytes, string ScheduleMode, string? ScheduleResult,
    DateTime? LastBackupUtc, bool? LastBackupChecked, bool? LastOffsiteOk, bool BackupsReadable, IReadOnlyList<string> Warnings);

public sealed record DiskOverview(string Label, string Path, long FreeBytes, long TotalBytes)
{
    public double FreePercent => TotalBytes <= 0 ? 100 : FreeBytes * 100.0 / TotalBytes;
    public bool Low => FreePercent < 15 || FreeBytes < 5L * 1024 * 1024 * 1024;
}

public sealed record PlatformSnapshot(DateTime AtUtc, IReadOnlyList<CompanyOverview> Companies, IReadOnlyList<DiskOverview> Disks);

/// <summary>Collects usage, schema version, backup state, document storage and disk space; cached for 30 seconds so a refresh or a monitoring poll is cheap.</summary>
public sealed class PlatformOverview
{
    private const string CacheKey = "platform:overview";
    private readonly ITenantCatalog _catalog;
    private readonly ITenantUsageService _usage;
    private readonly ITenantMigrationService _migrations;
    private readonly ITenantBackupService _backups;
    private readonly BackupScheduleStore _schedules;
    private readonly IMemoryCache _cache;
    private readonly TenancyOptions _options;
    private readonly StorageOptions _storage;

    public PlatformOverview(ITenantCatalog catalog, ITenantUsageService usage, ITenantMigrationService migrations, ITenantBackupService backups,
        BackupScheduleStore schedules, IMemoryCache cache, IOptions<TenancyOptions> options, IOptions<StorageOptions> storage)
    {
        _catalog = catalog;
        _usage = usage;
        _migrations = migrations;
        _backups = backups;
        _schedules = schedules;
        _cache = cache;
        _options = options.Value;
        _storage = storage.Value;
    }

    public static void Invalidate(IMemoryCache cache) => cache.Remove(CacheKey);

    public async Task<PlatformSnapshot> GetAsync(bool fresh, CancellationToken ct)
    {
        if (!fresh && _cache.TryGetValue(CacheKey, out PlatformSnapshot? cached) && cached is not null) return cached;
        var tenants = await _catalog.ListAsync(ct);
        var usage = (await _usage.GetAsync(ct)).ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);
        var schema = (await _migrations.StatusAsync(ct)).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<CompanyOverview>();
        foreach (var t in tenants)
        {
            usage.TryGetValue(t.Code, out var u);
            schema.TryGetValue(t.Code, out var s);
            var schedule = await _schedules.LoadAsync(t.Code, ct);
            DateTime? lastBackup = null; bool? lastChecked = null, lastOffsite = null; var readable = true;
            try
            {
                var latest = (await _backups.ListAsync(t.Code, ct)).FirstOrDefault();
                if (latest is not null)
                {
                    lastBackup = latest.CreatedUtc;
                    lastChecked = latest.VerifiedUtc is null ? null : latest.VerifyOk;
                    lastOffsite = latest.OffsiteUtc is null ? null : latest.OffsiteOk;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { readable = false; }
            var docs = DocumentBytes(t.Code);
            rows.Add(new CompanyOverview(t, u, s, docs, schedule.Mode, schedule.LastResult, lastBackup, lastChecked, lastOffsite, readable,
                Warnings(t, u, s, schedule, lastBackup, lastChecked, lastOffsite, readable)));
        }
        var snapshot = new PlatformSnapshot(DateTime.UtcNow, rows, Disks());
        _cache.Set(CacheKey, snapshot, TimeSpan.FromSeconds(30));
        return snapshot;
    }

    internal static List<string> Warnings(TenantRecord t, TenantUsage? usage, TenantSchemaStatus? schema, BackupSchedule schedule, DateTime? lastBackupUtc,
        bool? lastChecked, bool? lastOffsite, bool backupsReadable)
    {
        var w = new List<string>();
        if (!t.IsActive) w.Add("Suspended");
        if (usage?.Problem is { } p) w.Add("Database unreadable: " + p);
        if (schema is { UpToDate: false }) w.Add(schema.Problem ?? schema.Pending + " database change(s) pending");
        if (t.IsDemo) return w;
        if (!backupsReadable) w.Add("Backup folder cannot be read");
        else if (lastBackupUtc is null) w.Add("No backup yet");
        else if (DateTime.UtcNow - lastBackupUtc > TimeSpan.FromHours(48)) w.Add("Last backup is older than 2 days");
        if (lastChecked == false) w.Add("Latest backup FAILED its test restore");
        if (lastOffsite == false) w.Add("Latest off-site copy failed");
        if (schedule.Mode == BackupSchedule.Off) w.Add("No backup schedule");
        else if (schedule.LastResult?.Contains("FAILED", StringComparison.OrdinalIgnoreCase) == true) w.Add("Last scheduled backup failed");
        else if (schedule.IsOverdue(DateTime.Now, BackupHealthCheck.Grace)) w.Add("Scheduled backup overdue");
        return w;
    }

    private long DocumentBytes(string code)
    {
        try
        {
            var folder = Path.Combine(Path.GetFullPath(_storage.RootPath), "t-" + code.ToLowerInvariant());
            if (!Directory.Exists(folder)) return 0;
            return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private List<DiskOverview> Disks()
    {
        var list = new List<DiskOverview>();
        void Add(string label, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                if (root is null || list.Any(d => string.Equals(Path.GetPathRoot(d.Path), root, StringComparison.OrdinalIgnoreCase) && d.Label == label)) return;
                var drive = new DriveInfo(root);
                list.Add(new DiskOverview(label, full, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { /* unreadable (for example a network share): not shown */ }
        }
        Add("Documents", _storage.RootPath);
        Add("Backups", _options.BackupFolder);
        Add("Off-site folder", _options.OffsiteFolder);
        return list;
    }
}
