using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Application.Reports;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Reports centre: run, filter, group, export (Excel, CSV, Word, print/PDF), save configurations, history.</summary>
[HasPermission(Permissions.Reports.View)]
[Route("reports")]
public sealed class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly ILookupService _lookups;
    private readonly ICompanyDirectory _directory;
    private readonly IDisplayTime _time;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IReportService reports, ILookupService lookups, ICompanyDirectory directory, IDisplayTime time,
        ILogger<ReportsController> logger)
    {
        _reports = reports;
        _lookups = lookups;
        _directory = directory;
        _time = time;
        _logger = logger;
    }

    private ReportAccess Access => new(
        User.HasPermission(Permissions.Reports.Export),
        User.HasPermission(Permissions.Assets.ViewFinancials),
        User.HasPermission(Permissions.Assets.ViewSecurityDetails),
        User.HasPermission(Permissions.Administration.AuditLogsView));

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(new ReportCentreViewModel
    {
        Reports = _reports.Available(Access),
        Saved = await _reports.SavedAsync(null, cancellationToken),
        Recent = (await _reports.HistoryAsync(10, cancellationToken))
    });

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken) =>
        View(await _reports.HistoryAsync(500, cancellationToken));

    [HttpGet("{code}")]
    public async Task<IActionResult> Run(string code, [FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        var result = await _reports.RunAsync(code, request ?? new ReportRequest(), Access, cancellationToken: cancellationToken);
        if (result.IsFailure) return result.FirstError!.Type == Shared.Results.ErrorType.NotFound ? NotFound() : Forbid();
        return View(new ReportRunViewModel
        {
            Result = result.Value,
            Request = request ?? new ReportRequest(),
            QueryString = _reports.ToQueryString(request ?? new ReportRequest()),
            CanExport = Access.CanExport,
            Saved = await _reports.SavedAsync(code, cancellationToken),
            Categories = await _lookups.GetOptionsAsync(LookupKeys.Categories, cancellationToken),
            Departments = await _lookups.GetOptionsAsync(LookupKeys.Departments, cancellationToken),
            Locations = await _lookups.GetOptionsAsync(LookupKeys.Locations, cancellationToken),
            Buildings = await _lookups.GetOptionsAsync(LookupKeys.BuildingsWithLocation, cancellationToken),
            Statuses = await _lookups.GetOptionsAsync(LookupKeys.Statuses, cancellationToken)
        });
    }

    [HttpGet("{code}/export")]
    [HasPermission(Permissions.Reports.Export)]
    public async Task<IActionResult> Export(string code, [FromQuery] ReportRequest request, [FromQuery] string format, CancellationToken cancellationToken)
    {
        var fmt = (format ?? string.Empty).ToLowerInvariant() switch
        {
            "excel" or "xlsx" => ReportFormat.Excel,
            "csv" => ReportFormat.Csv,
            "word" or "docx" => ReportFormat.Word,
            _ => (ReportFormat?)null
        };
        if (fmt is null) return BadRequest();
        var file = await _reports.ExportAsync(code, request ?? new ReportRequest(), Access, fmt.Value, await ContextAsync(cancellationToken), cancellationToken);
        if (file.IsFailure) return file.FirstError!.Type == Shared.Results.ErrorType.NotFound ? NotFound() : Forbid();
        _logger.LogInformation("Report {Report} exported as {Format} by {User}", code, fmt, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    /// <summary>Print-ready page (A4, header and footer, page numbers): print it or choose "Save as PDF".</summary>
    [HttpGet("{code}/print")]
    [HasPermission(Permissions.Reports.Export)]
    public async Task<IActionResult> Print(string code, [FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        var result = await _reports.RunAsync(code, request ?? new ReportRequest(), Access, allRows: true, cancellationToken);
        if (result.IsFailure) return result.FirstError!.Type == Shared.Results.ErrorType.NotFound ? NotFound() : Forbid();
        await _reports.LogAsync(code, ReportFormat.Print, result.Value.TotalRows, result.Value.AppliedFilters, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        var ctx = await ContextAsync(cancellationToken);
        return View(new ReportPrintViewModel { Result = result.Value, Context = ctx, CompanyCode = User.GetCompanyCode(), HasLogo = ctx.LogoImage is not null,
            BackUrl = Url.Action(nameof(Run), new { code }) + "?" + _reports.ToQueryString(request ?? new ReportRequest()) });
    }

    [HttpPost("{code}/save")]
    public async Task<IActionResult> Save(string code, [FromQuery] ReportRequest request, string saveName, bool shared, CancellationToken cancellationToken)
    {
        var result = await _reports.SaveAsync(code, saveName, request ?? new ReportRequest(), shared, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? $"Saved as \"{saveName?.Trim()}\"." : result.FirstError!.Message;
        return Redirect(Url.Action(nameof(Run), new { code }) + "?" + _reports.ToQueryString(request ?? new ReportRequest()));
    }

    [HttpPost("saved/{id:int}/delete")]
    public async Task<IActionResult> DeleteSaved(int id, CancellationToken cancellationToken)
    {
        var result = await _reports.DeleteSavedAsync(id, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "Saved report deleted." : result.FirstError!.Message;
        return RedirectToAction(nameof(Index));
    }

    private async Task<ReportContext> ContextAsync(CancellationToken ct)
    {
        var code = User.GetCompanyCode();
        var branding = await _directory.GetBrandingAsync(code, ct);
        var logo = branding?.HasLogo == true ? await _directory.GetLogoAsync(code, ct) : null;
        return new ReportContext(branding?.LegalName ?? User.GetCompanyName(), logo?.Content, logo?.ContentType,
            $"{_time.Format(DateTime.UtcNow)} ({_time.ZoneLabel})", User.GetDisplayName());
    }
}
