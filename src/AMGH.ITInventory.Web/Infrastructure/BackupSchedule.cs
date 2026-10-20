using System.Globalization;
using System.Text.Json;
using AMGH.ITInventory.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>One run of the backup job (scheduled or by hand), kept for the history on the Backup and restore page.</summary>
public sealed class BackupRun
{
    public DateTime StartedLocal { get; set; }
    public DateTime? FinishedLocal { get; set; }
    /// <summary>"Scheduled" or "Manual".</summary>
    public string Kind { get; set; } = "Scheduled";
    public bool Ok { get; set; }
    public string Summary { get; set; } = string.Empty;
}

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
    /// <summary>When a scheduled backup last finished and passed its test restore.</summary>
    public DateTime? LastSuccessLocal { get; set; }
    public string? LastResult { get; set; }
    /// <summary>Set while a scheduled backup is being made (cleared when it ends, or at start-up if the site restarted in the middle of it).</summary>
    public DateTime? RunningSinceLocal { get; set; }
    /// <summary>The newest runs first, at most <see cref="MaxHistory"/>.</summary>
    public List<BackupRun> History { get; set; } = new();

    public const int MaxHistory = 20;

    public void AddRun(BackupRun run)
    {
        History.Insert(0, run);
        if (History.Count > MaxHistory) History.RemoveRange(MaxHistory, History.Count - MaxHistory);
    }

    /// <summary>The next moment a backup is due: now when one is waiting to run, null when the schedule is off.</summary>
    public DateTime? NextRun(DateTime now)
    {
        if (Mode == Off || !TryParseTime(Time, out var time)) return null;
        if (IsDue(now)) return now;
        if (Mode == Daily) return now.Date + time > now ? now.Date + time : now.Date.AddDays(1) + time;
        if (Mode != Weekly || Day is < 0 or > 6) return null;
        var ahead = (Day - (int)now.DayOfWeek + 7) % 7;
        var slot = now.Date.AddDays(ahead) + time;
        return slot > now ? slot : slot.AddDays(7);
    }

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

    /// <summary>True when a backup was due more than <paramref name="grace"/> ago and no successful (checked) backup has been made since.</summary>
    public bool IsOverdue(DateTime now, TimeSpan grace) =>
        LastDueSlot(now) is { } slot && now - slot > grace && (LastSuccessLocal is null || LastSuccessLocal.Value < slot);

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
