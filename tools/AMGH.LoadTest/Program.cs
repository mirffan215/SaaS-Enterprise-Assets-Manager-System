// AMGH IT Inventory - load test (Release 2.6)
//
// Signs in once, then N virtual users browse the main pages with a weighted mix and think time between clicks.
// Reports response-time percentiles and errors per page, and PASS/FAIL against the thresholds below.
// Run it against a TEST site that uses a COPY of the database (see docs/Release2.6-Hardening.md).
//
//   dotnet run -c Release -- --url https://inventory-loadtest:8443 --company AMGH --user loadtest --users 25 --minutes 10
//   (password: --password or environment variable AMGH_LOADTEST_PASSWORD)
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

var opt = Options.Parse(args);
if (opt is null) return 2;

var cookies = new CookieContainer();
var handler = new SocketsHttpHandler
{
    CookieContainer = cookies, AllowAutoRedirect = false, MaxConnectionsPerServer = Math.Max(opt.Users * 2, 20),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
};
if (opt.Insecure) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true; // test certificates only
using var http = new HttpClient(handler) { BaseAddress = new Uri(opt.Url), Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("AMGH-LoadTest/2.6");

Console.WriteLine($"AMGH IT Inventory load test: {opt.Url} · {opt.Users} users · {opt.Minutes} min (+{opt.WarmupSeconds}s warm-up) · think {opt.ThinkMs} ms");

// ---------------------------------------------------------------- sign in (once: the sign-in page is rate limited)
var loginPage = await http.GetStringAsync($"/Account/Login?company={Uri.EscapeDataString(opt.Company)}");
var token = Regex.Match(loginPage, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["Username"] = opt.User, ["Password"] = opt.Password, ["CompanyCode"] = opt.Company, ["__RequestVerificationToken"] = token
}))
{
    var r = await http.PostAsync("/Account/Login", form);
    var to = r.Headers.Location?.ToString() ?? string.Empty;
    if (r.StatusCode != HttpStatusCode.Redirect || to.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Sign-in failed ({(int)r.StatusCode} {to}). Check the user name, password and company code.");
        return 2;
    }
}
Console.WriteLine("Signed in.");

