using AMGH.ITInventory.Application.Documents;
using AMGH.ITInventory.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Document detail pages (Release 2.13): movements, maintenance jobs, disposals, assignments, verifications and warranties.
/// Each type needs its view permission; documents of other companies do not exist for the user (company isolation).
/// </summary>
public sealed class DocumentsController : Controller
{
    private readonly IDocumentService _documents;

    public DocumentsController(IDocumentService documents) => _documents = documents;

    private IReadOnlySet<string> Permissions =>
        User.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    [HttpGet("documents/{type}/{id:int}")]
    public async Task<IActionResult> Show(string type, int id, CancellationToken cancellationToken)
    {
        var doc = await _documents.GetAsync(type, id, Permissions, cancellationToken);
        if (doc is null) return NotFound();
        ViewData["Title"] = doc.Number ?? doc.Type.Name;
        return View(doc);
    }

    /// <summary>Short link by number, e.g. /d/AMGH-MOV-000123 (for e-mails, printed forms and ERP links).</summary>
    [HttpGet("d/{number}")]
    public async Task<IActionResult> ByNumber(string number, CancellationToken cancellationToken)
    {
        var found = await _documents.FindAsync(number, Permissions, cancellationToken);
        return found is null ? NotFound() : RedirectToAction(nameof(Show), new { type = found.Type.Code, id = found.Id });
    }

    [HttpPost("documents/{type}/{id:int}/erp")]
    public async Task<IActionResult> SetErpReference(string type, int id, string? erpReference, CancellationToken cancellationToken)
    {
        var r = await _documents.SetErpReferenceAsync(type, id, erpReference, Permissions, cancellationToken);
        TempData["StatusMessage"] = r.IsSuccess ? (string.IsNullOrWhiteSpace(erpReference) ? "ERP reference removed." : "ERP reference saved.") : r.FirstError!.Message;
        return RedirectToAction(nameof(Show), new { type, id });
    }
}
