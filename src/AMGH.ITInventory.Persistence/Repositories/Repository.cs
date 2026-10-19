using System.Linq.Expressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence.Repositories;

internal sealed class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly AMGHDbContext _db;
    private readonly DbSet<TEntity> _set;

    public Repository(AMGHDbContext db)
    {
        _db = db;
        _set = db.Set<TEntity>();
    }

    public IQueryable<TEntity> Query() => _set;

    private static readonly ParameterExpression Row = Expression.Parameter(typeof(TEntity), "e");

    /// <summary>Company condition for this entity type (null for platform-wide types).</summary>
    private IQueryable<TEntity> SameCompany(IQueryable<TEntity> source)
    {
        var body = Extensions.ModelBuilderExtensions.CompanyPredicate(typeof(TEntity), Row, _db);
        return body is null ? source : source.Where(Expression.Lambda<Func<TEntity, bool>>(body, Row));
    }

    public IQueryable<TEntity> QueryNoTracking() => _set.AsNoTracking();

    /// <summary>Includes soft-deleted rows; the company filter is re-applied (never bypassed).</summary>
    public IQueryable<TEntity> QueryIncludingDeleted() => SameCompany(_set.IgnoreQueryFilters()).AsNoTracking();

    public Task<List<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> shape, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape(_set.AsNoTracking()).ToListAsync(cancellationToken);
    }

    public Task<int> QueryCountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter(_set.AsNoTracking()).CountAsync(cancellationToken);
    }

    public Task<List<TEntity>> ListIncludingDeletedAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        SameCompany(_set.IgnoreQueryFilters()).Where(predicate).ToListAsync(cancellationToken);

    public ValueTask<TEntity?> FindAsync(object[] keyValues, CancellationToken cancellationToken = default) =>
        _set.FindAsync(keyValues, cancellationToken);

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        _set.FirstOrDefaultAsync(predicate, cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        _set.AnyAsync(predicate, cancellationToken);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null ? _set.CountAsync(cancellationToken) : _set.CountAsync(predicate, cancellationToken);

    public Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null
            ? _set.AsNoTracking().ToListAsync(cancellationToken)
            : _set.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _set.AddAsync(entity, cancellationToken);
    }

    public Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return _set.AddRangeAsync(entities, cancellationToken);
    }

    public void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Remove(entity); // converted to a soft delete in AMGHDbContext.SaveChanges
    }

    public void SetOriginalRowVersion(TEntity entity, byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(rowVersion);
        // Any entity with a mapped RowVersion concurrency token qualifies - not only AuditableEntity: Company,
        // CompanyBranding, CompanyUser and CompanyMailSettings have one too (Release 2.12 fix: company saves failed).
        var entry = _db.Entry(entity);
        var property = entry.Metadata.FindProperty(nameof(AuditableEntity.RowVersion));
        if (property is null || !property.IsConcurrencyToken)
            throw new InvalidOperationException($"{typeof(TEntity).Name} has no RowVersion column.");
        entry.Property(property.Name).OriginalValue = rowVersion;
    }
}
