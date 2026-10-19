using System.Diagnostics;
using System.Security.Claims;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly IUnitOfWork _uow;
    private readonly IUserAdministrationService _users;

    public HomeController(IUnitOfWork uow, IUserAdministrationService users)
    {
        _uow = uow;
        _users = users;
    }

    public async Task<IActionResult> Index(bool overview = false, CancellationToken cancellationToken = default)
    {
        // Users who may see the executive dashboard land on it; ?overview=true keeps this page reachable.
        if (!overview && User.HasPermission(Permissions.Dashboard.View)) return RedirectToAction("Index", "Dashboard");

        int? activeUsers = null;
        if (User.HasPermission(Permissions.Administration.UsersView) && User.GetCompanyId() is { } companyId)
            // Users are platform-wide: count this company's active members only.
            activeUsers = await _uow.Repository<CompanyUser>().CountAsync(m => m.CompanyId == companyId && m.IsActive && m.User.IsActive, cancellationToken);

        var model = new HomeViewModel
        {
            DisplayName = User.GetDisplayName(),
            Roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
            PermissionCount = User.FindAll(AppClaimTypes.Permission).Count(),
            AssetCount = User.HasPermission(Permissions.Assets.View)
                ? await _uow.Repository<Asset>().CountAsync(cancellationToken: cancellationToken) : 0,
            LicenseCount = User.HasPermission(Permissions.Licenses.View)
                ? await _uow.Repository<SoftwareLicense>().CountAsync(cancellationToken: cancellationToken) : 0,
            ActiveUserCount = activeUsers,
            // Search is "contains"; keep only this user's own events.
            RecentSignIns = (await _users.GetLoginHistoryAsync(User.Identity?.Name, null, null, null, 25, cancellationToken))
                .Where(e => string.Equals(e.Username, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList()
        };
        return View(model);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier, StatusCode = 500 });

    [AllowAnonymous]
    [Route("Home/Status/{code:int}")]
    public IActionResult Status(int code) =>
        View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier, StatusCode = code });
}