// ---------------------------------------------------------------- discover test data from the site itself
var listHtml = await http.GetStringAsync("/Assets?PageSize=100");
var assetIds = Regex.Matches(listHtml, "/Assets/Details/(\\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToArray();
var totalMatch = Regex.Match(listHtml, "([\\d,]+) asset\\(s\\)");
var total = totalMatch.Success ? int.Parse(totalMatch.Groups[1].Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture) : assetIds.Length;
var words = Regex.Matches(listHtml, "<td>([A-Za-z][A-Za-z0-9 ]{3,40})", RegexOptions.None).Select(m => m.Groups[1].Value.Split(' ')[0]).Where(w => w.Length >= 3).Distinct().Take(40).ToArray();
if (assetIds.Length == 0) { Console.Error.WriteLine("No assets visible for this user: nothing to test."); return 2; }
if (words.Length == 0) words = new[] { "Dell", "HP", "Lenovo" };
var pages = Math.Max(1, total / 50);
Console.WriteLine($"Found {total:N0} assets ({pages:N0} pages of 50); {assetIds.Length} sample ids, {words.Length} search words.");

// ---------------------------------------------------------------- scenarios (name, weight, threshold p95 ms, url factory)
var rnd = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
string Pick(string[] a) => a[rnd.Value!.Next(a.Length)];
var sorts = new[] { "number", "name", "category", "status", "assignedTo", "location", "purchaseDate" };
var scenarios = new Scenario[]
{
    new("Dashboard", 10, 3000, () => "/dashboard"),
    new("Asset list", 20, 1500, () => $"/Assets?Page={rnd.Value!.Next(1, Math.Min(pages, 200) + 1)}&Sort={Pick(sorts)}&Descending={(rnd.Value!.Next(2) == 1).ToString().ToLowerInvariant()}"),
    new("Asset list: search", 8, 1500, () => $"/Assets?Search={Uri.EscapeDataString(Pick(words))}"),
    new("Asset details", 20, 1500, () => $"/Assets/Details/{assetIds[rnd.Value!.Next(assetIds.Length)]}"),
    new("Search suggestions", 12, 800, () => $"/search/suggest?q={Uri.EscapeDataString(Pick(words)[..3])}"),
    new("Global search", 4, 1500, () => $"/search?q={Uri.EscapeDataString(Pick(words))}"),
    new("Report: asset register", 4, 5000, () => "/reports/asset-register"),
    new("Report: compliance", 2, 8000, () => "/reports/compliance"),
    new("Approval Center", 4, 1500, () => "/approvals"),
    new("Notifications", 4, 1000, () => "/notifications"),
    new("Maintenance", 4, 1500, () => "/maintenance"),
    new("Verification", 4, 2000, () => "/verification"),
    new("Health", 4, 1000, () => "/health")
};
var totalWeight = scenarios.Sum(s => s.Weight);
Scenario Choose()
{
    var x = rnd.Value!.Next(totalWeight);
    foreach (var s in scenarios) { if (x < s.Weight) return s; x -= s.Weight; }
    return scenarios[0];
}

// ---------------------------------------------------------------- run
var samples = new ConcurrentDictionary<string, ConcurrentBag<double>>();
var errors = new ConcurrentDictionary<string, int>();
var errorExamples = new ConcurrentDictionary<string, string>();
var started = Stopwatch.StartNew();
var warmup = TimeSpan.FromSeconds(opt.WarmupSeconds);
var end = warmup + TimeSpan.FromMinutes(opt.Minutes);
using var stop = new CancellationTokenSource(end);
var progress = Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stop.Token); } catch (OperationCanceledException) { break; }
        var n = samples.Values.Sum(b => b.Count);
        Console.WriteLine($"  {started.Elapsed:mm\\:ss}  {(started.Elapsed < warmup ? "warm-up" : $"{n:N0} requests measured, {errors.Values.Sum()} errors")}");
    }
});
var users = Enumerable.Range(0, opt.Users).Select(async u =>
{
    await Task.Delay(rnd.Value!.Next(0, Math.Max(1, opt.ThinkMs * 2))); // staggered start
    while (!stop.IsCancellationRequested)
    {
        var s = Choose();
        var url = s.Url();
        var sw = Stopwatch.StartNew();
        string? failure = null;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, stop.Token);
            sw.Stop();
            var code = (int)response.StatusCode;
            if (code >= 400 || (code is 301 or 302 && response.Headers.Location?.ToString().Contains("/Account/Login", StringComparison.OrdinalIgnoreCase) == true))
                failure = $"{code} {url}";
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception ex) { sw.Stop(); failure = $"{ex.GetType().Name}: {ex.Message} ({url})"; }
        if (started.Elapsed >= warmup)
        {
            samples.GetOrAdd(s.Name, _ => new ConcurrentBag<double>()).Add(sw.Elapsed.TotalMilliseconds);
            if (failure is not null) { errors.AddOrUpdate(s.Name, 1, (_, v) => v + 1); errorExamples.TryAdd(s.Name, failure); }
        }
        var think = opt.ThinkMs <= 0 ? 0 : (int)(opt.ThinkMs * (0.5 + rnd.Value!.NextDouble()));
        try { await Task.Delay(think, stop.Token); } catch (OperationCanceledException) { break; }
    }
}).ToArray();
await Task.WhenAll(users);
await progress;

