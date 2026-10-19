using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Security.Accounts;

// ============================================================================ models

public sealed record PermissionItem(string Code, string Module, string Name, string? Description);

public sealed record RoleListItem(int RoleId, string RoleName, string? Description, bool IsSystemRole, bool IsActive, int Members, int Permissions);

public sealed class RoleForm
{
    public string? RoleName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> Permissions { get; set; } = new();
}

public sealed record CompanyRoleOption(int RoleId, string RoleName, bool IsActive);

/// <summary>One person's access in one company.</summary>
public sealed record MembershipItem(int CompanyId, string CompanyCode, string CompanyName, string CompanyStatus, bool IsMember, bool IsActive, bool IsDefault,
    IReadOnlyList<int> RoleIds, IReadOnlyList<string> RoleNames, IReadOnlyList<CompanyRoleOption> AvailableRoles);

public sealed record UserAccess(int UserId, string Username, string DisplayName, string? Email, bool IsActive, bool IsPlatformAdmin, DateTime? LastLoginUtc,
    IReadOnlyList<MembershipItem> Companies);

public sealed record UserAccessSummary(int UserId, string Username, string DisplayName, string? Email, bool IsActive, bool IsPlatformAdmin,
    DateTime? LastLoginUtc, string Companies);

/// <summary>One line of the access review: a person's access in one company.</summary>
public sealed record AccessReviewRow(string CompanyCode, string CompanyName, string Username, string DisplayName, string? Email, bool UserActive,
    bool MembershipActive, bool IsDefault, bool IsPlatformAdmin, string Roles, DateTime? LastLoginUtc);

public sealed class MembershipForm
{
    public int CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public List<int> RoleIds { get; set; } = new();
}

// ============================================================================ roles (inside the signed-in company)

