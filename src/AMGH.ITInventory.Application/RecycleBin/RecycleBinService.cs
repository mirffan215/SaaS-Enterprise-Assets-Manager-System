using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Errors;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.RecycleBin;

public sealed record RecycleTypeInfo(string Type, string Title, int Count);

public sealed record RecycledItem(string Type, int Id, string Label, string? DeletedBy, DateTime? DeletedUtc);

/// <summary>
/// The recycle bin: records are never physically deleted, so a deleted asset, employee, location, etc. can be put back with one
/// click. Lists the signed-in company's soft-deleted rows (the company filter always applies) and restores them.
/// </summary>
public interface IRecycleBinService
{
    Task<IReadOnlyList<RecycleTypeInfo>> TypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecycledItem>> ListAsync(string type, CancellationToken cancellationToken = default);
    /// <summary>Restores one row (IsDeleted = false, DeletedBy/DeletedDate cleared) and writes the audit entries; returns its name.</summary>
    Task<Result<string>> RestoreAsync(string type, int id, CancellationToken cancellationToken = default);
}

internal sealed class RecycleBinService : IRecycleBinService
{
    internal const int CountCap = 1000;
    internal const int ListCap = 300;

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    private readonly IReadOnlyList<RecycleSource> _sources;

    public RecycleBinService(IUnitOfWork uow, ICurrentUserService user, IDateTimeProvider clock)
    {
        _uow = uow;
        _user = user;
        _clock = clock;
        _sources = new RecycleSource[]
        {
            new RecycleSource<Asset>("asset", "Assets", e => e.AssetId, e => e.AssetNumber + " · " + e.AssetName),
            new RecycleSource<Employee>("employee", "Employees", e => e.EmployeeId, e => e.EmployeeNumber + " · " + e.FirstName + " " + e.LastName),
            new RecycleSource<Department>("department", "Departments", e => e.DepartmentId, e => e.DepartmentCode + " · " + e.DepartmentName),
            new RecycleSource<Location>("location", "Locations", e => e.LocationId, e => e.LocationCode + " · " + e.LocationName),
            new RecycleSource<Building>("building", "Buildings", e => e.BuildingId, e => e.BuildingCode + " · " + e.BuildingName),
            new RecycleSource<Floor>("floor", "Floors", e => e.FloorId, e => e.FloorCode + " · " + e.FloorName),
            new RecycleSource<Room>("room", "Rooms", e => e.RoomId, e => e.RoomCode + " · " + e.RoomName),
            new RecycleSource<Vendor>("vendor", "Vendors", e => e.VendorId, e => e.VendorCode + " · " + e.VendorName),
            new RecycleSource<Manufacturer>("manufacturer", "Manufacturers", e => e.ManufacturerId, e => e.ManufacturerCode + " · " + e.ManufacturerName),
            new RecycleSource<AssetCategory>("category", "Asset categories", e => e.AssetCategoryId, e => e.CategoryCode + " · " + e.CategoryName),
            new RecycleSource<AssetSubCategory>("subcategory", "Sub-categories", e => e.AssetSubCategoryId, e => e.SubCategoryCode + " · " + e.SubCategoryName),
            new RecycleSource<Classification>("classification", "Classifications", e => e.ClassificationId, e => e.ClassificationCode + " · " + e.ClassificationName),
            new RecycleSource<SoftwareLicense>("license", "Software licences", e => e.SoftwareLicenseId, e => e.LicenseNumber + " · " + e.SoftwareName),
            new RecycleSource<AssetDocument>("document", "Attachments", e => e.AssetDocumentId, e => e.DocumentTitle)
        };
    }

