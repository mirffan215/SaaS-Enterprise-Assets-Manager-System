using AMGH.ITInventory.Application.Search;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Global search (any signed-in user; each group is limited to what the user may open).</summary>
[Route("search")]
public sealed class SearchController : Controller
{
    private readonly ISearchService _search;

    public SearchController(ISearchService search) => _search = search;

    private SearchScope Scope => new(User.HasPermission(Permissions.Assets.View), User.HasPermission(Permissions.MasterData.View),
        User.HasPermission(Permissions.Licenses.View),
        User.FindAll(AMGH.ITInventory.Security.Claims.AppClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal));

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var results = await _search.SearchAsync(q ?? string.Empty, Scope, 25, ct);
        // a scanned label or an exact asset number goes straight to the asset
        if (results.ExactAssetId is { } id) return RedirectToAction("Details", "Assets", new { id });
        // an exact document number (e.g. AMGH-MOV-000123) opens the document
        if (results.ExactDocument is { } doc) return RedirectToAction("Show", "Documents", new { type = doc.Type.Code, id = doc.Id });
        ViewData["Title"] = "Search";
        return View(results);
    }

    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(string? q, CancellationToken ct)
    {
        var results = await _search.SearchAsync(q ?? string.Empty, Scope, 4, ct);
        Response.Headers.CacheControl = "no-store";
        return Json(results.Groups.SelectMany(g => g.Hits.Select(h => new { group = g.Title, title = h.Title, subtitle = h.Subtitle, url = UrlFor(h) }))
            .Take(14));
    }

    internal string UrlFor(SearchHit h) => h.Kind switch
    {
        SearchKind.Asset => Url.Action("Details", "Assets", new { id = h.Id })!,
        SearchKind.Document => Url.Action("Show", "Documents", new { type = h.DocType, id = h.Id })!,
        SearchKind.Licence => Url.Action("Details", "Licenses", new { licenseId = h.Id })!,
        SearchKind.Employee => Url.Action("Edit", "MasterData", new { type = "employees", id = h.Id })!,
        SearchKind.Department => Url.Action("Edit", "MasterData", new { type = "departments", id = h.Id })!,
        SearchKind.Location => Url.Action("Edit", "MasterData", new { type = "locations", id = h.Id })!,
        _ => Url.Action("Edit", "MasterData", new { type = "vendors", id = h.Id })!
    };
}
