using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Persistence.Errors;

namespace AMGH.ITInventory.Persistence.Repositories;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly AMGHDbContext _db;
    private readonly Dictionary<Type, object> _repositories = new();

    public UnitOfWork(AMGHDbContext db) => _db = db;

    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        if (!_repositories.TryGetValue(typeof(TEntity), out var repository))
        {
            repository = new Repository<TEntity>(_db);
            _repositories[typeof(TEntity)] = repository;
        }
        return (IRepository<TEntity>)repository;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (DatabaseExceptionTranslator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await ExecuteInTransactionAsync(async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (_db.Database.CurrentTransaction is not null)
            return await work(cancellationToken); // join the caller's transaction

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear(); // discard entities whose save was rolled back
            if (DatabaseExceptionTranslator.Translate(ex) is { } translated) throw translated;
            throw;
        }
    }
}
