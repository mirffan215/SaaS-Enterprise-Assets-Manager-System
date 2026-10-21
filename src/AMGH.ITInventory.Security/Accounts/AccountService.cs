using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Passwords;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using Microsoft.Extensions.Options;

namespace AMGH.ITInventory.Security.Accounts;

internal sealed class AccountService : IAccountService
{
    internal const string InvalidCredentials = "Invalid username or password.";
    internal const string AccountDisabled = "This account is disabled. Contact the IT department.";
    internal const string NoCompanyAccess = "Your account has no access to this company. Contact your administrator.";
    internal const string NoAnyCompanyAccess = "Your account has no active access to any company. Contact your administrator.";
    public const string PlatformAdministratorRole = "Platform Administrator";

    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasherService _hasher;
    private readonly IPasswordPolicy _policy;
    private readonly IDateTimeProvider _clock;
    private readonly SecurityOptions _options;
    private readonly ICompanyContext _company;

    public AccountService(IUnitOfWork uow, IPasswordHasherService hasher, IPasswordPolicy policy,
        IDateTimeProvider clock, IOptions<SecurityOptions> options, ICompanyContext company)
    {
        _uow = uow;
        _hasher = hasher;
        _policy = policy;
        _clock = clock;
        _options = options.Value;
        _company = company;
    }

