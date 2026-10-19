using AMGH.ITInventory.Application.Dashboard;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Executive dashboard of the current company.</summary>
[HasPermission(Permissions.Dashboard.View)]
[Route("dashboard")]
public sealed class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ILookupService _lookups;

    public DashboardController(IDashboardService dashboard, ILookupService lookups)
    {
        _dashboard = dashboard;
        _lookups = lookups;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] DashboardFilter filter, CancellationToken cancellationToken)
    {
        filter ??= new DashboardFilter();
        if (filter.From is { } f && filter.To is { } t && f > t) (filter.From, filter.To) = (t, f);
        var data = await _dashboard.GetAsync(filter, User.HasPermission(Permissions.Assets.ViewFinancials),
            User.HasPermission(Permissions.Assets.ViewSecurityDetails), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return View(new DashboardViewModel
        {
            Filter = filter,
            Data = data,
            Departments = await _lookups.GetOptionsAsync(LookupKeys.Departments, cancellationToken),
            Buildings = await _lookups.GetOptionsAsync(LookupKeys.BuildingsWithLocation, cancellationToken),
            Categories = await _lookups.GetOptionsAsync(LookupKeys.Categories, cancellationToken),
            Statuses = await _lookups.GetOptionsAsync(LookupKeys.Statuses, cancellationToken)
        });
    }
}
