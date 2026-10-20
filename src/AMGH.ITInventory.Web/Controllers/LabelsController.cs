using AMGH.ITInventory.Application.Labels;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>QR asset labels, printed from the browser on any label printer at the exact label size.</summary>
[HasPermission(Permissions.QrCodes.Print)]
[Route("labels")]
public sealed class LabelsController : Controller
{
    private readonly ILabelService _labels;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LabelsController> _logger;

    public LabelsController(ILabelService labels, IConfiguration configuration, ILogger<LabelsController> logger)
    {
        _labels = labels;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Address encoded in the QR code. Set Labels:BaseUrl when users reach the site by another name.</summary>
    private string BaseUrl => _configuration["Labels:BaseUrl"] is { Length: > 0 } configured
        ? configured
        : $"{Request.Scheme}://{Request.Host}";

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int[] ids, [FromQuery] LabelDesign design, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["StatusMessage"] = "Select one or more assets to label.";
            return RedirectToAction("Index", "Assets");
        }
        return View(new LabelOptionsViewModel
        {
            Preview = await _labels.PreviewAsync(ids, cancellationToken),
            Design = Enum.IsDefined(design) ? design : LabelDesign.QrLabel,
            CanReprint = User.HasPermission(Permissions.QrCodes.Reprint)
        });
    }

    [HttpPost("print")]
    public async Task<IActionResult> Print(int[] ids, LabelDesign design, LabelSize size, int copies, string? reprintReason, string? style,
        CancellationToken cancellationToken)
    {
        style = style == "mono" ? "mono" : "colour";
        var result = await _labels.PrintAsync(new LabelPrintRequest(ids ?? Array.Empty<int>(), size, copies, reprintReason, design), BaseUrl,
            User.HasPermission(Permissions.QrCodes.Reprint), cancellationToken);
        if (result.IsFailure)
        {
            ModelState.AddErrors(result.Errors);
            return View("Index", new LabelOptionsViewModel
            {
                Preview = await _labels.PreviewAsync(ids ?? Array.Empty<int>(), cancellationToken),
                Design = design,
                Style = style,
                Size = size,
                Copies = copies,
                ReprintReason = reprintReason,
                CanReprint = User.HasPermission(Permissions.QrCodes.Reprint)
            });
        }
        _logger.LogInformation("{Count} label(s) printed by {User}, batch {Batch}", result.Value.Labels.Count, User.Identity?.Name, result.Value.BatchId);
        Response.Headers.CacheControl = "no-store";
        ViewData["TagStyle"] = style;
        return View(result.Value.Design switch { LabelDesign.AssetTag => "PrintTags", LabelDesign.RfidLabel => "PrintRfid", _ => "Print" }, result.Value);
    }

    /// <summary>The file an RFID printer (or its encoding software) uses to write each tag's EPC: CSV, or ZPL for Zebra RFID printers.</summary>
    [HttpPost("rfid-file")]
    public async Task<IActionResult> RfidFile(int[] ids, string? format, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Length == 0) return RedirectToAction("Index", "Assets");
        var tags = await _labels.GetRfidTagsAsync(ids, BaseUrl, cancellationToken);
        if (tags.Count == 0)
        {
            TempData["StatusMessage"] = "None of the selected assets can carry a tag.";
            return RedirectToAction("Index", "Assets");
        }
        _logger.LogInformation("RFID encoding file for {Count} tag(s) made by {User}", tags.Count, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(format, "zpl", StringComparison.OrdinalIgnoreCase)
            ? File(System.Text.Encoding.ASCII.GetBytes(RfidEncoding.Zpl(tags)), "application/octet-stream", $"rfid-tags-{stamp}.zpl")
            : File(new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(RfidEncoding.Csv(tags))).ToArray(), "text/csv", $"rfid-tags-{stamp}.csv");
    }
}

/// <summary>Target of the link inside every QR code: /a/AMGH-IT-LAP-00001 opens the live asset record (sign-in required).</summary>
[HasPermission(Permissions.Assets.View)]
public sealed class ScanController : Controller
{
    private readonly Application.Assets.IAssetService _assets;

    public ScanController(Application.Assets.IAssetService assets) => _assets = assets;

    /// <summary>The scan page of the mobile app: camera (QR label, barcode) or typed number, then the asset opens.</summary>
    [HttpGet("scan")]
    public IActionResult Index() => View("Scan");

    [HttpGet("a/{assetNumber}")]
    public async Task<IActionResult> Open(string assetNumber, CancellationToken cancellationToken)
    {
        var id = await _assets.FindIdByNumberAsync(assetNumber, cancellationToken);
        return id is null ? NotFound() : RedirectToAction("Details", "Assets", new { id });
    }

    /// <summary>Target for an RFID reader or a scanner app: /r/A5000001000000010F3A9C21 opens the asset whose tag carries that EPC.</summary>
    [HttpGet("r/{epc}")]
    public async Task<IActionResult> OpenByRfid(string epc, CancellationToken cancellationToken)
    {
        var id = await _assets.FindIdByRfidAsync(epc, cancellationToken);
        return id is null ? NotFound() : RedirectToAction("Details", "Assets", new { id });
    }
}