    public async Task<SignInOutcome> SignInAsync(string username, string password, string? ipAddress, string? userAgent,
        string? companyCode = null, CancellationToken cancellationToken = default)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length == 0 || string.IsNullOrEmpty(password))
            return SignInOutcome.Fail(InvalidCredentials);

        var now = _clock.UtcNow;
        // User name or e-mail address (Release 2.12). User names cannot contain '@' and e-mail addresses are unique,
        // so the entry identifies at most one account; unknown entries take the same time and give the same answer.
        var user = username.Contains('@', StringComparison.Ordinal)
            ? await _uow.Repository<User>().FirstOrDefaultAsync(u => u.Email == username, cancellationToken)
            : await _uow.Repository<User>().FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            _hasher.SimulateVerification(password);
            using (_company.EnterPlatform())
            {
                await AddLoginAuditAsync(null, username, LoginEventType.LoginFailed, ipAddress, userAgent, "Unknown username", now, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
            }
            return SignInOutcome.Fail(InvalidCredentials);
        }

        // Events are recorded under the company being signed in to (platform level when the user has no access to it).
        var membership = await ResolveMembershipAsync(user, companyCode, cancellationToken);
        using var scope = membership is null ? _company.EnterPlatform() : _company.Enter(membership.CompanyId);

        if (user.LockoutEndUtc is { } lockedUntil && lockedUntil > now)
        {
            await AddLoginAuditAsync(user.UserId, username, LoginEventType.LoginFailed, ipAddress, userAgent, "Account locked", now, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return SignInOutcome.Locked(lockedUntil);
        }

        var check = _hasher.Verify(user, password);
        if (check == PasswordCheck.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= _options.MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(_options.LockoutMinutes);
                user.AccessFailedCount = 0;
                await AddLoginAuditAsync(user.UserId, username, LoginEventType.Lockout, ipAddress, userAgent,
                    $"Locked for {_options.LockoutMinutes} minutes after {_options.MaxFailedAccessAttempts} failed attempts", now, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
                return SignInOutcome.Locked(user.LockoutEndUtc.Value);
            }
            await AddLoginAuditAsync(user.UserId, username, LoginEventType.LoginFailed, ipAddress, userAgent, "Wrong password", now, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return SignInOutcome.Fail(InvalidCredentials);
        }

        // Disabled status and missing company access are revealed only after the correct password, so they cannot be probed.
        if (!user.IsActive)
        {
            await AddLoginAuditAsync(user.UserId, username, LoginEventType.AccountDisabled, ipAddress, userAgent, "Account disabled", now, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return SignInOutcome.Fail(AccountDisabled);
        }
        if (membership is null)
        {
            // Disabled in that company (membership exists but is inactive) reads as "account disabled", as before Release 2.0.
            var code = companyCode?.Trim();
            var disabledThere = await _uow.Repository<CompanyUser>().AnyAsync(m => m.UserId == user.UserId && !m.IsActive
                && m.Company.Status == CompanyStatuses.Active && (code == null || m.Company.CompanyCode == code), cancellationToken);
            await AddLoginAuditAsync(user.UserId, username, disabledThere ? LoginEventType.AccountDisabled : LoginEventType.LoginFailed,
                ipAddress, userAgent, disabledThere ? "Membership disabled"
                    : code is null ? "No active company membership" : $"No access to company {Truncate(code, 20)}", now, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return SignInOutcome.Fail(disabledThere ? AccountDisabled : code is null ? NoAnyCompanyAccess : NoCompanyAccess);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.PasswordHash = _hasher.Hash(user, password);

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginDate = now;
        user.LastLoginIp = Truncate(ipAddress, 45);
        // Platform access is recorded in the company's own sign-in history, so the company can see it.
        await AddLoginAuditAsync(user.UserId, username, LoginEventType.LoginSuccess, ipAddress, userAgent,
            membership.IsPlatformAccess ? "Platform administrator access (not a member of this company)" : null, now, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return SignInOutcome.Success(await BuildAuthenticatedUserAsync(user, membership, now, cancellationToken));
    }

    public async Task<SignInOutcome> SignInExternalAsync(string email, string companyCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        const string notAllowed = "Your account is not allowed to sign in here. Contact your administrator.";
        email = (email ?? string.Empty).Trim();
        var now = _clock.UtcNow;
        var user = email.Length == 0 ? null : await _uow.Repository<User>().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        var membership = user is null ? null : await ResolveMembershipAsync(user, companyCode, cancellationToken);
        if (user is null || !user.IsActive || membership is null)
        {
            using (_company.EnterPlatform())
            {
                await AddLoginAuditAsync(user?.UserId, email, LoginEventType.LoginFailed, ipAddress, userAgent, "Single sign-on: no active account with access to " + Truncate(companyCode, 20), now, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
            }
            return SignInOutcome.Fail(notAllowed);
        }
        using var scope = _company.Enter(membership.CompanyId);
        if (user.LockoutEndUtc is { } until && until > now) return SignInOutcome.Locked(until);
        user.LastLoginDate = now;
        user.LastLoginIp = Truncate(ipAddress, 45);
        await AddLoginAuditAsync(user.UserId, user.Username, LoginEventType.SsoSignIn, ipAddress, userAgent, null, now, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return SignInOutcome.Success(await BuildAuthenticatedUserAsync(user, membership, now, cancellationToken));
    }

    public async Task<AuthenticatedUser?> CreateSessionAsync(int userId, int companyId, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, cancellationToken);
        if (user is null) return null;
        var membership = await AccessToAsync(user, companyId, cancellationToken);
        return membership is null ? null : await BuildAuthenticatedUserAsync(user, membership, _clock.UtcNow, cancellationToken);
    }

    public async Task<Result<AuthenticatedUser>> ChangePasswordAsync(int userId, int companyId, string currentPassword, string newPassword,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, cancellationToken);
        if (user is null) return Result<AuthenticatedUser>.Failure(Error.NotFound("User account not found or disabled."));
        var membership = await AccessToAsync(user, companyId, cancellationToken);
        if (membership is null) return Result<AuthenticatedUser>.Failure(Error.Forbidden(NoCompanyAccess));
        using var scope = _company.Enter(companyId);

        if (_hasher.Verify(user, currentPassword) == PasswordCheck.Failed)
        {
            await AddLoginAuditAsync(user.UserId, user.Username, LoginEventType.LoginFailed, ipAddress, userAgent,
                "Wrong current password on password change", _clock.UtcNow, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return Result<AuthenticatedUser>.Failure(Error.Validation("The current password is incorrect.", "current_password"));
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            return Result<AuthenticatedUser>.Failure(Error.Validation("The new password must be different from the current password.", "new_password"));

        var errors = _policy.Validate(newPassword, user.Username, user.DisplayName);
        if (errors.Count > 0)
            return Result<AuthenticatedUser>.Failure(errors.Select(e => Error.Validation(e, "new_password")).ToArray());

        if (await IsInPasswordHistoryAsync(user, newPassword, cancellationToken))
            return Result<AuthenticatedUser>.Failure(Error.Validation(
                $"You cannot reuse any of your last {_options.PasswordHistoryCount} passwords.", "new_password"));

        var now = _clock.UtcNow;
        SetPassword(user, newPassword, now, mustChange: false);
        await AddLoginAuditAsync(user.UserId, user.Username, LoginEventType.PasswordChanged, ipAddress, userAgent, null, now, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<AuthenticatedUser>.Success(await BuildAuthenticatedUserAsync(user, membership, now, cancellationToken));
    }

    public async Task<bool> ValidateSecurityStampAsync(int userId, int companyId, Guid securityStamp, CancellationToken cancellationToken = default)
    {
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, cancellationToken);
        if (user is null || user.SecurityStamp != securityStamp) return false;
        // A suspended company, a disabled membership or a removed platform role ends the session at the next check.
        return await AccessToAsync(user, companyId, cancellationToken) is not null;
    }

    public async Task LogEventAsync(int? userId, int? companyId, string username, LoginEventType eventType, string? ipAddress,
        string? userAgent, string? reason = null, CancellationToken cancellationToken = default)
    {
        using var scope = companyId is { } id ? _company.Enter(id) : _company.EnterPlatform();
        await AddLoginAuditAsync(userId, username, eventType, ipAddress, userAgent, reason, _clock.UtcNow, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// tblCompanyUser is deliberately outside Row-Level Security (sign-in must see all of a user's companies),
    /// so this query filters by user explicitly and is the only place memberships are read for sign-in.
    /// </summary>
    public async Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId, CancellationToken cancellationToken = default) =>
        await _uow.Repository<CompanyUser>().QueryAsync(q => q
            .Where(m => m.UserId == userId && m.IsActive && m.Company.Status == CompanyStatuses.Active)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.Company.CompanyCode)
            .Select(m => new CompanyMembership(m.CompanyId, m.Company.CompanyCode, m.Company.DisplayName, m.IsDefault, false)), cancellationToken);

    private async Task<CompanyMembership?> ResolveMembershipAsync(User user, string? companyCode, CancellationToken ct)
    {
        var memberships = await GetMembershipsAsync(user.UserId, ct);
        if (string.IsNullOrWhiteSpace(companyCode))
            return memberships.Count > 0 ? memberships[0] : null; // default membership first, then by company code
        var code = companyCode.Trim().ToUpperInvariant();
        return memberships.FirstOrDefault(m => string.Equals(m.CompanyCode, code, StringComparison.OrdinalIgnoreCase))
               ?? (user.IsPlatformAdmin ? await PlatformAccessAsync(c => c.CompanyCode == code, ct) : null);
    }

    /// <summary>The user's access to <paramref name="companyId"/>: membership, or platform access for the Super Administrator.</summary>
    private async Task<CompanyMembership?> AccessToAsync(User user, int companyId, CancellationToken ct) =>
        (await GetMembershipsAsync(user.UserId, ct)).FirstOrDefault(m => m.CompanyId == companyId)
        ?? (user.IsPlatformAdmin ? await PlatformAccessAsync(c => c.CompanyId == companyId, ct) : null);

    private async Task<CompanyMembership?> PlatformAccessAsync(System.Linq.Expressions.Expression<Func<Company, bool>> which, CancellationToken ct)
    {
        var rows = await _uow.Repository<Company>().QueryAsync(q => q.Where(which).Where(c => c.Status == CompanyStatuses.Active)
            .Select(c => new CompanyMembership(c.CompanyId, c.CompanyCode, c.DisplayName, false, true)), ct);
        return rows.Count == 1 ? rows[0] : null;
    }

    // ------------------------------------------------------------------ helpers (shared with admin service)

    internal void SetPassword(User user, string password, DateTime now, bool mustChange)
    {
        user.PasswordHash = _hasher.Hash(user, password);
        user.PasswordChangedDate = now;
        user.MustChangePassword = mustChange;
        user.SecurityStamp = Guid.NewGuid(); // invalidates other sessions
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.PasswordHistory.Add(new UserPasswordHistory { User = user, PasswordHash = user.PasswordHash, CreatedDate = now });
    }

    private async Task<bool> IsInPasswordHistoryAsync(User user, string candidate, CancellationToken ct)
    {
        if (_options.PasswordHistoryCount <= 0) return false;
        var recent = await _uow.Repository<UserPasswordHistory>().QueryAsync(
            q => q.Where(h => h.UserId == user.UserId)
                  .OrderByDescending(h => h.UserPasswordHistoryId)
                  .Take(_options.PasswordHistoryCount)
                  .Select(h => h.PasswordHash), ct);
        return recent.Any(hash => _hasher.Verify(user, hash, candidate) != PasswordCheck.Failed);
    }

    /// <summary>Roles and permissions are those held in <paramref name="membership"/>'s company only.</summary>
    private async Task<AuthenticatedUser> BuildAuthenticatedUserAsync(User user, CompanyMembership membership, DateTime now, CancellationToken ct)
    {
        IReadOnlyList<string> roles, permissions;
        if (membership.IsPlatformAccess)
        {
            // Not a member: the Super Administrator works with every permission, under a visible platform role.
            roles = new[] { PlatformAdministratorRole };
            permissions = (await _uow.Repository<Permission>().QueryAsync(q => q.Select(p => p.PermissionCode), ct))
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
        }
        else
        {
            using (_company.Enter(membership.CompanyId))
                (roles, permissions) = await LoadRolesAndPermissionsAsync(user.UserId, membership.CompanyId, ct);
        }
        var expired = _options.PasswordMaximumAgeDays > 0
                      && (user.PasswordChangedDate is null || user.PasswordChangedDate.Value.AddDays(_options.PasswordMaximumAgeDays) < now);
        return new AuthenticatedUser(user.UserId, user.Username, user.DisplayName, user.SecurityStamp,
            user.MustChangePassword || expired, roles, permissions,
            membership.CompanyId, membership.CompanyCode, membership.CompanyName, user.IsPlatformAdmin);
    }

    /// <summary>
    /// The person's roles and permissions IN THIS COMPANY. Filtered explicitly by company at every step, in addition to
    /// Row-Level Security and the query filters: one company's roles can never grant rights in another (Release 2.8.1).
    /// </summary>
    private async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadRolesAndPermissionsAsync(int userId, int companyId, CancellationToken ct)
    {
        var roleIds = await _uow.Repository<UserRole>().QueryAsync(q => q.Where(ur => ur.UserId == userId && ur.CompanyId == companyId).Select(ur => ur.RoleId), ct);
        if (roleIds.Count == 0) return (Array.Empty<string>(), Array.Empty<string>());

        var roles = await _uow.Repository<Role>().QueryAsync(
            q => q.Where(r => roleIds.Contains(r.RoleId) && r.IsActive && r.CompanyId == companyId).Select(r => new { r.RoleId, r.RoleName }), ct);
        var activeRoleIds = roles.Select(r => r.RoleId).ToList();

        var permissions = await _uow.Repository<RolePermission>().QueryAsync(
            q => q.Where(rp => activeRoleIds.Contains(rp.RoleId) && rp.CompanyId == companyId && !rp.Permission.IsDeleted)
                  .Select(rp => rp.Permission.PermissionCode)
                  .Distinct(), ct);

        return (roles.Select(r => r.RoleName).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                permissions.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }

    internal Task AddLoginAuditAsync(int? userId, string username, LoginEventType eventType, string? ipAddress,
        string? userAgent, string? reason, DateTime now, CancellationToken ct) =>
        _uow.Repository<LoginAudit>().AddAsync(new LoginAudit
        {
            UserId = userId,
            UsernameAttempted = Truncate(username, 100) ?? string.Empty,
            EventType = eventType,
            IpAddress = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 500),
            FailureReason = Truncate(reason, 200),
            EventDate = now
        }, ct);

    internal static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
