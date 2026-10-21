using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Application.AssetConfiguration;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Administration → Asset categories and Asset numbering (the signed-in company only).</summary>
[HasPermission(Permissions.MasterData.View)]
[Route("admin/asset-configuration")]
public sealed class AssetConfigurationController : Controller
{
    private readonly IStarterCategoryService _categories;
    private readonly INumberingService _numbering;

    public AssetConfigurationController(IStarterCategoryService categories, INumberingService numbering)
    {
        _categories = categories;
        _numbering = numbering;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) => View(await _categories.OverviewAsync(ct));

    [HttpPost("categories/starter")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> AddStarter(string[] codes, CancellationToken ct)
    {
        var r = await _categories.AddAsync(codes ?? Array.Empty<string>(), ct);
        TempData["StatusMessage"] = r.IsSuccess ? $"{r.Value} categor{(r.Value == 1 ? "y" : "ies")} added, each with its own number series." : r.FirstError!.Message;
        return RedirectToAction(nameof(Categories));
    }

    [HttpGet("numbering")]
    public async Task<IActionResult> Numbering(CancellationToken ct) => View(await _numbering.ListAsync(ct));

    [BlockInDemo]
    [HttpPost("numbering/{id:int}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> SaveSeries(int id, NumberSeriesForm form, CancellationToken ct)
    {
        var r = await _numbering.UpdateAsync(id, form, ct);
        TempData["StatusMessage"] = r.IsSuccess ? "Number series saved. Numbers already issued are not changed." : string.Join(" ", r.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Numbering));
    }

    /// <summary>Moving a sequence forward is an administrator action (company administrators: Manage roles).</summary>
    [HttpPost("numbering/{id:int}/next")]
    [HasPermission(Permissions.Administration.RolesManage)]
    public async Task<IActionResult> SetNext(int id, long nextNumber, CancellationToken ct)
    {
        var r = await _numbering.SetNextNumberAsync(id, nextNumber, ct);
        TempData["StatusMessage"] = r.IsSuccess ? $"The next asset in this series gets number {nextNumber}." : r.FirstError!.Message;
        return RedirectToAction(nameof(Numbering));
    }
}
