using AMGH.ITInventory.Application.ConfigTransfer;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Company administrator: export the company's configuration as XML, import it into this or another company (with a preview that
/// lets the administrator choose what to apply), or copy it directly from another company they can access.
/// </summary>
[Route("admin/configuration")]
[HasPermission(Permissions.Administration.SystemSettings)]
public sealed class ConfigurationTransferController : Controller
{
    private readonly IConfigurationTransferService _transfer;
    private readonly IAccountService _accounts;

    public ConfigurationTransferController(IConfigurationTransferService transfer, IAccountService accounts)
    {
        _transfer = transfer;
        _accounts = accounts;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Sources"] = await SourcesAsync(cancellationToken);
        return View();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var bytes = await _transfer.ExportAsync(cancellationToken);
        var code = string.Concat(User.GetCompanyCode().Where(char.IsAsciiLetterOrDigit));
        return File(bytes, "application/xml", $"configuration-{(code.Length == 0 ? "company" : code)}-{DateTime.UtcNow:yyyyMMdd}.xml");
    }

    [HttpPost("preview")]
    [RequestSizeLimit(ConfigurationXml.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return Back("Choose the XML file to import.");
        if (file.Length > ConfigurationXml.MaxBytes) return Back("The file is larger than 2 MB.");
        await using var stream = file.OpenReadStream();
        var result = await _transfer.PreviewImportAsync(stream, cancellationToken);
        return result.IsFailure ? Back(result.FirstError!.Message) : View("ConfigDiffPreview", result.Value);
    }

    [HttpPost("preview-copy")]
    public async Task<IActionResult> PreviewCopy(int sourceCompanyId, CancellationToken cancellationToken)
    {
        // the signed-in user must belong to (or, as platform administrator, be able to reach) the source company
        if ((await SourcesAsync(cancellationToken)).All(s => s.CompanyId != sourceCompanyId)) return Forbid();
        var result = await _transfer.PreviewCopyAsync(sourceCompanyId, cancellationToken);
        return result.IsFailure ? Back(result.FirstError!.Message) : View("ConfigDiffPreview", result.Value);
    }

    [HttpPost("commit")]
    [BlockInDemo]
    public async Task<IActionResult> Commit(string? token, string[]? selected, CancellationToken cancellationToken)
    {
        Result<ConfigImportResult> result;
        try
        {
            result = await _transfer.CommitAsync(token ?? string.Empty, selected ?? Array.Empty<string>(), cancellationToken);
        }
        catch (Exception ex) when (ex is BusinessRuleException or DuplicateValueException or DataIntegrityException or ConcurrencyConflictException)
        {
            return Back("Nothing was saved: " + ex.Message);
        }
        if (result.IsFailure) return Back(result.FirstError!.Message);
        var r = result.Value;
        TempData["StatusMessage"] = $"Configuration applied: {r.Added} added, {r.Modified} changed" + (r.Skipped > 0 ? $", {r.Skipped} skipped (their category was not selected)." : ".");
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Back(string message)
    {
        TempData["ErrorMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The other companies the person may copy from.</summary>
    private async Task<IReadOnlyList<CompanyMembership>> SourcesAsync(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null) return Array.Empty<CompanyMembership>();
        var current = User.GetCompanyId();
        return (await _accounts.GetMembershipsAsync(userId.Value, cancellationToken)).Where(m => m.CompanyId != current).ToList();
    }
}
