using AMGH.ITInventory.Application.RecycleBin;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Recycle bin: deleted records of the company with a one-click restore (undo). Company administrators only.</summary>
[Route("recycle-bin")]
[HasPermission(Permissions.Administration.SystemSettings)]
public sealed class RecycleBinController : Controller
{
    private readonly IRecycleBinService _bin;

    public RecycleBinController(IRecycleBinService bin) => _bin = bin;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? type, CancellationToken cancellationToken)
    {
        var types = await _bin.TypesAsync(cancellationToken);
        var selected = types.FirstOrDefault(t => t.Type == type) ?? types.FirstOrDefault(t => t.Count > 0) ?? types.FirstOrDefault();
        var items = selected is null ? Array.Empty<RecycledItem>() : await _bin.ListAsync(selected.Type, cancellationToken);
        return View(new RecycleBinPageModel(types, selected?.Type, items));
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore(string type, int id, CancellationToken cancellationToken)
    {
        var result = await _bin.RestoreAsync(type ?? string.Empty, id, cancellationToken);
        TempData[result.IsSuccess ? "StatusMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Restored: {result.Value}."
            : result.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { type });
    }
}

public sealed record RecycleBinPageModel(IReadOnlyList<RecycleTypeInfo> Types, string? Selected, IReadOnlyList<RecycledItem> Items);
