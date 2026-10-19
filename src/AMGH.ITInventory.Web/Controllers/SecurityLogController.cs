using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Shared.Security;
using AMGH.ITInventory.Web.Infrastructure;
using AMGH.ITInventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AMGH.ITInventory.Web.Controllers;

/// <summary>Sign-in history for security monitoring (ISO 27001 A.8.15 / A.8.16).</summary>
[HasPermission(Permissions.Administration.AuditLogsView)]
public sealed class SecurityLogController : Controller
{
    private const int MaxRows = 500;

    private readonly IUserAdministrationService _users;
    private readonly IDisplayTime _time;

    public SecurityLogController(IUserAdministrationService users, IDisplayTime time)
    {
        _users = users;
        _time = time;
    }

    public async Task<IActionResult> Index(LoginHistoryViewModel filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var fromUtc = _time.LocalDateToUtc(filter.From);
        var toUtc = _time.LocalDateToUtc(filter.To?.AddDays(1)); // inclusive "to" date
        if (filter.EventType is not null && !Enum.TryParse<LoginEventType>(filter.EventType, out _)) filter.EventType = null;

        filter.Entries = await _users.GetLoginHistoryAsync(filter.Username, filter.EventType, fromUtc, toUtc, MaxRows, cancellationToken);
        filter.MaxRows = MaxRows;
        return View(filter);
    }
}
