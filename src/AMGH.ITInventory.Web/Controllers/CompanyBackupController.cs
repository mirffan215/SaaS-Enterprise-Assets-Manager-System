using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Security.Platform;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>
/// Backup and restore of the whole workspace (all its companies) by the workspace's Platform Super Administrator: Administration, Backup and restore.
/// The same service as the operator page; here it always works on the signed-in workspace and nothing else.
/// </summary>
[Route("admin/backup")]
[Authorize(Policy = PlatformPolicies.SuperAdministrator)]
public sealed class CompanyBackupController : Controller
{
    private readonly ITenantBackupService _backups;
    private readonly ITenantAccessor _tenants;

    public CompanyBackupController(ITenantBackupService backups, ITenantAccessor tenants)
    {
        _backups = backups;
        _tenants = tenants;
    }

    [HttpGet("")]
    public Task<IActionResult> Index() => ViewAsync(null, false);

    [HttpPost("create")]
    public async Task<IActionResult> Create()
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        var result = await _backups.BackupAsync(tenant.Code, CancellationToken.None);
        return await ViewAsync(result.IsSuccess ? $"Backup created: {result.Value.FileName}" : result.FirstError!.Message, result.IsSuccess);
    }

    [HttpPost("restore")]
    [BlockInDemo]
    public async Task<IActionResult> Restore(string? file, string? confirm)
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        // replacing all the data of the workspace: its code must be typed
        if (!string.Equals(confirm?.Trim(), tenant.Code, StringComparison.OrdinalIgnoreCase))
            return await ViewAsync("Nothing was restored: type the workspace code to confirm.", false);
        var result = await _backups.RestoreAsync(tenant.Code, file ?? string.Empty, CancellationToken.None);
        if (result.IsFailure) return await ViewAsync(result.FirstError!.Message, false);

        // the data is back as it was at backup time (users, roles and sessions included): everybody signs in again
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusMessage"] = $"The workspace was restored from {file}. Sign in again.";
        return RedirectToAction("Login", "Account");
    }

    private async Task<IActionResult> ViewAsync(string? message, bool succeeded)
    {
        var tenant = _tenants.CurrentTenant;
        var model = new TenantBackupsViewModel { Code = tenant?.Code ?? string.Empty, Name = tenant?.Name ?? string.Empty, Message = message, Succeeded = succeeded };
        if (tenant is null)
        {
            model.CanList = false;
            model.Message = "Backups of workspace databases are available in a multi-tenant installation.";
            return View("Index", model);
        }
        try { model.Files = (await _backups.ListAsync(tenant.Code, HttpContext.RequestAborted)).Select(f => new BackupRow(f.FileName, f.SizeBytes, f.CreatedUtc)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            model.CanList = false;
            model.Message ??= "The backup folder cannot be listed (" + ex.Message.Split('\n')[0] + "). Ask the operator to set up the backup folder.";
        }
        return View("Index", model);
    }
}
