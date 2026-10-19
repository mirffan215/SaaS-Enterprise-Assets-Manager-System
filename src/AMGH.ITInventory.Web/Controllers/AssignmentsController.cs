using AMGH.ITInventory.Application.Assignments;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Hand-over (assign), return and acknowledgement of assets.</summary>
[HasPermission(Permissions.Assignments.View)]
[Route("assets/{id:int}")]
public sealed class AssignmentsController : Controller
{
    private readonly IAssignmentService _assignments;
    private readonly ILogger<AssignmentsController> _logger;

    public AssignmentsController(IAssignmentService assignments, ILogger<AssignmentsController> logger)
    {
        _assignments = assignments;
        _logger = logger;
    }

    [HttpGet("assign")]
    [HasPermission(Permissions.Assignments.Assign)]
    public async Task<IActionResult> Assign(int id, CancellationToken cancellationToken)
    {
        var ctx = await _assignments.GetContextAsync(id, cancellationToken);
        if (ctx is null) return NotFound();
        if (!ctx.CanAssign)
        {
            TempData["StatusMessage"] = ctx.Current is not null
                ? "This asset is already assigned. Return it first."
                : $"An asset with status '{ctx.StatusName}' cannot be assigned. Set it to In stock first.";
            return RedirectToAction("Details", "Assets", new { id });
        }
        return View(new AssignViewModel { Context = ctx });
    }

    [HttpPost("assign")]
    [HasPermission(Permissions.Assignments.Assign)]
    public async Task<IActionResult> Assign(int id, [Bind(Prefix = "Form")] AssignForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var result = ModelState.IsValid ? await _assignments.AssignAsync(id, form, cancellationToken) : null;
        if (result is { IsSuccess: true })
        {
            _logger.LogInformation("Asset {AssetId} assigned by {User}", id, User.Identity?.Name);
            TempData["StatusMessage"] = "The asset was assigned. Print the hand-over form for signature (Assignments tab).";
            return RedirectToAction("Details", "Assets", new { id, tab = "assignments" });
        }
        if (result is not null) ModelState.AddErrors(result.Errors.Select(e => e with { Code = Prefixed(e.Code) }));
        var ctx = await _assignments.GetContextAsync(id, cancellationToken);
        return ctx is null ? NotFound() : View(new AssignViewModel { Context = ctx, Form = form });
    }

    [HttpGet("return")]
    [HasPermission(Permissions.Assignments.Return)]
    public async Task<IActionResult> Return(int id, CancellationToken cancellationToken)
    {
        var ctx = await _assignments.GetContextAsync(id, cancellationToken);
        if (ctx is null) return NotFound();
        if (ctx.Current is null)
        {
            TempData["StatusMessage"] = "This asset is not assigned to anyone.";
            return RedirectToAction("Details", "Assets", new { id });
        }
        return View(new ReturnViewModel { Context = ctx });
    }

    [HttpPost("return")]
    [HasPermission(Permissions.Assignments.Return)]
    public async Task<IActionResult> Return(int id, [Bind(Prefix = "Form")] ReturnForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var result = await _assignments.ReturnAsync(id, form, cancellationToken);
        if (result.IsSuccess)
        {
            _logger.LogInformation("Asset {AssetId} returned, recorded by {User}", id, User.Identity?.Name);
            TempData["StatusMessage"] = "The return was recorded.";
            return RedirectToAction("Details", "Assets", new { id, tab = "assignments" });
        }
        ModelState.AddErrors(result.Errors.Select(e => e with { Code = Prefixed(e.Code) }));
        var ctx = await _assignments.GetContextAsync(id, cancellationToken);
        return ctx is null ? NotFound() : View(new ReturnViewModel { Context = ctx, Form = form });
    }

    [HttpPost("assignments/{assignmentId:int}/acknowledge")]
    [HasPermission(Permissions.Assignments.Assign)]
    public async Task<IActionResult> Acknowledge(int id, int assignmentId, CancellationToken cancellationToken)
    {
        var result = await _assignments.AcknowledgeAsync(id, assignmentId, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess
            ? "Acknowledgement recorded. Attach the signed form under Documents (type: Hand-over form)."
            : result.FirstError!.Message;
        return RedirectToAction("Details", "Assets", new { id, tab = "assignments" });
    }

    [HttpGet("assignments/{assignmentId:int}/handover")]
    public async Task<IActionResult> Handover(int id, int assignmentId, CancellationToken cancellationToken)
    {
        var doc = await _assignments.GetHandoverAsync(id, assignmentId, cancellationToken);
        return doc is null ? NotFound() : View(doc);
    }

    private static string Prefixed(string code) => code.Length > 0 && char.IsUpper(code[0]) ? "Form." + code : code;
}
