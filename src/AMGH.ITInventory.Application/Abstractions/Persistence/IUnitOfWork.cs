namespace AMGH.ITInventory.Application.Abstractions.Persistence;

/// <summary>
/// Commits all repository changes as one transaction and translates database errors into
/// application exceptions (see AMGH.ITInventory.Shared.Errors).
/// </summary>
public interface IUnitOfWork
{
    IRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>Saves pending changes, stamps audit columns and writes the audit trail atomically.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> inside one database transaction (or joins the current one).
    /// Use when combining stored procedures with entity changes, e.g. number generation + insert.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}
