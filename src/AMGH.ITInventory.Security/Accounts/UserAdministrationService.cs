using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Passwords;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Security.Accounts;

/// <summary>
/// User administration within the current company. Users are platform-wide identities; this service only
/// sees and manages users with a membership in the current company. "Active" and the employee link are per
/// membership (disabling a user here does not affect their access to other companies); roles are per company.
/// </summary>
internal sealed partial class UserAdministrationService : IUserAdministrationService
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private readonly IUnitOfWork _uow;
    private readonly AccountService _accounts;
    private readonly IPasswordPolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly SecurityOptions _options;
    private readonly ICompanyContext _company;

    public UserAdministrationService(IUnitOfWork uow, AccountService accounts, IPasswordPolicy policy,
        ICurrentUserService currentUser, IDateTimeProvider clock, IOptions<SecurityOptions> options, ICompanyContext company)
    {
        _uow = uow;
        _accounts = accounts;
        _policy = policy;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
        _company = company;
    }

    private int CompanyId => _company.CompanyId ?? throw new InvalidOperationException("No company is active.");

    /// <summary>Membership of <paramref name="userId"/> in the current company (tracked), or null.</summary>
    private Task<CompanyUser?> MembershipAsync(int userId, CancellationToken ct)
    {
        var companyId = CompanyId;
        return _uow.Repository<CompanyUser>().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var companyId = CompanyId;
        var users = await _uow.Repository<CompanyUser>().QueryAsync(q => q.Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.User.Username).Select(m => new
            {
                m.User.UserId, m.User.Username, m.User.DisplayName, m.User.Email, IsActive = m.IsActive && m.User.IsActive,
                m.User.LockoutEndUtc, m.User.MustChangePassword, m.User.LastLoginDate
            }), cancellationToken);
        var roleMap = await LoadRoleNamesByUserAsync(cancellationToken);

        return users.Select(u => new UserSummary(u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive,
                u.LockoutEndUtc > now, u.MustChangePassword, u.LastLoginDate,
                roleMap.TryGetValue(u.UserId, out var r) ? r : Array.Empty<string>()))
            .ToList();
    }

    public async Task<UserDetails?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var membership = await MembershipAsync(userId, cancellationToken);
        if (membership is null) return null; // users of other companies do not exist here
        var rows = await _uow.Repository<User>().QueryAsync(q => q.Where(u => u.UserId == userId), cancellationToken);
        if (rows.Count == 0) return null;
        var u = rows[0];
        var roleIds = await _uow.Repository<UserRole>().QueryAsync(q => q.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId), cancellationToken);
        return new UserDetails(u.UserId, u.Username, u.DisplayName, u.Email, membership.EmployeeId, membership.IsActive && u.IsActive,
            u.LockoutEndUtc > _clock.UtcNow, u.LockoutEndUtc, u.MustChangePassword, u.LastLoginDate, u.LastLoginIp,
            u.CreatedDate, u.CreatedBy, roleIds, u.RowVersion);
    }

    public async Task<IReadOnlyList<RoleOption>> ListRolesAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<Role>().QueryAsync(q => q.Where(r => r.IsActive).OrderBy(r => r.RoleName)
            .Select(r => new RoleOption(r.RoleId, r.RoleName, r.Description)), cancellationToken);

    public async Task<Result<TemporaryPasswordResult>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var username = (request.Username ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();

        var errors = ValidateProfile(displayName, email).ToList();
        if (!UsernamePattern().IsMatch(username))
            errors.Add(Error.Validation("Username must be 3-100 characters: letters, digits, '.', '_' or '-'.", nameof(request.Username)));
        errors.AddRange(await ValidateRolesAsync(request.RoleIds, cancellationToken));
        if (errors.Count > 0) return Result<TemporaryPasswordResult>.Failure(errors.ToArray());

        var users = _uow.Repository<User>();
        if (await users.AnyAsync(u => u.Username == username, cancellationToken)
            || (await _uow.Repository<User>().ListIncludingDeletedAsync(u => u.Username == username, cancellationToken)).Count > 0)
            return Result<TemporaryPasswordResult>.Failure(Error.Conflict("This username is already taken.", nameof(request.Username)));
        if (await users.AnyAsync(u => u.Email == email, cancellationToken))
            return Result<TemporaryPasswordResult>.Failure(Error.Conflict("Another user already has this e-mail address.", nameof(request.Email)));

        var companyId = CompanyId;
        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            Email = email,
            IsActive = true
        };
        var temporary = NewTemporaryPassword(user);
        _accounts.SetPassword(user, temporary, _clock.UtcNow, mustChange: true);

        // The membership must exist before its roles (database rule FK_tblUserRole_Membership): two saves, one transaction.
        await _uow.ExecuteInTransactionAsync(async ct =>
        {
            await users.AddAsync(user, ct);
            await _uow.Repository<CompanyUser>().AddAsync(new CompanyUser
            {
                CompanyId = companyId, User = user, EmployeeId = request.EmployeeId, IsActive = true, IsDefault = true,
                CreatedBy = _currentUser.UserName, CreatedDate = _clock.UtcNow
            }, ct);
            await _uow.SaveChangesAsync(ct);
            foreach (var roleId in request.RoleIds.Distinct())
                await _uow.Repository<UserRole>().AddAsync(new UserRole { UserId = user.UserId, RoleId = roleId }, ct);
            await _uow.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);
        return Result<TemporaryPasswordResult>.Success(new TemporaryPasswordResult(user.UserId, user.Username, temporary));
    }

    public async Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var users = _uow.Repository<User>();
        var membership = await MembershipAsync(request.UserId, cancellationToken);
        var user = membership is null ? null : await users.FirstOrDefaultAsync(u => u.UserId == request.UserId, cancellationToken);
        if (user is null || membership is null) return Result.Failure(Error.NotFound("User not found."));

        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var errors = ValidateProfile(displayName, email).ToList();
        errors.AddRange(await ValidateRolesAsync(request.RoleIds, cancellationToken));
        if (request.UserId == _currentUser.UserId && !request.IsActive)
            errors.Add(Error.BusinessRule("You cannot disable your own account."));
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
            && await users.AnyAsync(u => u.Email == email && u.UserId != request.UserId, cancellationToken))
            errors.Add(Error.Conflict("Another user already has this e-mail address.", nameof(request.Email)));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());

        users.SetOriginalRowVersion(user, request.RowVersion);

        // Active and employee link belong to this company's membership; the platform account stays as it is.
        var securityRelevantChange = membership.IsActive != request.IsActive;
        user.DisplayName = displayName;
        user.Email = email;
        membership.EmployeeId = request.EmployeeId;
        membership.IsActive = request.IsActive;
        membership.ModifiedBy = _currentUser.UserName;
        membership.ModifiedDate = _clock.UtcNow;

        // Role links: soft-deleted rows keep the composite key, so re-activate instead of inserting.
        var wanted = request.RoleIds.Distinct().ToHashSet();
        var links = await _uow.Repository<UserRole>().ListIncludingDeletedAsync(ur => ur.UserId == user.UserId, cancellationToken);
        var now = _clock.UtcNow;
        foreach (var link in links)
        {
            var shouldExist = wanted.Contains(link.RoleId);
            if (shouldExist && link.IsDeleted)
            {
                link.Restore(_currentUser.UserName, now);
                securityRelevantChange = true;
            }
            else if (!shouldExist && !link.IsDeleted)
            {
                _uow.Repository<UserRole>().Remove(link); // soft delete on save
                securityRelevantChange = true;
            }
        }
        foreach (var roleId in wanted.Where(id => links.All(l => l.RoleId != id)))
        {
            await _uow.Repository<UserRole>().AddAsync(new UserRole { UserId = user.UserId, RoleId = roleId }, cancellationToken);
            securityRelevantChange = true;
        }

        // Forces the user's open sessions to sign in again with the new roles / status.
        if (securityRelevantChange) user.SecurityStamp = Guid.NewGuid();

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (await MembershipAsync(userId, cancellationToken) is null) return Result.Failure(Error.NotFound("User not found."));
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return Result.Failure(Error.NotFound("User not found."));
        user.LockoutEndUtc = null;
        user.AccessFailedCount = 0;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<TemporaryPasswordResult>> ResetPasswordAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (await MembershipAsync(userId, cancellationToken) is null)
            return Result<TemporaryPasswordResult>.Failure(Error.NotFound("User not found."));
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return Result<TemporaryPasswordResult>.Failure(Error.NotFound("User not found."));
        if (userId == _currentUser.UserId)
            return Result<TemporaryPasswordResult>.Failure(Error.BusinessRule("Use 'Change password' to change your own password."));

        var now = _clock.UtcNow;
        var temporary = NewTemporaryPassword(user);
        _accounts.SetPassword(user, temporary, now, mustChange: true);
        await _accounts.AddLoginAuditAsync(user.UserId, user.Username, LoginEventType.PasswordReset, _currentUser.IpAddress, null,
            $"Reset by {_currentUser.UserName}", now, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result<TemporaryPasswordResult>.Success(new TemporaryPasswordResult(user.UserId, user.Username, temporary));
    }

    public async Task<IReadOnlyList<LoginAuditEntry>> GetLoginHistoryAsync(string? username, string? eventType,
        DateTime? fromUtc, DateTime? toUtc, int maxRows = 500, CancellationToken cancellationToken = default)
    {
        maxRows = Math.Clamp(maxRows, 1, 5000);
        LoginEventType? type = Enum.TryParse<LoginEventType>(eventType, ignoreCase: true, out var t) ? t : null;
        var name = string.IsNullOrWhiteSpace(username) ? null : username.Trim();

        return await _uow.Repository<LoginAudit>().QueryAsync(q =>
        {
            if (name is not null) q = q.Where(a => a.UsernameAttempted.Contains(name));
            if (type is not null) q = q.Where(a => a.EventType == type);
            if (fromUtc is not null) q = q.Where(a => a.EventDate >= fromUtc);
            if (toUtc is not null) q = q.Where(a => a.EventDate < toUtc);
            return q.OrderByDescending(a => a.LoginAuditId).Take(maxRows)
                .Select(a => new LoginAuditEntry(a.LoginAuditId, a.EventDate, a.UsernameAttempted, a.EventType.ToString(),
                    a.IpAddress, a.FailureReason, a.UserAgent));
        }, cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private string NewTemporaryPassword(User user)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = TemporaryPasswordGenerator.Generate(_options.TemporaryPasswordLength);
            if (_policy.Validate(candidate, user.Username, user.DisplayName).Count == 0) return candidate;
        }
        throw new InvalidOperationException("Could not generate a temporary password that satisfies the policy.");
    }

    private static IEnumerable<Error> ValidateProfile(string displayName, string email)
    {
        if (displayName.Length is < 2 or > 200)
            yield return Error.Validation("Display name must be 2-200 characters.", "DisplayName");
        if (email.Length > 256 || !EmailPattern().IsMatch(email))
            yield return Error.Validation("Enter a valid e-mail address.", "Email");
    }

    private async Task<IEnumerable<Error>> ValidateRolesAsync(IReadOnlyList<int>? roleIds, CancellationToken ct)
    {
        if (roleIds is null || roleIds.Count == 0)
            return new[] { Error.Validation("Select at least one role.", "RoleIds") };
        var distinct = roleIds.Distinct().ToList();
        var found = await _uow.Repository<Role>().CountAsync(r => distinct.Contains(r.RoleId) && r.IsActive, ct);
        return found == distinct.Count ? Array.Empty<Error>() : new[] { Error.Validation("One or more selected roles are invalid.", "RoleIds") };
    }

    private async Task<Dictionary<int, IReadOnlyList<string>>> LoadRoleNamesByUserAsync(CancellationToken ct)
    {
        var rows = await _uow.Repository<UserRole>().QueryAsync(q => q.Select(ur => new { ur.UserId, ur.Role.RoleName }), ct);
        return rows.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.RoleName).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }
}
