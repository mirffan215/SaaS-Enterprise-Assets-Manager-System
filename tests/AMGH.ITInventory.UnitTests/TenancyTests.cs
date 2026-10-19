using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class TenancyTests
{
    private static readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DatabaseName"] = "ITInv_Tenant_acme", ["MasterKeyPassword"] = "pw", ["AppLogin"] = "svc", ["AllowMultiCompany"] = "YES",
        ["AdminUsername"] = "admin", ["AdminEmail"] = "o'brien@acme.test", ["AdminDisplayName"] = "Administrator", ["AdminPasswordHash"] = "hash"
    };

    private static string ScriptsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "deployment", "SQLScripts"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "deployment", "SQLScripts");
    }

    [Fact]
    public void Every_baseline_script_splits_into_batches_without_unresolved_variables()
    {
        foreach (var name in TenantProvisioningService.ScriptOrder)
        {
            var batches = SqlScriptRunner.ToBatches(File.ReadAllText(Path.Combine(ScriptsFolder(), name)), Vars);
            Assert.NotEmpty(batches);
            Assert.All(batches, b => Assert.DoesNotContain("$(", b, StringComparison.Ordinal));
            Assert.All(batches, b => Assert.DoesNotContain("\nGO\n", b, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Variable_values_are_escaped_for_string_literals()
    {
        var batches = SqlScriptRunner.ToBatches("SELECT N'$(AdminEmail)';\nGO\nSELECT 2;", Vars);
        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT N'o''brien@acme.test';", batches[0]);
    }

    [Fact]
    public void Sqlcmd_commands_are_skipped_and_go_count_repeats()
    {
        var batches = SqlScriptRunner.ToBatches(":on error exit\n:setvar X 1\nSELECT 1;\nGO 2\n", Vars);
        Assert.Equal(new[] { "SELECT 1;", "SELECT 1;" }, batches);
    }

    [Fact]
    public void Tenant_accessor_scopes_nest_and_restore()
    {
        var accessor = new TenantAccessor(true);
        Assert.Null(accessor.CurrentTenant);
        var a = new TenantInfo(1, "a", "A", "cs-a", false);
        var b = new TenantInfo(2, "b", "B", "cs-b", true);
        accessor.Set(a);
        using (accessor.Enter(b)) Assert.Equal("b", accessor.CurrentTenant!.Code);
        Assert.Equal("a", accessor.CurrentTenant!.Code);
    }

    private static TenantResolutionMiddleware Middleware(bool query = false) =>
        new(_ => Task.CompletedTask, Options.Create(new TenancyOptions { Enabled = true, BaseDomain = "example.com", AllowQueryParameter = query }));

    private static DefaultHttpContext Request(string host, string? query = null, string? header = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        if (query is not null) ctx.Request.QueryString = new QueryString(query);
        if (header is not null) ctx.Request.Headers[TenantResolutionMiddleware.HeaderName] = header;
        return ctx;
    }

    [Theory]
    [InlineData("acme.example.com", "acme")]
    [InlineData("ACME.Example.com", "ACME")]
    [InlineData("example.com", null)]
    [InlineData("www.example.com", null)]
    [InlineData("a.b.example.com", null)]
    [InlineData("acme.other.com", null)]
    public void Subdomain_is_the_tenant(string host, string? expected) =>
        Assert.Equal(expected, Middleware().SubdomainOf(host));

    [Fact]
    public void Resolution_order_is_subdomain_then_query_then_header()
    {
        var (fromHost, _) = Middleware(true).Resolve(Request("acme.example.com", "?tenant=q", "h").Request);
        Assert.Equal("acme", fromHost);
        var (fromQuery, isQuery) = Middleware(true).Resolve(Request("localhost", "?tenant=q", "h").Request);
        Assert.Equal("q", fromQuery);
        Assert.True(isQuery);
        Assert.Equal("h", Middleware(true).Resolve(Request("localhost", null, "h").Request).Code);
    }

    [Fact]
    public void Query_parameter_is_ignored_unless_allowed() =>
        Assert.Null(Middleware(false).Resolve(Request("localhost", "?tenant=q").Request).Code);
}

public class TenantMiddlewarePublicPathTests
{
    private sealed class NoCatalog : ITenantCatalog
    {
        public Task<TenantInfo?> FindActiveAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<TenantInfo?>(null);
        public Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantRecord>>(Array.Empty<TenantRecord>());
        public Task<TenantRecord> AddAsync(string code, string name, string connectionString, bool isDemo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<(bool Reached, int Status, string? Location)> Run(string path)
    {
        var reached = false;
        var middleware = new TenantResolutionMiddleware(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new TenancyOptions { Enabled = true, AllowQueryParameter = true }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        ctx.Request.Headers.Accept = "text/html";
        await middleware.InvokeAsync(ctx, new TenantAccessor(true), new NoCatalog());
        return (reached, ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/Tenant/Select")]
    [InlineData("/Account/ExploreDemo")]
    [InlineData("/health")]
    [InlineData("/branding/default-logo.svg")]
    public async Task Pages_that_come_before_a_company_is_chosen_are_reachable(string path) =>
        Assert.True((await Run(path)).Reached);

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Assets")]
    public async Task Everything_else_sends_the_visitor_to_the_company_page(string path)
    {
        var (reached, status, location) = await Run(path);
        Assert.False(reached);
        Assert.Equal(302, status);
        Assert.Equal("/Tenant/Select", location);
    }
}
