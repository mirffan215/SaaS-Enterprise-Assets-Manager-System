using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

[HasPermission(Permissions.Administration.UsersView)]
public sealed class UsersController : Controller
{
    private readonly IUserAdministrationService _users;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserAdministrationService users, ILogger<UsersController> logger)
    {
        _users = users;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new UserListViewModel
        {
            Users = await _users.ListUsersAsync(cancellationToken),
            CanManage = User.HasPermission(Permissions.Administration.UsersManage)
        });

    [HttpGet]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View("Form", new UserFormViewModel { AvailableRoles = await _users.ListRolesAsync(cancellationToken) });

    [HttpPost]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            var result = await RunAsync(() => _users.CreateUserAsync(
                new CreateUserRequest(model.Username, model.DisplayName, model.Email, model.EmployeeId, model.RoleIds), cancellationToken));
            if (result is { IsSuccess: true })
            {
                _logger.LogInformation("User {NewUser} created by {Admin}", result.Value.Username, User.Identity?.Name);
                return TemporaryPasswordView(result.Value, isNewUser: true);
            }
            if (result is not null) ModelState.AddErrors(result.Errors);
        }
        model.AvailableRoles = await _users.ListRolesAsync(cancellationToken);
        return View("Form", model);
    }

    [HttpGet]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var details = await _users.GetUserAsync(id, cancellationToken);
        if (details is null) return NotFound();
        return View("Form", await ToFormAsync(details, cancellationToken));
    }

    [BlockInDemo]
    [HttpPost]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> Edit(int id, UserFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ModelState.Remove(nameof(UserFormViewModel.Username)); // username cannot change
        if (ModelState.IsValid)
        {
            byte[] rowVersion;
            try { rowVersion = Convert.FromBase64String(model.RowVersion ?? string.Empty); }
            catch (FormatException) { return BadRequest(); }

            var result = await RunAsync(() => _users.UpdateUserAsync(new UpdateUserRequest(id, model.DisplayName, model.Email,
                model.EmployeeId, model.IsActive, model.RoleIds, rowVersion), cancellationToken));
            if (result is { IsSuccess: true })
            {
                TempData["StatusMessage"] = $"Changes to {model.DisplayName} were saved.";
                return RedirectToAction(nameof(Index));
            }
            if (result is not null) ModelState.AddErrors(result.Errors);
        }

        var details = await _users.GetUserAsync(id, cancellationToken);
        if (details is null) return NotFound();
        var form = await ToFormAsync(details, cancellationToken);
        form.DisplayName = model.DisplayName;
        form.Email = model.Email;
        form.EmployeeId = model.EmployeeId;
        form.IsActive = model.IsActive;
        form.RoleIds = model.RoleIds;
        return View("Form", form);
    }

    [HttpPost]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken)
    {
        var result = await _users.UnlockAsync(id, cancellationToken);
        TempData["StatusMessage"] = result.IsSuccess ? "The account was unlocked." : result.FirstError!.Message;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [BlockInDemo]
    [HttpPost]
    [HasPermission(Permissions.Administration.UsersManage)]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken cancellationToken)
    {
        var result = await _users.ResetPasswordAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["StatusMessage"] = result.FirstError!.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }
        _logger.LogInformation("Password of user {Target} reset by {Admin}", result.Value.Username, User.Identity?.Name);
        return TemporaryPasswordView(result.Value, isNewUser: false);
    }

    /// <summary>Shown once, never redirected or stored, and never cached.</summary>
    private ViewResult TemporaryPasswordView(TemporaryPasswordResult value, bool isNewUser)
    {
        Response.Headers.CacheControl = "no-store";
        return View("TemporaryPassword", new TemporaryPasswordViewModel
        {
            UserId = value.UserId,
            Username = value.Username,
            TemporaryPassword = value.TemporaryPassword,
            IsNewUser = isNewUser
        });
    }

    private async Task<UserFormViewModel> ToFormAsync(UserDetails d, CancellationToken cancellationToken) => new()
    {
        UserId = d.UserId,
        Username = d.Username,
        DisplayName = d.DisplayName,
        Email = d.Email,
        EmployeeId = d.EmployeeId,
        IsActive = d.IsActive,
        RoleIds = d.RoleIds.ToList(),
        RowVersion = Convert.ToBase64String(d.RowVersion),
        AvailableRoles = await _users.ListRolesAsync(cancellationToken),
        IsLockedOut = d.IsLockedOut,
        LockoutEndUtc = d.LockoutEndUtc,
        MustChangePassword = d.MustChangePassword,
        LastLoginDateUtc = d.LastLoginDateUtc,
        LastLoginIp = d.LastLoginIp,
        CreatedDateUtc = d.CreatedDateUtc,
        CreatedBy = d.CreatedBy
    };

    /// <summary>Turns expected database rejections into form errors instead of an error page.</summary>
    private async Task<T?> RunAsync<T>(Func<Task<T>> action) where T : class
    {
        try
        {
            return await action();
        }
        catch (ConcurrencyConflictException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (DuplicateValueException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (BusinessRuleException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        return null;
    }
}
