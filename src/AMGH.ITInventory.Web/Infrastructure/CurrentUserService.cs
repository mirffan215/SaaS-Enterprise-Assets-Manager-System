using System.Security.Claims;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Abstractions;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>The signed-in user of the current HTTP request (SYSTEM when anonymous).</summary>
internal sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId => IsAuthenticated ? Principal!.GetUserId() : null;

    public string UserName => IsAuthenticated && !string.IsNullOrEmpty(Principal!.Identity!.Name)
        ? Principal.Identity.Name!
        : SystemUserService.SystemUserName;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => _accessor.HttpContext?.TraceIdentifier;
}
