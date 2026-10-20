using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Security.Accounts;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class GlobalSignInTests
{
    /// <summary>Accepts "pw-<tenant code>" for the user "bob" in the tenants listed in <see cref="Members"/>; "locked" is locked out in tenant "c".</summary>
    private sealed class FakeAccounts : IAccountService
    {
        public static readonly HashSet<string> Members = new() { "a", "c" };
        private readonly ITenantAccessor _tenants;
        public FakeAccounts(ITenantAccessor tenants) => _tenants = tenants;

        public Task<SignInOutcome> SignInAsync(string username, string password, string? ipAddress, string? userAgent, string? companyCode = null, CancellationToken cancellationToken = default)
        {
            var code = _tenants.CurrentTenant!.Code;   // the scope must be inside a tenant, or this throws
            if (username == "locked" && code == "c") return Task.FromResult(SignInOutcome.Locked(DateTime.UtcNow.AddMinutes(5)));
            if (username == "bob" && Members.Contains(code) && (password == "pw-" + code || password == "shared"))
                return Task.FromResult(SignInOutcome.Success(new AuthenticatedUser(1, "bob", "Bob", Guid.NewGuid(), false, Array.Empty<string>(), Array.Empty<string>(), 1, code.ToUpperInvariant(), code, false)));
            return Task.FromResult(SignInOutcome.Fail("nope"));
        }
        public Task<SignInOutcome> SignInExternalAsync(string email, string companyCode, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthenticatedUser?> CreateSessionAsync(int userId, int companyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AuthenticatedUser>> ChangePasswordAsync(int userId, int companyId, string currentPassword, string newPassword, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ValidateSecurityStampAsync(int userId, int companyId, Guid securityStamp, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogEventAsync(int? userId, int? companyId, string username, LoginEventType eventType, string? ipAddress, string? userAgent, string? details = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CompanyMembership>> GetMembershipsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCatalog : ITenantCatalog
    {
        private readonly List<TenantRecord> _rows;
        public FakeCatalog(IEnumerable<TenantRecord> rows) => _rows = rows.ToList();
        public Task<TenantInfo?> FindActiveAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(_rows.FirstOrDefault(r => r.Code == code)?.ToInfo());
        public Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(_rows.Any(r => r.Code == code));
        public Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantRecord>>(_rows);
        public Task<TenantRecord> AddAsync(string code, string name, string connectionString, bool isDemo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static TenantRecord T(string code, bool active = true, bool demo = false) => new(1, code, code.ToUpperInvariant(), "cs", demo, active, DateTime.UtcNow);

    private static TenantSignInService Service(params TenantRecord[] tenants)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantAccessor>(_ => new TenantAccessor(true));
        services.AddScoped<ITenantCatalog>(_ => new FakeCatalog(tenants));
        services.AddScoped<IAccountService, FakeAccounts>();
        return new TenantSignInService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task The_person_is_signed_in_to_the_only_company_where_the_password_is_right()
    {
        var result = await Service(T("a"), T("b"), T("c")).SignInAsync("bob", "pw-c", "10.0.0.1", "agent", CancellationToken.None);
        var win = Assert.Single(result.Successes);
        Assert.Equal("c", win.Tenant.Code);
    }

    [Fact]
    public async Task A_wrong_password_gives_the_same_generic_message_everywhere()
    {
        var result = await Service(T("a"), T("b"), T("c")).SignInAsync("bob", "wrong", null, null, CancellationToken.None);
        Assert.Empty(result.Successes);
        Assert.Equal(TenantSignInService.GenericFailure, result.Message);
        var unknown = await Service(T("a"), T("b")).SignInAsync("nobody", "x", null, null, CancellationToken.None);
        Assert.Equal(TenantSignInService.GenericFailure, unknown.Message);
    }

    [Fact]
    public async Task Details_that_work_in_two_companies_report_both_so_the_person_can_choose()
    {
        var result = await Service(T("a"), T("b"), T("c")).SignInAsync("bob", "shared", null, null, CancellationToken.None);
        Assert.Equal(new[] { "a", "c" }, result.Successes.Select(w => w.Tenant.Code).OrderBy(c => c));
    }

    [Fact]
    public async Task Inactive_and_demo_companies_are_not_searched()
    {
        var result = await Service(T("a", active: false), T("c", demo: true)).SignInAsync("bob", "pw-a", null, null, CancellationToken.None);
        Assert.Empty(result.Successes);
    }

    [Fact]
    public async Task A_lock_out_is_reported_instead_of_the_generic_message()
    {
        var result = await Service(T("a"), T("c")).SignInAsync("locked", "x", null, null, CancellationToken.None);
        Assert.Empty(result.Successes);
        Assert.NotEqual(TenantSignInService.GenericFailure, result.Message);
    }

    [Fact]
    public async Task Too_many_companies_are_not_searched()
    {
        var many = Enumerable.Range(0, TenantSignInService.MaxCompanies + 1).Select(i => T("t" + i)).ToArray();
        var result = await Service(many).SignInAsync("bob", "pw", null, null, CancellationToken.None);
        Assert.True(result.TooManyCompanies);
        Assert.Empty(result.Successes);
    }
}
