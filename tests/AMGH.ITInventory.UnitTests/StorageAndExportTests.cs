using AMGH.ITInventory.Application.Tenancy;
using AMGH.ITInventory.Infrastructure.Storage;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class StorageAndExportTests
{
    private static LocalFileStorage Storage(string root, TenantAccessor tenants) =>
        new(Options.Create(new StorageOptions { RootPath = root }), new CompanyContext(() => 1), tenants);

    [Fact]
    public async Task Two_tenants_with_the_same_company_id_never_share_a_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "eams-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new TenantAccessor(true); a.Set(new TenantInfo(1, "acme", "Acme", "cs", false));
            var b = new TenantAccessor(true); b.Set(new TenantInfo(2, "beta", "Beta", "cs", false));
            await Storage(root, a).SaveAsync("docs", "file1.bin", new MemoryStream(new byte[] { 1 }));
            await Storage(root, b).SaveAsync("docs", "file1.bin", new MemoryStream(new byte[] { 2 }));   // same name, same company id: must not collide

            Assert.True(File.Exists(Path.Combine(root, "t-acme", "1", "docs", "file1.bin")));
            Assert.True(File.Exists(Path.Combine(root, "t-beta", "1", "docs", "file1.bin")));
            await using var read = await Storage(root, b).OpenReadAsync("docs", "file1.bin");
            Assert.Equal(2, read!.ReadByte());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Single_database_mode_keeps_the_original_folder_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "eams-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Storage(root, new TenantAccessor(false)).SaveAsync("docs", "f.bin", new MemoryStream(new byte[] { 7 }));
            Assert.True(File.Exists(Path.Combine(root, "1", "docs", "f.bin")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task A_staged_file_can_be_deleted_once_its_stream_is_disposed()
    {
        // the import commit reads the staged workbook, then deletes it; Windows refuses to delete a file that is still open
        var root = Path.Combine(Path.GetTempPath(), "eams-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = Storage(root, new TenantAccessor(false));
            await storage.SaveAsync("_imports", "abc.xlsx", new MemoryStream(new byte[] { 1, 2, 3 }));
            var stream = await storage.OpenReadAsync("_imports", "abc.xlsx");
            await using var buffer = new MemoryStream();
            await stream!.CopyToAsync(buffer);
            await stream.DisposeAsync();
            await storage.DeleteAsync("_imports", "abc.xlsx");
            Assert.False(File.Exists(Path.Combine(root, "1", "_imports", "abc.xlsx")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class NoCatalog : ITenantCatalog
    {
        public Task<TenantInfo?> FindActiveAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<TenantInfo?>(null);
        public Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantRecord>>(Array.Empty<TenantRecord>());
        public Task<TenantRecord> AddAsync(string code, string name, string connectionString, bool isDemo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task An_unknown_company_in_a_browser_goes_back_to_the_picker_with_a_message()
    {
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Options.Create(new TenancyOptions { Enabled = true, AllowQueryParameter = true }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/Account/Login";
        ctx.Request.QueryString = new QueryString("?tenant=demo");
        ctx.Request.Headers.Accept = "text/html";
        await middleware.InvokeAsync(ctx, new TenantAccessor(true), new NoCatalog());
        Assert.Equal(302, ctx.Response.StatusCode);
        Assert.Equal("/Tenant/Select?notfound=demo", ctx.Response.Headers.Location.ToString());
    }
}
