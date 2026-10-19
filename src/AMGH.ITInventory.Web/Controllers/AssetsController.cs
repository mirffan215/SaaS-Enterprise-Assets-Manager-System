using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Assignments;
using AMGH.ITInventory.Application.Labels;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

[HasPermission(Permissions.Assets.View)]
public sealed class AssetsController : Controller
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxUploadBytes = 26L * 1024 * 1024;

    private readonly IAssetService _assets;
    private readonly IAssetDocumentService _documents;
    private readonly IAssetImportExportService _importExport;
    private readonly ILookupService _lookups;
    private readonly ILogger<AssetsController> _logger;
    private readonly IAssignmentService _assignments;
    private readonly ILabelService _labels;

    public AssetsController(IAssetService assets, IAssetDocumentService documents, IAssetImportExportService importExport,
        ILookupService lookups, ILogger<AssetsController> logger, IAssignmentService assignments, ILabelService labels)
    {
        _assignments = assignments;
        _labels = labels;
        _assets = assets;
        _documents = documents;
        _importExport = importExport;
        _lookups = lookups;
        _logger = logger;
    }

    private AssetAccess Access => new(User.HasPermission(Permissions.Assets.ViewFinancials), User.HasPermission(Permissions.Assets.ViewSecurityDetails));

    // ------------------------------------------------------------------ register

    public async Task<IActionResult> Index([FromQuery] AssetSearchCriteria criteria, [FromQuery] string? cols, CancellationToken cancellationToken)
    {
        criteria ??= new AssetSearchCriteria();
        // columns: from the link (a saved view or the chooser), else this browser's last choice, else the defaults
        if (cols is not null)
            Response.Cookies.Append(AssetColumns.CookieName, cols, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddYears(1) });
        var columns = AssetColumns.Parse(cols ?? Request.Cookies[AssetColumns.CookieName]);
        var query = string.Join("&", Request.Query.Where(q => q.Key is not ("Page" or "page" or "cols"))
            .SelectMany(q => q.Value.Select(v => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(v ?? string.Empty))));
        query = string.Join("&", new[] { query, "cols=" + Uri.EscapeDataString(string.Join(",", columns.Select(c => c.Key))) }.Where(x => x.Length > 0));
        return View(new AssetIndexViewModel
        {
            Columns = columns,
            Views = await HttpContext.RequestServices.GetRequiredService<Application.Views.ISavedViewService>().ListAsync("assets", cancellationToken),
            QueryString = query,
            Criteria = criteria,
            Results = await _assets.SearchAsync(criteria, cancellationToken),
            Lookups = await _lookups.GetAssetFormLookupsAsync(cancellationToken),
            CanCreate = User.HasPermission(Permissions.Assets.Create),
            CanImport = User.HasPermission(Permissions.Assets.Import),
            CanExport = User.HasPermission(Permissions.Assets.Export),
            CanPrintLabels = User.HasPermission(Permissions.QrCodes.Print)
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveView(string name, string query, bool shared, CancellationToken cancellationToken)
    {
        var views = HttpContext.RequestServices.GetRequiredService<Application.Views.ISavedViewService>();
        var r = await views.SaveAsync("assets", name, query, shared, cancellationToken);
        TempData["StatusMessage"] = r.IsSuccess ? $"View \"{name?.Trim()}\" saved." : r.FirstError!.Message;
        return Redirect(Url.Action(nameof(Index)) + "?" + query);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteView(int id, CancellationToken cancellationToken)
    {
        var r = await HttpContext.RequestServices.GetRequiredService<Application.Views.ISavedViewService>().DeleteAsync(id, cancellationToken);
        TempData["StatusMessage"] = r.IsSuccess ? "View deleted." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var details = await _assets.GetDetailsAsync(id, Access, cancellationToken);
        if (details is null) return NotFound();
        var canSeeDocs = User.HasPermission(Permissions.Documents.View);
        return View(new AssetDetailsViewModel
        {
            Asset = details,
            Access = Access,
            History = await _assets.GetHistoryAsync(id, 200, cancellationToken),
            Documents = canSeeDocs ? await _documents.ListAsync(id, cancellationToken) : null,
            Classifications = await _lookups.GetOptionsAsync(LookupKeys.Classifications, cancellationToken),
            CanEdit = User.HasPermission(Permissions.Assets.Edit) && details.StatusCode != "DISPOSED",
            CanDelete = User.HasPermission(Permissions.Assets.Delete) && details.StatusCode is not ("ASSIGNED" or "PEND_DISP" or "DISPOSED"),
            CanUpload = User.HasPermission(Permissions.Documents.Upload),
            CanDeleteDocuments = User.HasPermission(Permissions.Documents.Delete),
            Assignments = User.HasPermission(Permissions.Assignments.View) ? await _assignments.GetHistoryAsync(id, cancellationToken) : null,
            LabelHistory = await _labels.GetHistoryAsync(id, cancellationToken),
            CanAssign = User.HasPermission(Permissions.Assignments.Assign),
            CanReturn = User.HasPermission(Permissions.Assignments.Return),
            CanPrintLabel = User.HasPermission(Permissions.QrCodes.Print) && details.StatusCode != "DISPOSED",
            AllowedExtensions = string.Join(",", AssetDocumentRules.AllowedExtensions),
            Lifecycle = await LifecycleAsync(id, details.StatusCode, cancellationToken)
        });
    }

    private async Task<AssetLifecycleViewModel> LifecycleAsync(int id, string statusCode, CancellationToken ct)
    {
        var sp = HttpContext.RequestServices;
        bool P(string permission) => User.HasPermission(permission);
        var active = statusCode is not ("DISPOSED" or "PEND_DISP");
        return new AssetLifecycleViewModel
        {
            Movements = P(Permissions.Movements.View) ? await sp.GetRequiredService<Application.Lifecycle.IMovementService>().ListAsync(id, null, 50, ct) : null,
            Maintenance = P(Permissions.Maintenance.View) ? await sp.GetRequiredService<Application.Lifecycle.IMaintenanceService>().ListAsync(id, Application.Lifecycle.MaintenanceView.All, 50, cancellationToken: ct) : null,
            Warranties = P(Permissions.Warranties.View) ? await sp.GetRequiredService<Application.Lifecycle.IWarrantyService>().ListAsync(id, null, 50, ct) : null,
            Licences = P(Permissions.Licenses.View) ? await sp.GetRequiredService<Application.Lifecycle.ILicenseService>().AllocationsAsync(null, id, ct) : null,
            Disposals = P(Permissions.Disposals.View) ? await sp.GetRequiredService<Application.Lifecycle.IDisposalService>().ListAsync(id, null, 10, ct) : null,
            CanMove = active && P(Permissions.Movements.Request),
            CanMaintain = statusCode != "DISPOSED" && P(Permissions.Maintenance.Manage),
            CanWarranty = P(Permissions.Warranties.Manage),
            CanVerify = statusCode != "DISPOSED" && P(Permissions.Audits.Perform),
            CanDispose = active && P(Permissions.Disposals.Request)
        };
    }

    // ------------------------------------------------------------------ create / edit / delete

    [HttpGet]
    [HasPermission(Permissions.Assets.Create)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View("Form", await FormAsync(await _assets.NewAsync(cancellationToken), cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Assets.Create)]
    public async Task<IActionResult> Create([Bind(Prefix = "Asset")] AssetEditModel asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.AssetId = 0;
        if (ModelState.IsValid)
        {
            var result = await _assets.CreateAsync(asset, Access, cancellationToken);
            if (result.IsSuccess)
            {
                TempData["StatusMessage"] = $"Asset {result.Value.AssetNumber} was created.";
                return RedirectToAction(nameof(Details), new { id = result.Value.AssetId });
            }
            ModelState.AddErrors(result.Errors.Select(e => e with { Code = Prefixed(e.Code) }));
        }
        return View("Form", await FormAsync(asset, cancellationToken));
    }

    [HttpGet]
    [HasPermission(Permissions.Assets.Edit)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var model = await _assets.GetForEditAsync(id, Access, cancellationToken);
        if (model is null) return NotFound();
        if (model.CurrentStatusCode == "DISPOSED")
        {
            TempData["StatusMessage"] = "Disposed assets are a read-only record.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View("Form", await FormAsync(model, cancellationToken));
    }

    [HttpPost]
    [HasPermission(Permissions.Assets.Edit)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Asset")] AssetEditModel asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.AssetId = id;
        if (ModelState.IsValid)
        {
            var result = await _assets.UpdateAsync(asset, Access, cancellationToken);
            if (result.IsSuccess)
            {
                TempData["StatusMessage"] = "Changes were saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddErrors(result.Errors.Select(e => e with { Code = Prefixed(e.Code) }));
        }
        var current = await _assets.GetForEditAsync(id, Access, cancellationToken);
        if (current is null) return NotFound();
        asset.AssetNumber = current.AssetNumber;
        asset.CurrentStatusCode = current.CurrentStatusCode;
        asset.StatusLocked = current.StatusLocked;
        return View("Form", await FormAsync(asset, cancellationToken));
    }

    [HttpPost]
    [HasPermission(Permissions.Assets.Delete)]
    public async Task<IActionResult> Delete(int id, string rowVersion, CancellationToken cancellationToken)
    {
        var result = await _assets.DeleteAsync(id, rowVersion, cancellationToken);
        if (result.IsFailure)
        {
            TempData["StatusMessage"] = result.FirstError!.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        _logger.LogInformation("Asset {AssetId} deleted by {User}", id, User.Identity?.Name);
        TempData["StatusMessage"] = "The asset was deleted. Its number stays reserved and its history is kept.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ Excel

    [HttpGet]
    [HasPermission(Permissions.Assets.Export)]
    public async Task<IActionResult> Export([FromQuery] AssetSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var bytes = await _importExport.ExportAsync(criteria ?? new AssetSearchCriteria(), Access, cancellationToken);
        _logger.LogInformation("Asset register exported by {User}", User.Identity?.Name);
        return File(bytes, XlsxType, $"AMGH-IT-Assets-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    [HasPermission(Permissions.Assets.Import)]
    public async Task<IActionResult> Template(CancellationToken cancellationToken)
    {
        var bytes = _importExport.CreateTemplate(Access, await _lookups.GetAssetFormLookupsAsync(cancellationToken));
        return File(bytes, XlsxType, "AMGH-IT-Asset-Import-Template.xlsx");
    }

    [HttpGet]
    [HasPermission(Permissions.Assets.Import)]
    public IActionResult Import() => View(new AssetImportViewModel { MaxRows = Application.Assets.ImportLimits.AssetRows });

    [HttpPost]
    [HasPermission(Permissions.Assets.Import)]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose the completed .xlsx file to upload.");
            return View(new AssetImportViewModel { MaxRows = Application.Assets.ImportLimits.AssetRows });
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Only Excel workbooks (.xlsx) can be imported.");
            return View(new AssetImportViewModel { MaxRows = Application.Assets.ImportLimits.AssetRows });
        }
        await using var stream = file.OpenReadStream();
        var preview = await _importExport.PreviewAsync(stream, Access, cancellationToken);
        return View(new AssetImportViewModel { Preview = preview, MaxRows = Application.Assets.ImportLimits.AssetRows });
    }

    [HttpPost]
    [HasPermission(Permissions.Assets.Import)]
    public async Task<IActionResult> ConfirmImport(string token, CancellationToken cancellationToken)
    {
        var result = await _importExport.CommitAsync(token, Access, cancellationToken);
        if (result.IsFailure) return View("Import", new AssetImportViewModel { CommitErrors = result.Errors, MaxRows = Application.Assets.ImportLimits.AssetRows });
        _logger.LogInformation("{Count} assets imported by {User}", result.Value.Count, User.Identity?.Name);
        return View("Import", new AssetImportViewModel { Created = result.Value, MaxRows = Application.Assets.ImportLimits.AssetRows });
    }

    // ------------------------------------------------------------------ documents

    [HttpPost]
    [HasPermission(Permissions.Documents.Upload)]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> UploadDocument(int id, IFormFile? file, DocumentType documentType, string? title,
        int? classificationId, string? description, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["StatusMessage"] = "Choose a file to attach.";
            return RedirectToAction(nameof(Details), new { id, tab = "documents" });
        }
        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsync(new DocumentUpload(id, documentType, string.IsNullOrWhiteSpace(title) ? file.FileName : title,
            file.FileName, file.Length, stream, classificationId, description), cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "The document was attached." : result.FirstError!.Message;
        return RedirectToAction(nameof(Details), new { id, tab = "documents" });
    }

    [HttpGet]
    [HasPermission(Permissions.Documents.View)]
    public async Task<IActionResult> Document(int id, int documentId, CancellationToken cancellationToken)
    {
        var file = await _documents.DownloadAsync(id, documentId, cancellationToken);
        if (file is null) return NotFound();
        return File(file.Content, file.ContentType, file.FileName); // Content-Disposition: attachment
    }

    [HttpPost]
    [HasPermission(Permissions.Documents.Delete)]
    public async Task<IActionResult> DeleteDocument(int id, int documentId, CancellationToken cancellationToken)
    {
        var result = await _documents.DeleteAsync(id, documentId, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "The document was removed." : result.FirstError!.Message;
        return RedirectToAction(nameof(Details), new { id, tab = "documents" });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<AssetFormViewModel> FormAsync(AssetEditModel model, CancellationToken ct) => new()
    {
        Asset = model,
        Lookups = await _lookups.GetAssetFormLookupsAsync(ct),
        Access = Access
    };

    /// <summary>Service errors name model properties; form fields are prefixed with "Asset.".</summary>
    private static string Prefixed(string code) => code.Length > 0 && char.IsUpper(code[0]) ? "Asset." + code : code;
}
