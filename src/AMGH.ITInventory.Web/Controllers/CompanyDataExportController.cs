using AMGH.ITInventory.Application.CompanyAdmin;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Company administrator self-service: a full export of the company's data. The request goes through the Approval Center and
/// needs a second administrator; the requester then downloads an encrypted .zip with a one-time token.
/// </summary>
[Route("company/data-export")]
public sealed class CompanyDataExportController : Controller
{
    private readonly IApprovalService _approvals;
    private readonly ICompanyDataExportService _exports;
    private readonly bool _requiresApproval;

    public CompanyDataExportController(IApprovalService approvals, ICompanyDataExportService exports,
        Microsoft.Extensions.Options.IOptions<AMGH.ITInventory.Application.Tenancy.TenancyOptions> tenancy)
    {
        _approvals = approvals;
        _exports = exports;
        _requiresApproval = tenancy.Value.ExportRequiresApproval;
    }

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!User.HasPermission(Permissions.CompanyAdmin.ExportDataRequest) && !User.HasPermission(Permissions.CompanyAdmin.ExportDataApprove)
            && !User.HasPermission(Permissions.CompanyAdmin.ExportDataDownload))
            context.Result = Forbid();
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _exports.ListAsync(cancellationToken);
        var me = User.Identity?.Name ?? string.Empty;
        var credentials = new Dictionary<int, DataExportCredentials>();
        if (User.HasPermission(Permissions.CompanyAdmin.ExportDataDownload))
            foreach (var item in items.Where(i => i.Status == "Approved" && string.Equals(i.RequestedBy, me, StringComparison.OrdinalIgnoreCase)))
                if (await _exports.CredentialsAsync(item.Id, me, cancellationToken) is { } c) credentials[item.Id] = c;
        return View(new DataExportPageModel(items, credentials, _requiresApproval));
    }

    [HttpPost("request")]
    [HasPermission(Permissions.CompanyAdmin.ExportDataRequest)]
    public async Task<IActionResult> RequestExport(string? reason, CancellationToken cancellationToken)
    {
        var result = await _approvals.RequestDataExportAsync(reason ?? string.Empty, cancellationToken);
        TempData[result.IsSuccess ? "StatusMessage" : "ErrorMessage"] = result.IsSuccess
            ? (_requiresApproval ? "Export requested. A second administrator must approve it in the Approval Center before you can download it."
                                 : "Export ready. Copy the archive password below, then download the file.")
            : string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST so the token never appears in a URL or log; the antiforgery token applies as on every form.</summary>
    [HttpPost("{id:int}/download")]
    [HasPermission(Permissions.CompanyAdmin.ExportDataDownload)]
    public async Task<IActionResult> Download(int id, string? token, CancellationToken cancellationToken)
    {
        var result = await _exports.DownloadAsync(id, token ?? string.Empty, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.FirstError!.Message;
            return RedirectToAction(nameof(Index));
        }
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Content, "application/zip", result.Value.FileName);
    }
}

public sealed record DataExportPageModel(IReadOnlyList<DataExportItem> Items, IReadOnlyDictionary<int, DataExportCredentials> Credentials, bool RequiresApproval);
