using AMGH.ITInventory.Application.Tenancy;
using System.Globalization;
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
    private const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

    private readonly ITenantBackupService _backups;
    private readonly ITenantAccessor _tenants;
    private readonly IBackupArchive _archive;
    private readonly BackupScheduleStore _schedules;

    public CompanyBackupController(ITenantBackupService backups, ITenantAccessor tenants, IBackupArchive archive, BackupScheduleStore schedules)
    {
        _backups = backups;
        _tenants = tenants;
        _archive = archive;
        _schedules = schedules;
    }

    /// <summary>At least 12 characters with three of the four kinds (upper, lower, digit, symbol); null when it is acceptable.</summary>
    internal static string? PasswordProblem(string? password)
    {
        if (password is null || password.Length < 12) return "Choose a password of at least 12 characters for the zip file.";
        var kinds = (password.Any(char.IsUpper) ? 1 : 0) + (password.Any(char.IsLower) ? 1 : 0) + (password.Any(char.IsDigit) ? 1 : 0) + (password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        return kinds < 3 ? "Use at least three of: upper-case letters, lower-case letters, digits, symbols." : null;
    }

    [HttpGet("")]
    public Task<IActionResult> Index() => ViewAsync(null, false);

    [HttpPost("create")]
    public async Task<IActionResult> Create()
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        var result = await _backups.BackupAsync(tenant.Code, cancellationToken: CancellationToken.None);
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

    /// <summary>Downloads one of this workspace's backups as a password-protected zip (AES-256) the person chooses the password for.</summary>
    [HttpPost("download")]
    public async Task<IActionResult> Download(string? file, string? password)
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        if (PasswordProblem(password) is { } problem) return await ViewAsync(problem, false);
        var source = await _backups.OpenAsync(tenant.Code, file ?? string.Empty, HttpContext.RequestAborted);
        if (source is null) return await ViewAsync("That backup is not available.", false);
        await using (source)
        {
            var temp = new FileStream(Path.Combine(Path.GetTempPath(), "eams-dl-" + Guid.NewGuid().ToString("N") + ".zip"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 256 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await _archive.WriteAsync(temp, password!, file!, source, HttpContext.RequestAborted);
                temp.Position = 0;
            }
            catch
            {
                await temp.DisposeAsync();
                throw;
            }
            Response.Headers.CacheControl = "no-store";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(temp, "application/zip", Path.ChangeExtension(file!, ".zip"));
        }
    }

    /// <summary>Restores the workspace from a zip that was downloaded here (needs its password and the workspace code).</summary>
    [HttpPost("upload")]
    [BlockInDemo]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFile? zip, string? password, string? confirm)
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        if (zip is null || zip.Length == 0) return await ViewAsync("Choose the backup zip file.", false);
        if (string.IsNullOrEmpty(password)) return await ViewAsync("Enter the password of the zip file.", false);
        if (!string.Equals(confirm?.Trim(), tenant.Code, StringComparison.OrdinalIgnoreCase))
            return await ViewAsync("Nothing was restored: type the workspace code to confirm.", false);

        await using var stream = zip.OpenReadStream();
        var result = await _backups.RestoreFromZipAsync(tenant.Code, stream, password, CancellationToken.None);
        if (result.IsFailure) return await ViewAsync(result.FirstError!.Message, false);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusMessage"] = "The workspace was restored from the uploaded backup. Sign in again.";
        return RedirectToAction("Login", "Account");
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> Schedule(string? mode, string? time, int day, int keep)
    {
        if (_tenants.CurrentTenant is not { } tenant) return await ViewAsync(null, false);
        if (mode is not (BackupSchedule.Off or BackupSchedule.Daily or BackupSchedule.Weekly)) return await ViewAsync("Choose Off, Daily or Weekly.", false);
        if (!BackupSchedule.TryParseTime(time, out _)) return await ViewAsync("Enter the time as HH:mm, for example 02:00.", false);
        if (day is < 0 or > 6) return await ViewAsync("Choose a day of the week.", false);
        if (keep is < 1 or > 365) return await ViewAsync("Keep between 1 and 365 scheduled backups.", false);
        var schedule = await _schedules.LoadAsync(tenant.Code, HttpContext.RequestAborted);
        schedule.Mode = mode; schedule.Time = time!; schedule.Day = day; schedule.Keep = keep;
        // switching a schedule on does not trigger a backup for a slot that has already passed today
        if (mode != BackupSchedule.Off) schedule.LastRunLocal = DateTime.Now;
        await _schedules.SaveAsync(tenant.Code, schedule, HttpContext.RequestAborted);
        return await ViewAsync(mode == BackupSchedule.Off ? "Scheduled backups are off." : $"Saved: {mode.ToLowerInvariant()} at {time}{(mode == BackupSchedule.Weekly ? " on " + CultureInfo.InvariantCulture.DateTimeFormat.GetDayName((DayOfWeek)day) : string.Empty)}, keeping the last {keep}.", true);
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
        model.Schedule = await _schedules.LoadAsync(tenant.Code, HttpContext.RequestAborted);
        try { model.Files = (await _backups.ListAsync(tenant.Code, HttpContext.RequestAborted)).Select(f => new BackupRow(f.FileName, f.SizeBytes, f.CreatedUtc)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            model.CanList = false;
            model.Message ??= "The backup folder cannot be listed (" + ex.Message.Split('\n')[0] + "). Ask the operator to set up the backup folder.";
        }
        return View("Index", model);
    }
}
