using System.Linq.Expressions;

namespace AMGH.ITInventory.Application.Abstractions.Persistence;

/// <summary>
/// Generic data access for one entity type. Deletes are always soft deletes
/// (the database denies DELETE to the application identity).
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>Tracked query; soft-deleted business records are excluded.</summary>
    IQueryable<TEntity> Query();

    /// <summary>Read-only query (no change tracking) for lists, grids and reports.</summary>
    IQueryable<TEntity> QueryNoTracking();

    /// <summary>Read-only query that also returns soft-deleted rows (audit / restore screens).</summary>
    IQueryable<TEntity> QueryIncludingDeleted();

    /// <summary>
    /// Runs a server-side query shaped by <paramref name="shape"/> (Where/OrderBy/Skip/Take/Select)
    /// without change tracking. Lets services page and project without referencing EF Core.
    /// </summary>
    Task<List<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> shape, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="QueryAsync{TResult}"/> but over all rows of the signed-in company including soft-deleted ones
    /// (the company filter is never bypassed). Used by the recycle bin.
    /// </summary>
    Task<List<TResult>> QueryIncludingDeletedAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> shape, CancellationToken cancellationToken = default);

    /// <summary>Server-side COUNT of a filtered query (pair with <see cref="QueryAsync{TResult}"/> for paging).</summary>
    Task<int> QueryCountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> filter, CancellationToken cancellationToken = default);

    /// <summary>Tracked list that includes soft-deleted rows (e.g. to re-activate a removed role link).</summary>
    Task<List<TEntity>> ListIncludingDeletedAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    ValueTask<TEntity?> FindAsync(object[] keyValues, CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    /// <summary>
    /// Marks the entity for deletion; SaveChanges converts it to a soft delete
    /// (IsDeleted = 1, DeletedBy, DeletedDate).
    /// </summary>
    void Remove(TEntity entity);

    /// <summary>
    /// Sets the original RowVersion so SaveChanges fails with a concurrency conflict if
    /// another user changed the row since the edit form was loaded.
    /// </summary>
    void SetOriginalRowVersion(TEntity entity, byte[] rowVersion);
}