    public async Task<IReadOnlyList<RecycleTypeInfo>> TypesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RecycleTypeInfo>();
        foreach (var s in _sources) result.Add(new RecycleTypeInfo(s.Type, s.Title, await s.CountAsync(_uow, cancellationToken)));
        return result;
    }

    public async Task<IReadOnlyList<RecycledItem>> ListAsync(string type, CancellationToken cancellationToken = default) =>
        _sources.FirstOrDefault(s => s.Type == type) is { } source ? await source.ListAsync(_uow, cancellationToken) : Array.Empty<RecycledItem>();

    public async Task<Result<string>> RestoreAsync(string type, int id, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.Type == type);
        if (source is null) return Result<string>.Failure(Error.NotFound("Unknown kind of record."));
        string label;
        try
        {
            label = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var restored = await source.RestoreAsync(_uow, id, _user.UserName, _clock.UtcNow, ct);
                if (restored is null) return string.Empty;
                // the entity change itself is audited by the data layer (Restore); this entry records who used the recycle bin, for what
                await _uow.Repository<AuditLog>().AddAsync(new AuditLog
                {
                    EntityName = "RecycleBin", EntityKey = $"{type}:{id.ToString(CultureInfo.InvariantCulture)}", Action = AuditAction.Restore,
                    NewValues = JsonSerializer.Serialize(new { type, id, item = restored }), PerformedBy = _user.UserName, IpAddress = _user.IpAddress,
                    CorrelationId = _user.CorrelationId, EventDate = _clock.UtcNow
                }, ct);
                await _uow.SaveChangesAsync(ct);
                return restored;
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is DuplicateValueException or DataIntegrityException)
        {
            return Result<string>.Failure(Error.Conflict("It cannot be restored because a record with the same code or number now exists. Rename or remove that one first."));
        }
        return label.Length == 0
            ? Result<string>.Failure(Error.NotFound("That record is not in the recycle bin (anymore)."))
            : Result<string>.Success(label);
    }

    private abstract class RecycleSource
    {
        protected RecycleSource(string type, string title)
        {
            Type = type;
            Title = title;
        }

        public string Type { get; }
        public string Title { get; }
        public abstract Task<int> CountAsync(IUnitOfWork uow, CancellationToken ct);
        public abstract Task<IReadOnlyList<RecycledItem>> ListAsync(IUnitOfWork uow, CancellationToken ct);
        public abstract Task<string?> RestoreAsync(IUnitOfWork uow, int id, string by, DateTime now, CancellationToken ct);
    }

    private sealed class RecycleSource<TEntity> : RecycleSource where TEntity : AuditableEntity
    {
        private readonly Expression<Func<TEntity, int>> _key;
        private readonly Expression<Func<TEntity, string>> _label;

        public RecycleSource(string type, string title, Expression<Func<TEntity, int>> key, Expression<Func<TEntity, string>> label) : base(type, title)
        {
            _key = key;
            _label = label;
        }

        public override async Task<int> CountAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await uow.Repository<TEntity>().QueryIncludingDeletedAsync(q => q.Where(e => e.IsDeleted).Take(CountCap).Select(e => 1), ct)).Count;

        public override async Task<IReadOnlyList<RecycledItem>> ListAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var rows = await uow.Repository<TEntity>().QueryIncludingDeletedAsync(q => q.Where(e => e.IsDeleted).OrderByDescending(e => e.DeletedDate).Take(ListCap)
                .Select(Row()), ct);
            return rows.Select(r => new RecycledItem(Type, r.Id, r.Label, r.DeletedBy, r.DeletedDate)).ToList();
        }

        // projects key, label and the deletion stamps in one expression the database can translate
        private Expression<Func<TEntity, Row>> Row()
        {
            var e = _key.Parameters[0];
            var label = new ReplaceParameter(_label.Parameters[0], e).Visit(_label.Body)!;
            var init = Expression.MemberInit(Expression.New(typeof(Row)),
                Expression.Bind(typeof(Row).GetProperty(nameof(RecycleBinService.Row.Id))!, _key.Body),
                Expression.Bind(typeof(Row).GetProperty(nameof(RecycleBinService.Row.Label))!, label),
                Expression.Bind(typeof(Row).GetProperty(nameof(RecycleBinService.Row.DeletedBy))!, Expression.Property(e, nameof(AuditableEntity.DeletedBy))),
                Expression.Bind(typeof(Row).GetProperty(nameof(RecycleBinService.Row.DeletedDate))!, Expression.Property(e, nameof(AuditableEntity.DeletedDate))));
            return Expression.Lambda<Func<TEntity, Row>>(init, e);
        }

        public override async Task<string?> RestoreAsync(IUnitOfWork uow, int id, string by, DateTime now, CancellationToken ct)
        {
            var key = Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(
                Expression.Equal(_key.Body, Expression.Constant(id)),
                Expression.Property(_key.Parameters[0], nameof(AuditableEntity.IsDeleted))), _key.Parameters[0]);
            var entity = (await uow.Repository<TEntity>().ListIncludingDeletedAsync(key, ct)).FirstOrDefault();
            if (entity is null) return null;
            entity.Restore(by, now);
            await uow.SaveChangesAsync(ct);
            return _label.Compile()(entity);
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
    }

    private sealed class ReplaceParameter : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;
        public ReplaceParameter(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }
        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }
}