public interface IRoleAdministrationService
{
    const string SystemAdministrator = "SystemAdministrator";
    /// <summary>The System Administrator role always keeps these, so a company cannot lock itself out of its own administration.</summary>
    static readonly IReadOnlyList<string> AdministratorEssentials = new[]
    {
        Permissions.Administration.RolesManage, Permissions.Administration.UsersManage, Permissions.Administration.UsersView
    };

    Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionItem>> CatalogAsync(CancellationToken cancellationToken = default);
    Task<(RoleListItem Role, IReadOnlySet<string> Permissions)?> GetAsync(int roleId, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(int? roleId, RoleForm form, CancellationToken cancellationToken = default);
}

internal sealed class RoleAdministrationService : IRoleAdministrationService
{
    private static readonly System.Text.RegularExpressions.Regex NamePattern = new("^[A-Za-z][A-Za-z0-9]{2,49}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    private readonly ICompanyContext _company;

    public RoleAdministrationService(IUnitOfWork uow, ICurrentUserService user, IDateTimeProvider clock, ICompanyContext company)
    {
        _uow = uow;
        _user = user;
        _clock = clock;
        _company = company;
    }

    /// <summary>The signed-in company (explicit filter in addition to Row-Level Security and the query filters).</summary>
    private int Company => _company.CompanyId ?? throw new InvalidOperationException("No company is active.");

    public async Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<Role>().QueryAsync(q => q.Where(r => r.CompanyId == Company).OrderByDescending(r => r.IsSystemRole).ThenBy(r => r.RoleName).Select(r => new RoleListItem(
            r.RoleId, r.RoleName, r.Description, r.IsSystemRole, r.IsActive, r.UserRoles.Count(ur => !ur.IsDeleted), r.RolePermissions.Count(p => !p.IsDeleted))),
            cancellationToken);

    public async Task<IReadOnlyList<PermissionItem>> CatalogAsync(CancellationToken cancellationToken = default) =>
        await _uow.Repository<Permission>().QueryAsync(q => q.OrderBy(p => p.ModuleName).ThenBy(p => p.SortOrder).ThenBy(p => p.PermissionCode)
            .Select(p => new PermissionItem(p.PermissionCode, p.ModuleName, p.PermissionName, p.Description)), cancellationToken);

    public async Task<(RoleListItem Role, IReadOnlySet<string> Permissions)?> GetAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var role = (await ListAsync(cancellationToken)).FirstOrDefault(r => r.RoleId == roleId);
        if (role is null) return null;
        var codes = await _uow.Repository<RolePermission>().QueryAsync(q => q.Where(rp => rp.RoleId == roleId && rp.CompanyId == Company).Select(rp => rp.Permission.PermissionCode), cancellationToken);
        return (role, codes.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<Result<int>> SaveAsync(int? roleId, RoleForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var ct = cancellationToken;
        var errors = new List<Error>();
        var name = (form.RoleName ?? string.Empty).Trim();
        var description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        var catalog = (await _uow.Repository<Permission>().QueryAsync(q => q.Select(p => new { p.PermissionId, p.PermissionCode }), ct))
            .ToDictionary(p => p.PermissionCode, p => p.PermissionId, StringComparer.Ordinal);
        var wanted = form.Permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var unknown = wanted.FirstOrDefault(p => !catalog.ContainsKey(p));
        if (unknown is not null) errors.Add(Error.Validation($"Unknown permission '{unknown}'.", "Permissions"));
        if (wanted.Count == 0) errors.Add(Error.Validation("Give the role at least one permission.", "Permissions"));
        if (description?.Length > 500) errors.Add(Error.Validation("Description: up to 500 characters.", nameof(form.Description)));

        Role role;
        if (roleId is { } id)
        {
            var found = await _uow.Repository<Role>().FirstOrDefaultAsync(r => r.RoleId == id && r.CompanyId == Company, ct);
            if (found is null) return Result<int>.Failure(Error.NotFound("Role not found."));
            role = found;
            if (role.IsSystemRole)
            {
                // The database protects system roles (trg_tblRole_ProtectSystemRoles): their name and status are fixed.
                if (!string.Equals(name, role.RoleName, StringComparison.Ordinal)) errors.Add(Error.Validation("A built-in role keeps its name.", nameof(form.RoleName)));
                if (!form.IsActive) errors.Add(Error.Validation("A built-in role cannot be deactivated.", nameof(form.IsActive)));
            }
        }
        else
        {
            role = new Role { CompanyId = Company, IsSystemRole = false };
        }
        if (!NamePattern.IsMatch(name)) errors.Add(Error.Validation("Role code: 3-50 letters or digits, starting with a letter (e.g. AssetViewer).", nameof(form.RoleName)));
        else if (await _uow.Repository<Role>().AnyAsync(r => r.RoleName == name && r.CompanyId == Company && r.RoleId != (roleId ?? 0), ct))
            errors.Add(Error.Conflict("Another role in this company already has this code.", nameof(form.RoleName)));
        if (string.Equals(name, IRoleAdministrationService.SystemAdministrator, StringComparison.Ordinal))
        {
            var missing = IRoleAdministrationService.AdministratorEssentials.Where(p => !wanted.Contains(p)).ToList();
            if (missing.Count > 0)
                errors.Add(Error.Validation("The System Administrator role must keep " + string.Join(", ", missing) +
                    ", otherwise nobody could manage this company's users and roles.", "Permissions"));
        }
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());

        role.RoleName = name;
        role.Description = description;
        role.IsActive = form.IsActive;
        if (roleId is null)
        {
            await _uow.Repository<Role>().AddAsync(role, ct);
            await _uow.SaveChangesAsync(ct);
        }

        // permission links: soft-deleted rows keep the composite key, so re-activate instead of inserting
        var wantedIds = wanted.Select(c => catalog[c]).ToHashSet();
        var links = await _uow.Repository<RolePermission>().ListIncludingDeletedAsync(rp => rp.RoleId == role.RoleId && rp.CompanyId == Company, ct);
        var now = _clock.UtcNow;
        foreach (var link in links)
        {
            var keep = wantedIds.Contains(link.PermissionId);
            if (keep && link.IsDeleted) link.Restore(_user.UserName, now);
            else if (!keep && !link.IsDeleted) _uow.Repository<RolePermission>().Remove(link);
        }
        foreach (var pid in wantedIds.Where(p => links.All(l => l.PermissionId != p)))
            await _uow.Repository<RolePermission>().AddAsync(new RolePermission { CompanyId = Company, RoleId = role.RoleId, PermissionId = pid }, ct);

        // everyone with this role signs in again within minutes (their permissions are in the session cookie)
        var memberIds = await _uow.Repository<UserRole>().QueryAsync(q => q.Where(ur => ur.RoleId == role.RoleId && ur.CompanyId == Company).Select(ur => ur.UserId), ct);
        foreach (var u in await _uow.Repository<User>().ListAsync(x => memberIds.Contains(x.UserId), ct)) u.SecurityStamp = Guid.NewGuid();
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(role.RoleId);
    }
}

// ============================================================================ people across companies (platform administrators)

public interface IUserAccessService
{
    Task<IReadOnlyList<UserAccessSummary>> SearchAsync(string? term, CancellationToken cancellationToken = default);
    Task<UserAccess?> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result> SaveMembershipAsync(int userId, MembershipForm form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccessReviewRow>> ReviewAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and changes a person's access in every company. Role links are company data, so each company is visited in
/// its own company context (Row-Level Security and query filters stay in force); memberships are platform data.
/// Changes are recorded in the audit trail of the company they concern.
/// </summary>
internal sealed class UserAccessService : IUserAccessService
{
    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;

    public UserAccessService(IUnitOfWork uow, ICompanyContext company, ICurrentUserService user, IDateTimeProvider clock)
    {
        _uow = uow;
        _company = company;
        _user = user;
        _clock = clock;
    }

    private async Task<List<(int Id, string Code, string Name, string Status)>> CompaniesAsync(CancellationToken ct) =>
        (await _uow.Repository<Company>().QueryAsync(q => q.OrderBy(c => c.CompanyCode).Select(c => new { c.CompanyId, c.CompanyCode, c.DisplayName, c.Status }), ct))
        .Select(c => (c.CompanyId, c.CompanyCode, c.DisplayName, c.Status)).ToList();

    /// <summary>Role links and roles of one company, read inside that company.</summary>
    private async Task<(List<(int UserId, int RoleId, string RoleName)> Links, List<CompanyRoleOption> Roles)> CompanyRolesAsync(int companyId, CancellationToken ct)
    {
        using var scope = _company.Enter(companyId);
        // explicit company filter as well as Row-Level Security and the query filters (defence in depth)
        var links = (await _uow.Repository<UserRole>().QueryAsync(q => q.Where(ur => ur.CompanyId == companyId).Select(ur => new { ur.UserId, ur.RoleId, ur.Role.RoleName }), ct))
            .Select(x => (x.UserId, x.RoleId, x.RoleName)).ToList();
        var roles = (await _uow.Repository<Role>().QueryAsync(q => q.Where(r => r.CompanyId == companyId).OrderBy(r => r.RoleName).Select(r => new { r.RoleId, r.RoleName, r.IsActive }), ct))
            .Select(r => new CompanyRoleOption(r.RoleId, r.RoleName, r.IsActive)).ToList();
        return (links, roles);
    }

    public async Task<IReadOnlyList<UserAccessSummary>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var t = (term ?? string.Empty).Trim();
        var users = await _uow.Repository<User>().QueryAsync(q => q.OrderBy(u => u.Username).Select(u => new
            { u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive, u.IsPlatformAdmin, u.LastLoginDate }), ct);
        var memberships = await _uow.Repository<CompanyUser>().QueryAsync(q => q.Select(m => new { m.UserId, m.IsActive, m.Company.CompanyCode }), ct);
        // filtered in memory (the user list is small): case-insensitive, culture-independent
        return users.Where(u => t.Length == 0 || u.Username.Contains(t, StringComparison.OrdinalIgnoreCase)
                || u.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase) || (u.Email ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase))
            .Select(u => new UserAccessSummary(u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive, u.IsPlatformAdmin, u.LastLoginDate,
                string.Join(", ", memberships.Where(m => m.UserId == u.UserId).OrderBy(m => m.CompanyCode).Select(m => m.CompanyCode + (m.IsActive ? "" : " (inactive)")))))
            .ToList();
    }

