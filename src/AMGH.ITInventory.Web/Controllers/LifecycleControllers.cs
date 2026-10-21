using System.Security.Claims;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Shared helpers for the lifecycle screens.</summary>
public abstract class LifecycleControllerBase : Controller
{
    protected ApprovalActor Actor => new(User.Identity?.Name ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal),
        User.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal));

    protected IActionResult Done(Result result, string success, string action, object? route = null)
    {
        TempData["StatusMessage"] = result.IsSuccess ? success : string.Join(" ", result.Errors.Select(e => e.Message));
        return RedirectToAction(action, route);
    }

    protected IActionResult ToAsset(int assetId, string tab, string message)
    {
        TempData["StatusMessage"] = message;
        return RedirectToAction("Details", "Assets", new { id = assetId, tab });
    }
}

// ============================================================================ approvals

/// <summary>Approval Center: open to anyone who may view movements or disposals; deciding needs the approve permission (checked per request).</summary>
[Route("approvals")]
public sealed class ApprovalsController : LifecycleControllerBase
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (!User.HasPermission(Permissions.Movements.View) && !User.HasPermission(Permissions.Disposals.View) && !User.HasPermission(Permissions.CompanyAdmin.ExportDataApprove)) context.Result = Forbid();
        base.OnActionExecuting(context);
    }

    private readonly IApprovalService _approvals;
    private readonly IUserAdministrationService _users;

    public ApprovalsController(IApprovalService approvals, IUserAdministrationService users)
    {
        _approvals = approvals;
        _users = users;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View("~/Views/Lifecycle/Approvals.cshtml", await _approvals.PendingAsync(Actor, ct));

    [HttpPost("{type}/{id:int}/approve")]
    public async Task<IActionResult> Approve(string type, int id, string? remarks, CancellationToken ct)
    {
        var r = await _approvals.ApproveAsync(type, id, remarks, Actor, ct);
        TempData["StatusMessage"] = r.IsSuccess ? r.Value : r.FirstError!.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{type}/{id:int}/reject")]
    public async Task<IActionResult> Reject(string type, int id, string reason, CancellationToken ct) =>
        Done(await _approvals.RejectAsync(type, id, reason, Actor, ct), "Request rejected.", nameof(Index));

    [HttpGet("rules")]
    [HasPermission(Permissions.Administration.RolesManage)]
    public async Task<IActionResult> Rules(CancellationToken ct) => View("~/Views/Lifecycle/ApprovalRules.cshtml", new ApprovalRulesViewModel
    {
        Rules = await _approvals.RulesAsync(ct),
        Roles = (await _users.ListRolesAsync(ct)).Select(r => r.RoleName).ToList()
    });

    [HttpPost("rules/{type}")]
    [HasPermission(Permissions.Administration.RolesManage)]
    public async Task<IActionResult> SaveRules(string type, string[] stages, CancellationToken ct) =>
        Done(await _approvals.SaveRulesAsync(type, stages ?? Array.Empty<string>(), ct), $"{type} approval stages saved.", nameof(Rules));
}

// ============================================================================ movements

[HasPermission(Permissions.Movements.View)]
public sealed class MovementsController : LifecycleControllerBase
{
    private readonly IMovementService _moves;
    private readonly IAssetService _assets;
    private readonly ILookupService _lookups;

    public MovementsController(IMovementService moves, IAssetService assets, ILookupService lookups)
    {
        _moves = moves;
        _assets = assets;
        _lookups = lookups;
    }

    [HttpGet("movements")]
    public async Task<IActionResult> Index(string? view, CancellationToken ct)
    {
        WorkflowStatus? status = view switch { "approved" => WorkflowStatus.Approved, "requested" => WorkflowStatus.Requested, _ => null };
        ViewData["View"] = view ?? "all";
        return View("~/Views/Lifecycle/Movements.cshtml", await _moves.ListAsync(null, status, 500, ct));
    }

    [HttpGet("assets/{id:int}/move")]
    [HasPermission(Permissions.Movements.Request)]
    public async Task<IActionResult> RequestMove(int id, CancellationToken ct) => await FormAsync(id, new MovementForm(), ct);

    [HttpPost("assets/{id:int}/move")]
    [HasPermission(Permissions.Movements.Request)]
    public async Task<IActionResult> RequestMove(int id, [Bind(Prefix = "Form")] MovementForm form, CancellationToken ct)
    {
        var r = await _moves.RequestAsync(id, form, ct);
        if (r.IsSuccess) return ToAsset(id, "lifecycle", "Movement requested; it now waits for approval in the Approval Center.");
        ModelState.AddErrors(r.Errors.Select(e => e with { Code = e.Code.Length > 0 ? "Form." + e.Code : e.Code }));
        return await FormAsync(id, form, ct);
    }

    [HttpPost("movements/{movementId:int}/complete")]
    [HasPermission(Permissions.Movements.Complete)]
    public async Task<IActionResult> Complete(int movementId, CancellationToken ct) =>
        Done(await _moves.CompleteAsync(movementId, ct), "Movement completed; the asset's location is updated.", nameof(Index), new { view = "approved" });

    private async Task<IActionResult> FormAsync(int id, MovementForm form, CancellationToken ct)
    {
        var header = await _assets.HeaderAsync(id, ct);
        if (header is null) return NotFound();
        return View("~/Views/Lifecycle/MoveForm.cshtml", new MoveFormViewModel
        {
            Asset = header, Form = form,
            Locations = await _lookups.GetOptionsAsync(LookupKeys.Locations, ct),
            Buildings = await _lookups.GetOptionsAsync(LookupKeys.Buildings, ct),
            Floors = await _lookups.GetOptionsAsync(LookupKeys.Floors, ct),
            Rooms = await _lookups.GetOptionsAsync(LookupKeys.Rooms, ct)
        });
    }
}

// ============================================================================ maintenance

[HasPermission(Permissions.Maintenance.View)]
public sealed class MaintenanceController : LifecycleControllerBase
{
    private readonly IMaintenanceService _maint;
    private readonly IAssetService _assets;
    private readonly ILookupService _lookups;

    public MaintenanceController(IMaintenanceService maint, IAssetService assets, ILookupService lookups)
    {
        _maint = maint;
        _assets = assets;
        _lookups = lookups;
    }

    [HttpGet("maintenance")]
    public async Task<IActionResult> Index(MaintenanceView view = MaintenanceView.Open, bool mine = false, CancellationToken ct = default)
    {
        ViewData["View"] = view;
        var me = User.GetUserId();
        return View("~/Views/Lifecycle/Maintenance.cshtml", new MaintenanceListViewModel
        {
            Jobs = await _maint.ListAsync(null, view, 500, mine ? me ?? -1 : null, ct),
            Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct),
            Technicians = User.HasPermission(Permissions.Maintenance.Manage) ? await _maint.TechniciansAsync(ct) : Array.Empty<TechnicianOption>(),
            Mine = mine,
            CurrentUserId = me,
            CanViewCosts = User.HasPermission(Permissions.Assets.ViewFinancials)
        });
    }

    [HttpGet("assets/{id:int}/maintenance/new")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Create(int id, CancellationToken ct)
    {
        // pre-select the signed-in user when they are one of the technicians (they can change it)
        var me = User.GetUserId();
        var form = new MaintenanceForm();
        if (me is { } uid && (await _maint.TechniciansAsync(ct)).Any(t => t.UserId == uid)) form.AssignedToUserId = uid;
        return await FormAsync(id, form, ct);
    }

    [HttpPost("assets/{id:int}/maintenance/new")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Create(int id, [Bind(Prefix = "Form")] MaintenanceForm form, CancellationToken ct)
    {
        var r = await _maint.ScheduleAsync(id, form, ct);
        if (r.IsSuccess) return ToAsset(id, "lifecycle", "Maintenance scheduled.");
        ModelState.AddErrors(r.Errors.Select(e => e with { Code = e.Code.Length > 0 ? "Form." + e.Code : e.Code }));
        return await FormAsync(id, form, ct);
    }

    [HttpPost("maintenance/{jobId:int}/start")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Start(int jobId, bool takeOutOfService, CancellationToken ct) =>
        Done(await _maint.StartAsync(jobId, takeOutOfService, ct), takeOutOfService ? "Job started; the asset is Under Maintenance." : "Job started.", nameof(Index));

    [HttpPost("maintenance/{jobId:int}/complete")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Complete(int jobId, MaintenanceCompletionForm form, CancellationToken ct) =>
        Done(await _maint.CompleteAsync(jobId, form, ct), "Job completed.", nameof(Index));

    [HttpPost("maintenance/{jobId:int}/cost")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Cost(int jobId, MaintenanceCostForm form, CancellationToken ct) =>
        Done(await _maint.UpdateCostAsync(jobId, form, ct), "Cost saved.", nameof(Index));

    [HttpPost("maintenance/{jobId:int}/assign")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Assign(int jobId, int? assignedToUserId, CancellationToken ct) =>
        Done(await _maint.AssignAsync(jobId, assignedToUserId, ct), assignedToUserId is null ? "Job unassigned." : "Job assigned.", nameof(Index));

    [HttpPost("maintenance/{jobId:int}/cancel")]
    [HasPermission(Permissions.Maintenance.Manage)]
    public async Task<IActionResult> Cancel(int jobId, string reason, CancellationToken ct) =>
        Done(await _maint.CancelAsync(jobId, reason, ct), "Job cancelled.", nameof(Index));

    private async Task<IActionResult> FormAsync(int id, MaintenanceForm form, CancellationToken ct)
    {
        var header = await _assets.HeaderAsync(id, ct);
        if (header is null) return NotFound();
        return View("~/Views/Lifecycle/MaintenanceForm.cshtml", new MaintenanceFormViewModel
        {
            Asset = header, Form = form, Vendors = await _lookups.GetOptionsAsync(LookupKeys.Vendors, ct),
            Technicians = await _maint.TechniciansAsync(ct), Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct)
        });
    }
}

// ============================================================================ physical verification

[HasPermission(Permissions.Audits.View)]
[Route("verification")]
public sealed class VerificationController : LifecycleControllerBase
{
    private readonly IVerificationService _verify;
    private readonly ILookupService _lookups;

    public VerificationController(IVerificationService verify, ILookupService lookups)
    {
        _verify = verify;
        _lookups = lookups;
    }

    /// <summary>Scan page: cycle, scan box (camera or handheld scanner), the scanned asset with result buttons, and progress.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? cycle, string? scan, bool camera, CancellationToken ct)
    {
        cycle = string.IsNullOrWhiteSpace(cycle) ? _verify.CurrentCycle() : cycle.Trim();
        var target = string.IsNullOrWhiteSpace(scan) ? null : await _verify.FindAsync(scan, cycle, ct);
        return View("~/Views/Lifecycle/Verification.cshtml", new VerificationViewModel
        {
            Cycle = cycle, Scan = scan, Target = target, NotFound = !string.IsNullOrWhiteSpace(scan) && target is null, ScanMethod = camera ? ScanMethod.QrScan : ScanMethod.Manual,
            Progress = await _verify.ProgressAsync(cycle, 100, ct),
            Locations = await _lookups.GetOptionsAsync(LookupKeys.Locations, ct),
            Rooms = await _lookups.GetOptionsAsync(LookupKeys.Rooms, ct),
            Employees = await _lookups.GetOptionsAsync(LookupKeys.Employees, ct),
            CanPerform = User.HasPermission(Permissions.Audits.Perform)
        });
    }

    [HttpPost("{assetId:int}")]
    [HasPermission(Permissions.Audits.Perform)]
    public async Task<IActionResult> Record(int assetId, VerificationForm form, CancellationToken ct)
    {
        var r = await _verify.RecordAsync(assetId, form, ct);
        TempData["StatusMessage"] = r.IsSuccess ? $"Recorded: {Application.Common.DisplayText.Humanize(form.Result.ToString())}. Scan the next asset." : r.FirstError!.Message;
        return RedirectToAction(nameof(Index), new { cycle = form.Cycle });
    }

    [HttpGet("exceptions")]
    public async Task<IActionResult> Exceptions(CancellationToken ct) => View("~/Views/Lifecycle/VerificationExceptions.cshtml", await _verify.ExceptionsAsync(ct));

    [HttpPost("exceptions/{auditId:int}/resolve")]
    [HasPermission(Permissions.Audits.Perform)]
    public async Task<IActionResult> Resolve(int auditId, string correctiveAction, CancellationToken ct) =>
        Done(await _verify.ResolveAsync(auditId, correctiveAction, ct), "Exception resolved.", nameof(Exceptions));
}

// ============================================================================ disposal

[HasPermission(Permissions.Disposals.View)]
public sealed class DisposalsController : LifecycleControllerBase
{
    private readonly IDisposalService _disposals;
    private readonly IAssetService _assets;
    private readonly ILookupService _lookups;

    public DisposalsController(IDisposalService disposals, IAssetService assets, ILookupService lookups)
    {
        _disposals = disposals;
        _assets = assets;
        _lookups = lookups;
    }

    [HttpGet("disposals")]
    public async Task<IActionResult> Index(CancellationToken ct) => View("~/Views/Lifecycle/Disposals.cshtml", await _disposals.ListAsync(null, null, 500, ct));

    [HttpGet("assets/{id:int}/dispose")]
    [HasPermission(Permissions.Disposals.Request)]
    public async Task<IActionResult> RequestDisposal(int id, CancellationToken ct) => await RequestFormAsync(id, null, null, ct);

    [HttpPost("assets/{id:int}/dispose")]
    [HasPermission(Permissions.Disposals.Request)]
    public async Task<IActionResult> RequestDisposal(int id, int disposalMethodId, string reason, CancellationToken ct)
    {
        var r = await _disposals.RequestAsync(id, disposalMethodId, reason, ct);
        if (r.IsSuccess) return ToAsset(id, "lifecycle", "Disposal requested; the asset is Pending Disposal until the request is decided.");
        ModelState.AddErrors(r.Errors);
        return await RequestFormAsync(id, disposalMethodId, reason, ct);
    }

    [HttpGet("disposals/{disposalId:int}/complete")]
    [HasPermission(Permissions.Disposals.Complete)]
    public async Task<IActionResult> Complete(int disposalId, CancellationToken ct) => await CompleteFormAsync(disposalId, new DisposalCompletionForm(), ct);

    [HttpPost("disposals/{disposalId:int}/complete")]
    [HasPermission(Permissions.Disposals.Complete)]
    public async Task<IActionResult> Complete(int disposalId, [Bind(Prefix = "Form")] DisposalCompletionForm form, CancellationToken ct)
    {
        var r = await _disposals.CompleteAsync(disposalId, form, ct);
        if (r.IsSuccess)
        {
            TempData["StatusMessage"] = "Disposal completed. Attach the certificate under the asset's Documents.";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddErrors(r.Errors.Select(e => e with { Code = e.Code.Length > 0 ? "Form." + e.Code : e.Code }));
        return await CompleteFormAsync(disposalId, form, ct);
    }

    private async Task<IActionResult> RequestFormAsync(int id, int? method, string? reason, CancellationToken ct)
    {
        var header = await _assets.HeaderAsync(id, ct);
        if (header is null) return NotFound();
        return View("~/Views/Lifecycle/DisposeForm.cshtml", new DisposeFormViewModel
        {
            Asset = header, MethodId = method, Reason = reason, Methods = await _lookups.GetOptionsAsync(LookupKeys.DisposalMethods, ct)
        });
    }

    private async Task<IActionResult> CompleteFormAsync(int disposalId, DisposalCompletionForm form, CancellationToken ct)
    {
        var item = (await _disposals.ListAsync(null, null, 5000, ct)).FirstOrDefault(d => d.AssetDisposalId == disposalId);
        if (item is null) return NotFound();
        return View("~/Views/Lifecycle/DisposalComplete.cshtml", new DisposalCompleteViewModel
        {
            Disposal = item, Form = form, Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct)
        });
    }
}

// ============================================================================ warranties

[HasPermission(Permissions.Warranties.View)]
public sealed class WarrantiesController : LifecycleControllerBase
{
    private readonly IWarrantyService _warranties;
    private readonly IAssetService _assets;
    private readonly ILookupService _lookups;

    public WarrantiesController(IWarrantyService warranties, IAssetService assets, ILookupService lookups)
    {
        _warranties = warranties;
        _assets = assets;
        _lookups = lookups;
    }

    [HttpGet("warranties")]
    public async Task<IActionResult> Index(string? state, CancellationToken ct)
    {
        ViewData["State"] = state;
        return View("~/Views/Lifecycle/Warranties.cshtml", await _warranties.ListAsync(null, state, 1000, ct));
    }

    [HttpGet("assets/{id:int}/warranties/{warrantyId:int?}")]
    [HasPermission(Permissions.Warranties.Manage)]
    public async Task<IActionResult> Edit(int id, int? warrantyId, CancellationToken ct)
    {
        var form = warrantyId is { } w ? await _warranties.GetAsync(w, ct) : new WarrantyForm { StartDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        return form is null ? NotFound() : await FormAsync(id, warrantyId, form, ct);
    }

    [HttpPost("assets/{id:int}/warranties/{warrantyId:int?}")]
    [HasPermission(Permissions.Warranties.Manage)]
    public async Task<IActionResult> Edit(int id, int? warrantyId, [Bind(Prefix = "Form")] WarrantyForm form, CancellationToken ct)
    {
        var r = await _warranties.SaveAsync(id, warrantyId, form, ct);
        if (r.IsSuccess) return ToAsset(id, "lifecycle", "Warranty saved.");
        ModelState.AddErrors(r.Errors.Select(e => e with { Code = e.Code.Length > 0 ? "Form." + e.Code : e.Code }));
        return await FormAsync(id, warrantyId, form, ct);
    }

    [HttpPost("assets/{id:int}/warranties/{warrantyId:int}/void")]
    [HasPermission(Permissions.Warranties.Manage)]
    public async Task<IActionResult> Void(int id, int warrantyId, CancellationToken ct)
    {
        var r = await _warranties.VoidAsync(warrantyId, ct);
        return ToAsset(id, "lifecycle", r.IsSuccess ? "Warranty voided (kept in the audit trail)." : r.FirstError!.Message);
    }

    private async Task<IActionResult> FormAsync(int id, int? warrantyId, WarrantyForm form, CancellationToken ct)
    {
        var header = await _assets.HeaderAsync(id, ct);
        if (header is null) return NotFound();
        return View("~/Views/Lifecycle/WarrantyForm.cshtml", new WarrantyFormViewModel
        {
            Asset = header, WarrantyId = warrantyId, Form = form,
            Vendors = await _lookups.GetOptionsAsync(LookupKeys.Vendors, ct), Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct)
        });
    }
}

// ============================================================================ software licences

[HasPermission(Permissions.Licenses.View)]
[Route("licenses")]
public sealed class LicensesController : LifecycleControllerBase
{
    private readonly ILicenseService _licenses;
    private readonly IAssetService _assets;
    private readonly ILookupService _lookups;
    private readonly ILogger<LicensesController> _logger;

    public LicensesController(ILicenseService licenses, IAssetService assets, ILookupService lookups, ILogger<LicensesController> logger)
    {
        _licenses = licenses;
        _assets = assets;
        _lookups = lookups;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(bool inactive, CancellationToken ct) => View("~/Views/Lifecycle/Licenses.cshtml", await _licenses.ListAsync(inactive, ct));

    [HttpGet("{licenseId:int}")]
    public async Task<IActionResult> Details(int licenseId, CancellationToken ct)
    {
        var item = await _licenses.GetItemAsync(licenseId, ct);
        if (item is null) return NotFound();
        return View("~/Views/Lifecycle/LicenseDetails.cshtml", new LicenseDetailsViewModel
        {
            License = item, Allocations = await _licenses.AllocationsAsync(licenseId, null, ct),
            Employees = await _lookups.GetOptionsAsync(LookupKeys.Employees, ct)
        });
    }

    [HttpGet("new")]
    [HasPermission(Permissions.Licenses.Manage)]
    public async Task<IActionResult> Create(CancellationToken ct) => await FormAsync(null, new LicenseForm(), ct);

    [HttpGet("{licenseId:int}/edit")]
    [HasPermission(Permissions.Licenses.Manage)]
    public async Task<IActionResult> Edit(int licenseId, CancellationToken ct)
    {
        var form = await _licenses.GetFormAsync(licenseId, ct);
        return form is null ? NotFound() : await FormAsync(licenseId, form, ct);
    }

    [HttpPost("save/{licenseId:int?}")]
    [HasPermission(Permissions.Licenses.Manage)]
    public async Task<IActionResult> Save(int? licenseId, [Bind(Prefix = "Form")] LicenseForm form, CancellationToken ct)
    {
        var r = await _licenses.SaveAsync(licenseId, form, ct);
        if (r.IsSuccess)
        {
            TempData["StatusMessage"] = "Licence saved.";
            return RedirectToAction(nameof(Details), new { licenseId = r.Value });
        }
        form.LicenseKey = null; // never echo a key back into the page
        ModelState.AddErrors(r.Errors.Select(e => e with { Code = e.Code.Length > 0 ? "Form." + e.Code : e.Code }));
        return await FormAsync(licenseId, form, ct);
    }

    [HttpPost("{licenseId:int}/allocate")]
    [HasPermission(Permissions.Licenses.Manage)]
    public async Task<IActionResult> Allocate(int licenseId, string? assetNumber, int? employeeId, string? notes, CancellationToken ct)
    {
        int? assetId = null;
        if (!string.IsNullOrWhiteSpace(assetNumber))
        {
            assetId = await _assets.FindIdByNumberAsync(ScanText.AssetNumber(assetNumber) ?? assetNumber, ct);
            if (assetId is null)
            {
                TempData["StatusMessage"] = $"No asset with number {assetNumber.Trim()}.";
                return RedirectToAction(nameof(Details), new { licenseId });
            }
        }
        return Done(await _licenses.AllocateAsync(licenseId, assetId, employeeId, notes, ct), "Seat allocated.", nameof(Details), new { licenseId });
    }

    [HttpPost("{licenseId:int}/allocations/{allocationId:int}/revoke")]
    [HasPermission(Permissions.Licenses.Manage)]
    public async Task<IActionResult> Revoke(int licenseId, int allocationId, CancellationToken ct) =>
        Done(await _licenses.RevokeAsync(allocationId, ct), "Seat released.", nameof(Details), new { licenseId });

    /// <summary>Shows the decrypted key once (not cached); the database records who revealed it and when.</summary>
    [HttpPost("{licenseId:int}/key")]
    [HasPermission(Permissions.Licenses.ViewKeys)]
    public async Task<IActionResult> RevealKey(int licenseId, CancellationToken ct)
    {
        var r = await _licenses.RevealKeyAsync(licenseId, ct);
        if (r.IsFailure) return Done(Result.Failure(r.Errors.ToArray()), string.Empty, nameof(Details), new { licenseId });
        _logger.LogWarning("Licence key {LicenseId} revealed by {User}", licenseId, User.Identity?.Name);
        Response.Headers.CacheControl = "no-store";
        var item = await _licenses.GetItemAsync(licenseId, ct);
        return View("~/Views/Lifecycle/LicenseKey.cshtml", new LicenseKeyViewModel { License = item!, Key = r.Value });
    }

    private async Task<IActionResult> FormAsync(int? licenseId, LicenseForm form, CancellationToken ct) =>
        View("~/Views/Lifecycle/LicenseForm.cshtml", new LicenseFormViewModel
        {
            LicenseId = licenseId, Form = form,
            Publishers = await _lookups.GetOptionsAsync(LookupKeys.Manufacturers, ct), Vendors = await _lookups.GetOptionsAsync(LookupKeys.Vendors, ct),
            Currencies = await _lookups.GetOptionsAsync(LookupKeys.Currencies, ct), Employees = await _lookups.GetOptionsAsync(LookupKeys.Employees, ct)
        });
}
