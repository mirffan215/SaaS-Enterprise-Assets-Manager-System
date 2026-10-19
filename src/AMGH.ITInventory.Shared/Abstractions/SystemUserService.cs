namespace AMGH.ITInventory.Shared.Abstractions;

/// <summary>Identity used by background jobs, seeding and design-time tooling.</summary>
public sealed class SystemUserService : ICurrentUserService
{
    public const string SystemUserName = "SYSTEM";

    private readonly string _userName;

    public SystemUserService(string userName = SystemUserName) => _userName = userName;

    public int? UserId => null;
    public string UserName => _userName;
    public string? IpAddress => null;
    public string? CorrelationId => null;
    public bool IsAuthenticated => false;
}