    public async Task<UserAccess?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var u = await _uow.Repository<User>().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (u is null) return null;
        var memberships = await _uow.Repository<CompanyUser>().ListAsync(m => m.UserId == userId, ct);
        var items = new List<MembershipItem>();
        foreach (var c in await CompaniesAsync(ct))
        {
            var (links, roles) = await CompanyRolesAsync(c.Id, ct);
            var m = memberships.FirstOrDefault(x => x.CompanyId == c.Id);
            var mine = links.Where(l => l.UserId == userId).ToList();
            items.Add(new MembershipItem(c.Id, c.Code, c.Name, c.Status, m is not null, m?.IsActive ?? false, m?.IsDefault ?? false,
                mine.Select(l => l.RoleId).ToList(), mine.Select(l => l.RoleName).ToList(), roles));
        }
        return new UserAccess(u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive, u.IsPlatformAdmin, u.LastLoginDate, items);
    }

    public async Task<Result> SaveMembershipAsync(int userId, MembershipForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var ct = cancellationToken;
        var user = await _uow.Repository<User>().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User not found."));
        var company = (await CompaniesAsync(ct)).FirstOrDefault(c => c.Id == form.CompanyId);
        if (company.Id == 0) return Result.Failure(Error.NotFound("Company not found."));
        if (company.Status == CompanyStatuses.Archived) return Result.Failure(Error.BusinessRule("The company is archived."));
        var (links, roles) = await CompanyRolesAsync(company.Id, ct);
        var wanted = form.RoleIds.Distinct().ToHashSet();
        if (wanted.Any(id => roles.All(r => r.RoleId != id))) return Result.Failure(Error.Validation("A chosen role does not belong to this company.", "RoleIds"));
        if (form.IsActive && wanted.Count == 0) return Result.Failure(Error.Validation("Give the person at least one role in this company.", "RoleIds"));

        // never leave a company without an active System Administrator
        var adminRole = roles.FirstOrDefault(r => r.RoleName == IRoleAdministrationService.SystemAdministrator);
        if (adminRole is not null && links.Any(l => l.UserId == userId && l.RoleId == adminRole.RoleId) && (!form.IsActive || !wanted.Contains(adminRole.RoleId)))
        {
            var activeMembers = (await _uow.Repository<CompanyUser>().QueryAsync(q => q.Where(m => m.CompanyId == company.Id && m.IsActive && m.User.IsActive)
                .Select(m => m.UserId), ct)).ToHashSet();
            if (!links.Any(l => l.RoleId == adminRole.RoleId && l.UserId != userId && activeMembers.Contains(l.UserId)))
                return Result.Failure(Error.BusinessRule($"{user.Username} is the last active System Administrator of {company.Code}: give another person that role first."));
        }

        var now = _clock.UtcNow;
        using (_company.Enter(company.Id))     // audit entries are recorded in this company
        {
            await _uow.ExecuteInTransactionAsync(async tct =>
            {
                var membership = await _uow.Repository<CompanyUser>().FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == company.Id, tct);
                if (membership is null)
                {
                    membership = new CompanyUser { CompanyId = company.Id, UserId = userId, CreatedBy = _user.UserName, CreatedDate = now };
                    await _uow.Repository<CompanyUser>().AddAsync(membership, tct);
                }
                membership.IsActive = form.IsActive;
                membership.ModifiedBy = _user.UserName;
                membership.ModifiedDate = now;
                if (form.IsDefault && form.IsActive)
                {
                    foreach (var other in await _uow.Repository<CompanyUser>().ListAsync(m => m.UserId == userId && m.CompanyId != company.Id && m.IsDefault, tct))
                        other.IsDefault = false;
                    await _uow.SaveChangesAsync(tct);           // one default per person (unique index)
                    membership.IsDefault = true;
                }
                else if (!form.IsActive) membership.IsDefault = false;
                await _uow.SaveChangesAsync(tct);               // the membership must exist before its roles (FK_tblUserRole_Membership)

                // only this company's role links: other companies' roles of the person are never touched
                var existing = await _uow.Repository<UserRole>().ListIncludingDeletedAsync(ur => ur.UserId == userId && ur.CompanyId == company.Id, tct);
                foreach (var link in existing)
                {
                    var keep = wanted.Contains(link.RoleId);
                    if (keep && link.IsDeleted) link.Restore(_user.UserName, now);
                    else if (!keep && !link.IsDeleted) _uow.Repository<UserRole>().Remove(link);
                }
                foreach (var roleId in wanted.Where(id => existing.All(l => l.RoleId != id)))
                    await _uow.Repository<UserRole>().AddAsync(new UserRole { CompanyId = company.Id, UserId = userId, RoleId = roleId }, tct);
                user.SecurityStamp = Guid.NewGuid();            // open sessions re-authorise within minutes
                await _uow.SaveChangesAsync(tct);
            }, ct);
        }
        return Result.Success();
    }

