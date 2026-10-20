using System.Globalization;
using System.Text.Json;
using AMGH.ITInventory.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>A workspace's backup schedule: off, every day at a time, or every week on a day at a time (server time).</summary>
public sealed class BackupSchedule
{
    public const string Off = "Off", Daily = "Daily", Weekly = "Weekly";

    public string Mode { get; set; } = Off;
    /// <summary>HH:mm, server time.</summary>
    public string Time { get; set; } = "02:00";
    /// <summary>0 = Sunday ... 6 = Saturday (weekly only).</summary>
    public int Day { get; set; } = 0;
    /// <summary>How many scheduled backups are kept (older ones are deleted; manual backups never are).</summary>
    public int Keep { get; set; } = 14;
    public DateTime? LastRunLocal { get; set; }
    public string? LastResult { get; set; }

    public static bool TryParseTime(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromHours(24);

    /// <summary>The most recent moment (not after <paramref name="now"/>) at which a backup was due; null when the schedule is off or malformed.</summary>
    public DateTime? LastDueSlot(DateTime now)
    {
        if (Mode == Off || !TryParseTime(Time, out var time)) return null;
        var slot = now.Date + time;
        if (Mode == Daily) return slot <= now ? slot : slot.AddDays(-1);
        if (Mode != Weekly || Day is < 0 or > 6) return null;
        var back = ((int)now.DayOfWeek - Day + 7) % 7;
        slot = now.Date.AddDays(-back) + time;
        return slot <= now ? slot : slot.AddDays(-7);
    }

    /// <summary>True when a backup that was due has not been made yet (a run missed while the server was down is made as soon as it is up).</summary>
    public bool IsDue(DateTime now) => LastDueSlot(now) is { } slot && (LastRunLocal is null || LastRunLocal.Value < slot);
}

/// <summary>Keeps each workspace's schedule in a small file next to its documents (outside the database, so a restore never rewinds it).</summary>
public sealed class BackupScheduleStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;

    public BackupScheduleStore(IOptions<StorageOptions> storage) =>
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage.Value.RootPath));

    private string PathFor(string tenantCode)
    {
        if (tenantCode.Length is < 2 or > 10 || !tenantCode.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("Invalid workspace code.", nameof(tenantCode));
        return Path.Combine(_root, "t-" + tenantCode.ToLowerInvariant(), "backup-schedule.json");
    }

    public async Task<BackupSchedule> LoadAsync(string tenantCode, CancellationToken ct = default)
    {
        var path = PathFor(tenantCode);
        if (!File.Exists(path)) return new BackupSchedule();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BackupSchedule>(stream, cancellationToken: ct) ?? new BackupSchedule();
        }
        catch (JsonException) { return new BackupSchedule(); }
    }

    public async Task SaveAsync(string tenantCode, BackupSchedule schedule, CancellationToken ct = default)
    {
        var path = PathFor(tenantCode);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(schedule, Json), ct);
        File.Move(temp, path, overwrite: true);
    }
}
