using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.MasterData;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

[HasPermission(Permissions.MasterData.View)]
[Route("master-data")]
public sealed class MasterDataController : Controller
{
    private readonly IMasterDataService _service;

    public MasterDataController(IMasterDataService service) => _service = service;

    [HttpGet("")]
    public IActionResult Index() => View(_service.Types);

    [HttpGet("{type}")]
    public async Task<IActionResult> List(string type, string? q, bool inactive = false, int page = 1, CancellationToken cancellationToken = default)
    {
        var def = _service.Find(type);
        if (def is null) return NotFound();
        return View(new MasterDataListViewModel
        {
            Type = def.Info,
            Page = await _service.ListAsync(type, q, inactive, page, cancellationToken),
            Search = q,
            IncludeInactive = inactive,
            CanManage = User.HasPermission(Permissions.MasterData.Manage),
            AllowCreate = def.AllowCreate
        });
    }

    [HttpGet("{type}/new")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Create(string type, CancellationToken cancellationToken)
    {
        var def = _service.Find(type);
        if (def is null || !def.AllowCreate) return NotFound();
        var defaults = def.Fields.Where(f => f.Kind == FieldKind.Boolean && f.Name == "IsActive")
            .ToDictionary(f => f.Name, _ => (string?)"true");
        if (def.Fields.Any(f => f.Name == "Country")) defaults["Country"] = "Saudi Arabia";
        if (def.Fields.Any(f => f.Name == "EmploymentStatus")) defaults["EmploymentStatus"] = "Active";
        return View("Form", await FormAsync(def, null, defaults, null, cancellationToken));
    }

    [HttpPost("{type}/new")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Create(string type, IFormCollection form, CancellationToken cancellationToken)
    {
        var def = _service.Find(type);
        if (def is null || !def.AllowCreate) return NotFound();
        var values = Read(def, form);
        var result = await _service.SaveAsync(type, null, values, null, cancellationToken);
        if (result.IsSuccess)
        {
            TempData["StatusMessage"] = $"{def.Info.SingularTitle} was added.";
            return RedirectToAction(nameof(List), new { type });
        }
        ModelState.AddErrors(result.Errors);
        return View("Form", await FormAsync(def, null, values, null, cancellationToken));
    }

    [HttpGet("{type}/{id:int}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Edit(string type, int id, CancellationToken cancellationToken)
    {
        var def = _service.Find(type);
        if (def is null) return NotFound();
        var record = await _service.GetAsync(type, id, cancellationToken);
        if (record is null) return NotFound();
        return View("Form", await FormAsync(def, id, record.Values, record.RowVersion, cancellationToken));
    }

    [HttpPost("{type}/{id:int}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Edit(string type, int id, IFormCollection form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var def = _service.Find(type);
        if (def is null) return NotFound();
        var values = Read(def, form);
        string? rowVersion = form["RowVersion"];
        var result = await _service.SaveAsync(type, id, values, rowVersion, cancellationToken);
        if (result.IsSuccess)
        {
            TempData["StatusMessage"] = "Changes were saved.";
            return RedirectToAction(nameof(List), new { type });
        }
        ModelState.AddErrors(result.Errors);
        var current = await _service.GetAsync(type, id, cancellationToken);
        return View("Form", await FormAsync(def, id, values, current?.RowVersion ?? rowVersion, cancellationToken));
    }

    [HttpPost("{type}/{id:int}/delete")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Delete(string type, int id, string? rowVersion, CancellationToken cancellationToken)
    {
        if (_service.Find(type) is null) return NotFound();
        var result = await _service.DeleteAsync(type, id, rowVersion, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "The record was deleted." : result.FirstError!.Message;
        return RedirectToAction(nameof(List), new { type });
    }

    private static Dictionary<string, string?> Read(IMasterDataDefinition def, IFormCollection form) =>
        def.Fields.ToDictionary(f => f.Name, f => (string?)form[f.Name].ToString(), StringComparer.Ordinal);

    private async Task<MasterDataFormViewModel> FormAsync(IMasterDataDefinition def, int? id, IReadOnlyDictionary<string, string?> values,
        string? rowVersion, CancellationToken ct)
    {
        var options = (await _service.GetFormOptionsAsync(def.Info.Key, ct))
            .ToDictionary(o => o.Key, o => o.Options, StringComparer.Ordinal);
        return new MasterDataFormViewModel
        {
            Type = def.Info,
            Fields = def.Fields,
            Values = values,
            Options = options,
            Id = id,
            RowVersion = rowVersion
        };
    }
}
