namespace AMGH.ITInventory.Shared.Abstractions;

/// <summary>
/// The company (workspace) the current request or job works in. Comes from the signed-in session (never from
/// request data). The data layer filters every query by it and SQL Server Row-Level Security receives it on
/// every connection. With no company, company-owned data is invisible.
/// </summary>
public interface ICompanyContext
{
    /// <summary>The active company, or null (anonymous request, platform administration).</summary>
    int? CompanyId { get; }

    /// <summary>Display name of the active company when known (from the session), else null.</summary>
    string? CurrentCompanyName { get; }

    /// <summary>True inside <see cref="EnterPlatform"/>: platform-level rows (CompanyId NULL in logs) are accessible.</summary>
    bool IsPlatform { get; }

    /// <summary>Works in <paramref name="companyId"/> until the returned scope is disposed (sign-in, background jobs).</summary>
    IDisposable Enter(int companyId);

    /// <summary>Platform scope: no company data, platform-level log rows only (e.g. a failed sign-in of an unknown user).</summary>
    IDisposable EnterPlatform();
}

/// <summary>
/// Default implementation: the ambient company (from the session) plus nested temporary scopes.
/// One instance per request / job (scoped lifetime).
/// </summary>
public sealed class CompanyContext : ICompanyContext
{
    private readonly Func<int?> _ambient;
    private readonly Func<string?> _ambientName;
    private readonly Stack<(int? Company, bool Platform)> _scopes = new();

    public CompanyContext(Func<int?>? ambient = null, Func<string?>? ambientName = null)
    {
        _ambient = ambient ?? (() => null);
        _ambientName = ambientName ?? (() => null);
    }

    public string? CurrentCompanyName => _scopes.Count > 0 ? null : (_ambientName() is { Length: > 0 } n ? n : null);

    public int? CompanyId => _scopes.Count > 0 ? _scopes.Peek().Company : _ambient();

    public bool IsPlatform => _scopes.Count > 0 && _scopes.Peek().Platform;

    public IDisposable Enter(int companyId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);
        _scopes.Push((companyId, false));
        return new Scope(this);
    }

    public IDisposable EnterPlatform()
    {
        _scopes.Push((null, true));
        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private CompanyContext? _owner;
        public Scope(CompanyContext owner) => _owner = owner;
        public void Dispose()
        {
            if (_owner is null) return;
            _owner._scopes.Pop();
            _owner = null;
        }
    }
}
