namespace AMGH.ITInventory.Security.Accounts;

/// <summary>Everything the web layer needs to issue the authentication cookie: the user, the company
/// signed in to, and the roles and permissions held in that company.</summary>
public sealed record AuthenticatedUser(
    int UserId,
    string Username,
    string DisplayName,
    Guid SecurityStamp,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    int CompanyId,
    string CompanyCode,
    string CompanyName,
    bool IsPlatformAdmin);

/// <summary>
/// Access of a user to an active company: a membership, or, for the Platform Super Administrator,
/// platform access to a company they are not a member of (<see cref="IsPlatformAccess"/>).
/// </summary>
public sealed record CompanyMembership(int CompanyId, string CompanyCode, string CompanyName, bool IsDefault, bool IsPlatformAccess = false);

public enum SignInStatus
{
    Succeeded,
    Failed,
    LockedOut
}

public sealed record SignInOutcome(SignInStatus Status, AuthenticatedUser? User, string? Message, DateTime? LockedUntilUtc)
{
    public bool Succeeded => Status == SignInStatus.Succeeded;

    public static SignInOutcome Success(AuthenticatedUser user) => new(SignInStatus.Succeeded, user, null, null);
    public static SignInOutcome Fail(string message) => new(SignInStatus.Failed, null, message, null);
    public static SignInOutcome Locked(DateTime untilUtc) =>
        new(SignInStatus.LockedOut, null, "This account is temporarily locked after repeated failed sign-in attempts.", untilUtc);
}

public sealed record UserSummary(
    int UserId,
    string Username,
    string DisplayName,
    string Email,
    bool IsActive,
    bool IsLockedOut,
    bool MustChangePassword,
    DateTime? LastLoginDateUtc,
    IReadOnlyList<string> Roles);

public sealed record UserDetails(
    int UserId,
    string Username,
    string DisplayName,
    string Email,
    int? EmployeeId,
    bool IsActive,
    bool IsLockedOut,
    DateTime? LockoutEndUtc,
    bool MustChangePassword,
    DateTime? LastLoginDateUtc,
    string? LastLoginIp,
    DateTime CreatedDateUtc,
    string CreatedBy,
    IReadOnlyList<int> RoleIds,
    byte[] RowVersion);

public sealed record RoleOption(int RoleId, string RoleName, string? Description);

public sealed record CreateUserRequest(string Username, string DisplayName, string Email, int? EmployeeId, IReadOnlyList<int> RoleIds);

public sealed record UpdateUserRequest(int UserId, string DisplayName, string Email, int? EmployeeId, bool IsActive,
    IReadOnlyList<int> RoleIds, byte[] RowVersion);

/// <summary>Returned once to the administrator; never stored in plain text.</summary>
public sealed record TemporaryPasswordResult(int UserId, string Username, string TemporaryPassword);

public sealed record LoginAuditEntry(
    long LoginAuditId,
    DateTime EventDateUtc,
    string Username,
    string EventType,
    string? IpAddress,
    string? FailureReason,
    string? UserAgent);
