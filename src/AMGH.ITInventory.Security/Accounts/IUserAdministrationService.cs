using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Security.Accounts;

/// <summary>User administration (Users.View / Users.Manage) and login history (AuditLogs.View).</summary>
public interface IUserAdministrationService
{
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<UserDetails?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleOption>> ListRolesAsync(CancellationToken cancellationToken = default);

    Task<Result<TemporaryPasswordResult>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result<TemporaryPasswordResult>> ResetPasswordAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoginAuditEntry>> GetLoginHistoryAsync(string? username, string? eventType, DateTime? fromUtc,
        DateTime? toUtc, int maxRows = 500, CancellationToken cancellationToken = default);
}
