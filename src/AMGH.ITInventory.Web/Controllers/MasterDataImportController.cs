using AMGH.ITInventory.Application.MasterData;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Models;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Release 2.14: import and export of all master data in one Excel workbook (administrators: Manage master data).</summary>
[HasPermission(Permissions.MasterData.Manage)]
[Route("master-data/import")]
public sealed class MasterDataImportController : Controller
{
    private const long MaxUploadBytes = 26L * 1024 * 1024;
    private readonly IMasterDataImportService _import;

    public MasterDataImportController(IMasterDataImportService import) => _import = import;

    [HttpGet("")]
    public IActionResult Index() => View(new MasterImportViewModel());

    [HttpGet("workbook")]
    public async Task<IActionResult> Workbook(CancellationToken cancellationToken)
    {
        var bytes = await _import.ExportAsync(cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"EAMS-MasterData-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost("")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return View("Index", new MasterImportViewModel { Message = "Choose the Excel workbook to import." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return View("Index", new MasterImportViewModel { Message = "Upload an Excel workbook (.xlsx)." });
        await using var stream = file.OpenReadStream();
        var result = await _import.PreviewAsync(stream, cancellationToken);
        return View("Index", new MasterImportViewModel { Result = result, FileName = Path.GetFileName(file.FileName) });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(string token, CancellationToken cancellationToken)
    {
        var result = await _import.CommitAsync(token, cancellationToken);
        return View("Index", new MasterImportViewModel { Result = result });
    }
}
