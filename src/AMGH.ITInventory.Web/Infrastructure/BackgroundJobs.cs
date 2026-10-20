using System.Collections.Concurrent;

namespace AMGH.ITInventory.Web.Infrastructure;

public sealed record BackgroundJobInfo(string Name, string Description, bool Running, DateTime? LastStartLocal, DateTime? LastFinishLocal, bool? LastOk, string? LastDetail, TimeSpan Every);

/// <summary>What the site's background jobs are doing, for the "Scheduled jobs" panel. In memory only: it starts empty when the site starts.</summary>
public sealed class BackgroundJobs
{
    private readonly ConcurrentDictionary<string, BackgroundJobInfo> _jobs = new(StringComparer.Ordinal);

    public void Started(string name, string description, TimeSpan every) =>
        _jobs.AddOrUpdate(name, _ => new BackgroundJobInfo(name, description, true, DateTime.Now, null, null, null, every),
            (_, old) => old with { Running = true, LastStartLocal = DateTime.Now, Description = description, Every = every });

    public void Finished(string name, bool ok, string? detail) =>
        _jobs.AddOrUpdate(name, _ => new BackgroundJobInfo(name, string.Empty, false, null, DateTime.Now, ok, detail, TimeSpan.Zero),
            (_, old) => old with { Running = false, LastFinishLocal = DateTime.Now, LastOk = ok, LastDetail = detail });

    public IReadOnlyList<BackgroundJobInfo> Snapshot() => _jobs.Values.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
}
