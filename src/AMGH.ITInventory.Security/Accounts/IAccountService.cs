using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Security.Accounts;

/// <summary>Sign-in, password change and session validation for local accounts.</summary>
public interface IAccountService
{
    /// <summary>
    /// Signs in to a company. <paramref name="companyCode"/> selects the workspace (the company picker);
    /// when null, the user's default membership is used. Users without an active membership in an active
    /// company cannot sign in (revealed only after a correct password).
    /// </summary>
    Task<SignInOutcome> SignInAsync(string username, string password, string? ipAddress, string? userAgent,
        string? companyCode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs in a person who was already identified by their company's single sign-on provider (verified e-mail address). The person must be an
    /// active user with access to the company; no password is involved. Recorded as a single sign-on event.
    /// </summary>
    Task<SignInOutcome> SignInExternalAsync(string email, string companyCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>The authenticated user for a session after the second step succeeded; null when the account or its company access is no longer active.</summary>
    Task<AuthenticatedUser?> CreateSessionAsync(int userId, int companyId, CancellationToken cancellationToken = default);

    /// <summary>Changes the user's own password. Returns the refreshed user (same company) for re-issuing the cookie.</summary>
    Task<Result<AuthenticatedUser>> ChangePasswordAsync(int userId, int companyId, string currentPassword, string newPassword,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>True while the user is active, the cookie's stamp is current, and the membership and company are still active.</summary>
    Task<bool> ValidateSecurityStampAsync(int userId, int companyId, Guid securityStamp, CancellationToken cancellationToken = default);

    /// <summary>Records a sign-in event for <paramref name="companyId"/> (null: platform-level event).</summary>
    Task LogEventAsync(int? userId, int? companyId, string username, LoginEventType eventType, string? ipAddress, string? userAgent,
        string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>The user's active memberships in active companies (for the workspace switcher).</summary>
    Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId, CancellationToken cancellationToken = default);
}
