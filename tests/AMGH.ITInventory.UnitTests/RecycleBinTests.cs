using System.Linq.Expressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.RecycleBin;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class RecycleBinTests
{
    private sealed class FakeRepository<T> : IRepository<T> where T : class
    {
        public List<T> Rows { get; } = new();
        public IQueryable<T> Query() => Rows.AsQueryable();
        public IQueryable<T> QueryNoTracking() => Rows.AsQueryable();
        public IQueryable<T> QueryIncludingDeleted() => Rows.AsQueryable();
        public Task<List<TResult>> QueryAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken cancellationToken = default) =>
            Task.FromResult(shape(Rows.AsQueryable()).ToList());
        public Task<List<TResult>> QueryIncludingDeletedAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> shape, CancellationToken cancellationToken = default) =>
            Task.FromResult(shape(Rows.AsQueryable()).ToList());
        public Task<int> QueryCountAsync(Func<IQueryable<T>, IQueryable<T>> filter, CancellationToken cancellationToken = default) => Task.FromResult(filter(Rows.AsQueryable()).Count());
        public Task<List<T>> ListIncludingDeletedAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.AsQueryable().Where(predicate).ToList());
        public ValueTask<T?> FindAsync(object[] keyValues, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Rows.AsQueryable().FirstOrDefault(predicate));
        public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Rows.AsQueryable().Any(predicate));
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(predicate is null ? Rows.Count : Rows.AsQueryable().Count(predicate));
        public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(predicate is null ? Rows.ToList() : Rows.AsQueryable().Where(predicate).ToList());
        public Task AddAsync(T entity, CancellationToken cancellationToken = default) { Rows.Add(entity); return Task.CompletedTask; }
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { Rows.AddRange(entities); return Task.CompletedTask; }
        public void Update(T entity) { }
        public void Remove(T entity) => Rows.Remove(entity);
        public void SetOriginalRowVersion(T entity, byte[] rowVersion) { }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = new();
        public IRepository<T> Repository<T>() where T : class
        {
            if (!_repositories.TryGetValue(typeof(T), out var repo)) _repositories[typeof(T)] = repo = new FakeRepository<T>();
            return (FakeRepository<T>)repo;
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) => work(cancellationToken);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default) => work(cancellationToken);
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId => 7;
        public string UserName => "admin";
        public string? IpAddress => "10.0.0.5";
        public string? CorrelationId => "corr-1";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
    }

    private static (RecycleBinService Service, FakeUnitOfWork Uow) Create()
    {
        var uow = new FakeUnitOfWork();
        var assets = (FakeRepository<Asset>)uow.Repository<Asset>();
        assets.Rows.Add(new Asset { AssetId = 1, AssetNumber = "IT-LAP-00001", AssetName = "Laptop A" });
        var gone = new Asset { AssetId = 2, AssetNumber = "IT-LAP-00002", AssetName = "Laptop B" };
        gone.SoftDelete("clerk", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        assets.Rows.Add(gone);
        var employee = new Employee { EmployeeId = 5, EmployeeNumber = "E1", FirstName = "Mia", LastName = "Khan" };
        employee.SoftDelete("clerk", new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc));
        ((FakeRepository<Employee>)uow.Repository<Employee>()).Rows.Add(employee);
        return (new RecycleBinService(uow, new FakeUser(), new FixedClock()), uow);
    }

    [Fact]
    public async Task Types_count_only_deleted_rows()
    {
        var (service, _) = Create();
        var types = await service.TypesAsync();
        Assert.Equal(1, types.Single(t => t.Type == "asset").Count);
        Assert.Equal(1, types.Single(t => t.Type == "employee").Count);
        Assert.Equal(0, types.Single(t => t.Type == "location").Count);
    }

    [Fact]
    public async Task List_returns_label_and_who_deleted_it_when()
    {
        var (service, _) = Create();
        var item = Assert.Single(await service.ListAsync("asset"));
        Assert.Equal(("asset", 2, "IT-LAP-00002 · Laptop B", "clerk"), (item.Type, item.Id, item.Label, item.DeletedBy));
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), item.DeletedUtc);
        Assert.Equal("E1 · Mia Khan", Assert.Single(await service.ListAsync("employee")).Label);
        Assert.Empty(await service.ListAsync("nonsense"));
    }

    [Fact]
    public async Task Restore_clears_the_deletion_stamps_and_writes_an_audit_entry()
    {
        var (service, uow) = Create();
        var result = await service.RestoreAsync("asset", 2);
        Assert.True(result.IsSuccess);
        Assert.Equal("IT-LAP-00002 · Laptop B", result.Value);

        var asset = ((FakeRepository<Asset>)uow.Repository<Asset>()).Rows.Single(a => a.AssetId == 2);
        Assert.False(asset.IsDeleted);
        Assert.Null(asset.DeletedBy);
        Assert.Null(asset.DeletedDate);
        Assert.Equal("admin", asset.ModifiedBy);

        var audit = Assert.Single(((FakeRepository<AuditLog>)uow.Repository<AuditLog>()).Rows);
        Assert.Equal((AuditAction.Restore, "RecycleBin", "asset:2", "admin"), (audit.Action, audit.EntityName, audit.EntityKey, audit.PerformedBy));
        Assert.Empty(await service.ListAsync("asset"));
    }

    [Fact]
    public async Task Restore_of_a_live_or_unknown_record_fails_without_changes()
    {
        var (service, uow) = Create();
        Assert.True((await service.RestoreAsync("asset", 1)).IsFailure);   // not deleted
        Assert.True((await service.RestoreAsync("asset", 99)).IsFailure);  // does not exist
        Assert.True((await service.RestoreAsync("nonsense", 1)).IsFailure);
        Assert.Empty(((FakeRepository<AuditLog>)uow.Repository<AuditLog>()).Rows);
    }
}