// ---------------------------------------------------------------- report
double P(List<double> v, double p) => v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)Math.Ceiling(p / 100.0 * v.Count) - 1)];
var measured = started.Elapsed - warmup;
var all = samples.Values.Sum(b => b.Count);
var errTotal = errors.Values.Sum();
var report = new StringBuilder();
report.AppendLine(CultureInfo.InvariantCulture, $"Page;Requests;Errors;p50 ms;p90 ms;p95 ms;p99 ms;max ms;p95 limit ms;Result");
Console.WriteLine();
Console.WriteLine($"{"Page",-24}{"Requests",9}{"Errors",8}{"p50",8}{"p90",8}{"p95",8}{"p99",8}{"max",8}{"limit",8}  Result");
var failed = false;
foreach (var s in scenarios)
{
    var v = samples.TryGetValue(s.Name, out var bag) ? bag.OrderBy(x => x).ToList() : new List<double>();
    var e = errors.GetValueOrDefault(s.Name);
    var p95 = P(v, 95);
    var ok = v.Count > 0 && p95 <= s.P95Limit && e <= Math.Max(0, v.Count / 100);
    failed |= !ok;
    var line = $"{s.Name,-24}{v.Count,9}{e,8}{P(v, 50),8:0}{P(v, 90),8:0}{p95,8:0}{P(v, 99),8:0}{(v.Count == 0 ? 0 : v[^1]),8:0}{s.P95Limit,8}  {(v.Count == 0 ? "NO DATA" : ok ? "PASS" : "FAIL")}";
    Console.WriteLine(line);
    report.AppendLine(CultureInfo.InvariantCulture, $"{s.Name};{v.Count};{e};{P(v, 50):0};{P(v, 90):0};{p95:0};{P(v, 99):0};{(v.Count == 0 ? 0 : v[^1]):0};{s.P95Limit};{(ok ? "PASS" : "FAIL")}");
}
var rate = all / Math.Max(1, measured.TotalSeconds);
var errorRate = all == 0 ? 0 : 100.0 * errTotal / all;
failed |= errorRate > 1;
Console.WriteLine();
Console.WriteLine($"Total: {all:N0} requests in {measured.TotalMinutes:0.0} min = {rate:0.0} requests/s · errors {errTotal} ({errorRate:0.00}%, limit 1%)");
foreach (var kv in errorExamples) Console.WriteLine($"  first error on {kv.Key}: {kv.Value}");
Console.WriteLine(failed ? "RESULT: FAIL - see the pages marked FAIL (and docs/Release2.6-Hardening.md, 'If the load test fails')." : "RESULT: PASS");
if (opt.Output is { } path)
{
    await File.WriteAllTextAsync(path, report.ToString());
    Console.WriteLine($"Summary written to {path}");
}
return failed ? 1 : 0;

internal sealed record Scenario(string Name, int Weight, int P95Limit, Func<string> Url);

internal sealed class Options
{
    public string Url { get; private set; } = string.Empty;
    public string Company { get; private set; } = "AMGH";
    public string User { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;
    public int Users { get; private set; } = 25;
    public double Minutes { get; private set; } = 10;
    public int ThinkMs { get; private set; } = 1000;
    public int WarmupSeconds { get; private set; } = 30;
    public bool Insecure { get; private set; }
    public string? Output { get; private set; }

    public static Options? Parse(string[] args)
    {
        var o = new Options { Password = Environment.GetEnvironmentVariable("AMGH_LOADTEST_PASSWORD") ?? string.Empty };
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--url": o.Url = Next().TrimEnd('/'); break;
                case "--company": o.Company = Next(); break;
                case "--user": o.User = Next(); break;
                case "--password": o.Password = Next(); break;
                case "--users": o.Users = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--minutes": o.Minutes = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--think": o.ThinkMs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--warmup": o.WarmupSeconds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--insecure": o.Insecure = true; break;
                case "--out": o.Output = Next(); break;
                default: Console.Error.WriteLine($"Unknown option {args[i]}"); return null;
            }
        }
        if (o.Url.Length == 0 || o.User.Length == 0 || o.Password.Length == 0)
        {
            Console.Error.WriteLine("Usage: --url <https://test-site> --user <name> [--password <pw> | env AMGH_LOADTEST_PASSWORD] [--company AMGH] [--users 25] [--minutes 10] [--think 1000] [--warmup 30] [--insecure] [--out summary.csv]");
            return null;
        }
        if (o.Users is < 1 or > 500) { Console.Error.WriteLine("--users: 1-500"); return null; }
        return o;
    }
}
