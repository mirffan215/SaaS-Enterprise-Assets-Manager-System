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
        return View(result.Value.Design == LabelDesign.AssetTag ? "PrintTags" : "Print", result.Value);
    }
}

/// <summary>Target of the link inside every QR code: /a/AMGH-IT-LAP-00001 opens the live asset record (sign-in required).</summary>
[HasPermission(Permissions.Assets.View)]
public sealed class ScanController : Controller
{
    private readonly Application.Assets.IAssetService _assets;

    public ScanController(Application.Assets.IAssetService assets) => _assets = assets;

    [HttpGet("a/{assetNumber}")]
    public async Task<IActionResult> Open(string assetNumber, CancellationToken cancellationToken)
    {
        var id = await _assets.FindIdByNumberAsync(assetNumber, cancellationToken);
        return id is null ? NotFound() : RedirectToAction("Details", "Assets", new { id });
    }
}