    public async Task<IReadOnlyList<AccessReviewRow>> ReviewAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var users = (await _uow.Repository<User>().QueryAsync(q => q.Select(u => new { u.UserId, u.Username, u.DisplayName, u.Email, u.IsActive, u.IsPlatformAdmin, u.LastLoginDate }), ct))
            .ToDictionary(u => u.UserId);
        var memberships = await _uow.Repository<CompanyUser>().QueryAsync(q => q.Select(m => new { m.CompanyId, m.UserId, m.IsActive, m.IsDefault }), ct);
        var rows = new List<AccessReviewRow>();
        foreach (var c in await CompaniesAsync(ct))
        {
            var (links, _) = await CompanyRolesAsync(c.Id, ct);
            foreach (var m in memberships.Where(x => x.CompanyId == c.Id))
            {
                if (!users.TryGetValue(m.UserId, out var u)) continue;
                rows.Add(new AccessReviewRow(c.Code, c.Name, u.Username, u.DisplayName, u.Email, u.IsActive, m.IsActive, m.IsDefault, u.IsPlatformAdmin,
                    string.Join(", ", links.Where(l => l.UserId == u.UserId).Select(l => l.RoleName).OrderBy(n => n)), u.LastLoginDate));
            }
        }
        return rows.OrderBy(r => r.CompanyCode).ThenBy(r => r.Username).ToList();
    }
}
