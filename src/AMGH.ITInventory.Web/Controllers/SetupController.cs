using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Web.Controllers;

public sealed class SetupViewModel
{
    [StringLength(200)] [DataType(DataType.Password)] public string? SetupKey { get; set; }
    [StringLength(10)] public string? Code { get; set; }
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(256)] public string? AdminEmail { get; set; }
    [StringLength(128)] [DataType(DataType.Password)] public string? AdminPassword { get; set; }
    public string? Message { get; set; }
    public string? DatabaseProblem { get; set; }
}

/// <summary>
/// First-time setup of a single-database site (hosting where you only get an empty database, such as a control panel without server access).
/// Exists only while "Setup:Key" is set, multi-tenant mode is off and the database has no company yet. Guarded by the setup key from the
/// settings file, so a stranger who finds the address cannot take the site over. After the setup the page is gone; remove the key.
/// </summary>
[AllowAnonymous]
[Route("Setup")]
public sealed class SetupController : Controller
{
    private readonly ISingleDatabaseSetup _setup;
    private readonly IConfiguration _configuration;
    private readonly TenancyOptions _tenancy;

    public SetupController(ISingleDatabaseSetup setup, IConfiguration configuration, IOptions<TenancyOptions> tenancy)
    {
        _setup = setup;
        _configuration = configuration;
        _tenancy = tenancy.Value;
    }

    private string? Key => _configuration["Setup:Key"];
    private string? Connection => _configuration.GetConnectionString("AMGHDatabase");
    private bool Enabled => !_tenancy.Enabled && !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Connection);

    private async Task<(bool Done, string? Problem)> StateAsync(CancellationToken ct)
    {
        try { return (await _setup.IsInitializedAsync(Connection!, ct), null); }
        catch (Microsoft.Data.SqlClient.SqlException ex) { return (false, ex.Message.Split('\n')[0].Trim()); }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!Enabled) return NotFound();
        var (done, problem) = await StateAsync(cancellationToken);
        if (done) { SetupGateMiddleware.MarkInitialized(); return NotFound(); }
        return View(new SetupViewModel { DatabaseProblem = problem });
    }

    [HttpPost("")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Run(SetupViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Enabled) return NotFound();
        var (done, problem) = await StateAsync(CancellationToken.None);
        if (done) { SetupGateMiddleware.MarkInitialized(); return NotFound(); }
        model.DatabaseProblem = problem;
        var supplied = Encoding.UTF8.GetBytes(model.SetupKey ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(supplied), SHA256.HashData(Encoding.UTF8.GetBytes(Key!))))
        {
            model.AdminPassword = null;
            model.Message = "The setup key is not correct. It is the value of Setup:Key in appsettings.Production.json.";
            return View("Index", model);
        }
        // not tied to the browser: closing the tab must not leave a half-built database behind (it takes a minute or two)
        var result = await _setup.InitializeAsync(Connection!, model.Code ?? string.Empty, model.Name ?? string.Empty, model.AdminEmail ?? string.Empty,
            model.AdminPassword ?? string.Empty, _configuration["Tenancy:MasterKeyPassword"], CancellationToken.None);
        model.AdminPassword = null;
        if (result.IsFailure)
        {
            model.Message = string.Join(" ", result.Errors.Select(e => e.Message));
            return View("Index", model);
        }
        SetupGateMiddleware.MarkInitialized();
        return View("Done", result.Value);
    }
}
