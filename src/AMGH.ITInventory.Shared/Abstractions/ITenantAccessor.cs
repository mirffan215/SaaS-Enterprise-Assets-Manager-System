namespace AMGH.ITInventory.Shared.Abstractions;

/// <summary>A customer (tenant): one dedicated database. Comes from the central tenant catalog.</summary>
/// <param name="Id">Catalog id.</param>
/// <param name="Code">Short unique code used in the sub-domain, e.g. "acme" for acme.example.com.</param>
/// <param name="Name">Display name.</param>
/// <param name="ConnectionString">Connection string of the tenant's own database.</param>
/// <param name="IsDemo">True for the sandbox tenant: destructive operations are blocked.</param>
public sealed record TenantInfo(int Id, string Code, string Name, string ConnectionString, bool IsDemo);

/// <summary>
/// The tenant (customer database) the current request or job works in. Resolved by the web pipeline from the
/// request (never from the signed-in user's input after sign-in). With <see cref="IsMultiTenant"/> false the
/// application runs in single-database mode with the configured default connection string.
/// </summary>
public interface ITenantAccessor
{
    /// <summary>True when the central tenant catalog is in use; then data access without a tenant is refused.</summary>
    bool IsMultiTenant { get; }

    /// <summary>The active tenant, or null (single-database mode, anonymous platform pages).</summary>
    TenantInfo? CurrentTenant { get; }

    /// <summary>Works in <paramref name="tenant"/> until the returned scope is disposed (background jobs, provisioning).</summary>
    IDisposable Enter(TenantInfo tenant);
}

/// <summary>Default implementation: one instance per request / job (scoped lifetime).</summary>
public sealed class TenantAccessor : ITenantAccessor
{
    private readonly Stack<TenantInfo?> _scopes = new();
    private TenantInfo? _resolved;

    public TenantAccessor(bool isMultiTenant = false) => IsMultiTenant = isMultiTenant;

    public bool IsMultiTenant { get; }

    public TenantInfo? CurrentTenant => _scopes.Count > 0 ? _scopes.Peek() : _resolved;

    /// <summary>Called by the tenant-resolution middleware.</summary>
    public void Set(TenantInfo? tenant) => _resolved = tenant;

    public IDisposable Enter(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        _scopes.Push(tenant);
        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private TenantAccessor? _owner;
        public Scope(TenantAccessor owner) => _owner = owner;
        public void Dispose()
        {
            if (_owner is null) return;
            _owner._scopes.Pop();
            _owner = null;
        }
    }
}
