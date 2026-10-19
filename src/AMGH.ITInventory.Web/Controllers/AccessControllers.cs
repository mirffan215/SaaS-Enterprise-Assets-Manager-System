using System.Globalization;
using System.Text;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Roles and permissions of the signed-in company.</summary>
[HasPermission(Permissions.Administration.RolesManage)]
[Route("admin/roles")]
public sealed class RolesController : Controller
{
    private readonly IRoleAdministrationService _roles;

    public RolesController(IRoleAdministrationService roles) => _roles = roles;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _roles.ListAsync(ct));

    [HttpGet("new")]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Edit", new RoleEditViewModel { Form = new RoleForm(), Catalog = await _roles.CatalogAsync(ct) });

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var r = await _roles.GetAsync(id, ct);
        if (r is null) return NotFound();
        return View("Edit", new RoleEditViewModel
        {
            RoleId = id, IsSystemRole = r.Value.Role.IsSystemRole, Members = r.Value.Role.Members, Catalog = await _roles.CatalogAsync(ct),
            Form = new RoleForm { RoleName = r.Value.Role.RoleName, Description = r.Value.Role.Description, IsActive = r.Value.Role.IsActive, Permissions = r.Value.Permissions.ToList() }
        });
    }

    [HttpPost("save/{id:int?}")]
    public async Task<IActionResult> Save(int? id, [Bind(Prefix = "Form")] RoleForm form, CancellationToken ct)
    {
        var result = await _roles.SaveAsync(id, form, ct);
        if (result.IsSuccess)
        {
            TempData["StatusMessage"] = "Role saved. People with this role get the new permissions within a few minutes.";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddErrors(result.Errors.Select(e => e with { Code = e.Code is "validation" or "conflict" ? string.Empty : "Form." + e.Code }));
        var existing = id is { } rid ? await _roles.GetAsync(rid, ct) : null;
        return View("Edit", new RoleEditViewModel { RoleId = id, IsSystemRole = existing?.Role.IsSystemRole ?? false, Members = existing?.Role.Members ?? 0,
            Catalog = await _roles.CatalogAsync(ct), Form = form });
    }
}

/// <summary>People and their access in every company (platform administrators only).</summary>
[Authorize(Policy = PlatformPolicies.SuperAdministrator)]
[Route("platform/users")]
public sealed class PlatformUsersController : Controller
{
    private readonly IUserAccessService _access;

    public PlatformUsersController(IUserAccessService access) => _access = access;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        ViewData["Query"] = q;
        return View(await _access.SearchAsync(q, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Access(int id, CancellationToken ct)
    {
        var a = await _access.GetAsync(id, ct);
        return a is null ? NotFound() : View(a);
    }

    [HttpPost("{id:int}/membership")]
    public async Task<IActionResult> Membership(int id, MembershipForm form, CancellationToken ct)
    {
        var r = await _access.SaveMembershipAsync(id, form, ct);
        TempData["StatusMessage"] = r.IsSuccess ? "Access saved. The person's open sessions are re-checked within a few minutes." : string.Join(" ", r.Errors.Select(e => e.Message));
        return RedirectToAction(nameof(Access), new { id });
    }

    [HttpGet("/platform/access-review")]
    public async Task<IActionResult> Review(string? format, CancellationToken ct)
    {
        var rows = await _access.ReviewAsync(ct);
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) return View(rows);
        static string Cell(string? v)
        {
            v ??= string.Empty;
            if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@') v = "'" + v;         // spreadsheet formula guard
            return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;
        }
        var sb = new StringBuilder("Company,Company name,User,Name,E-mail,User active,Membership active,Default,Platform administrator,Roles,Last sign-in (UTC)\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", new[] { r.CompanyCode, r.CompanyName, r.Username, r.DisplayName, r.Email, r.UserActive ? "Yes" : "No", r.MembershipActive ? "Yes" : "No",
                r.IsDefault ? "Yes" : "No", r.IsPlatformAdmin ? "Yes" : "No", r.Roles, r.LastLoginUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) }.Select(Cell))).Append('\n');
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv; charset=utf-8",
            $"access-review_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }
}
