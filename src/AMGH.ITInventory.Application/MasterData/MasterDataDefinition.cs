using System.Globalization;
using System.Linq.Expressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Common;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.MasterData;

public interface IMasterDataDefinition
{
    MasterDataTypeInfo Info { get; }
    IReadOnlyList<FieldDefinition> Fields { get; }
    bool AllowCreate { get; }
    Task<MasterDataRawPage> ListAsync(IUnitOfWork uow, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct);
    Task<MasterDataRecord?> GetAsync(IUnitOfWork uow, int id, CancellationToken ct);
    Task<Result<int>> SaveAsync(IUnitOfWork uow, int? id, IReadOnlyDictionary<string, string?> form, string? rowVersion, CancellationToken ct);
    Task<Result> DeleteAsync(IUnitOfWork uow, int id, string? rowVersion, CancellationToken ct);
}

/// <summary>
/// Generic list / create / edit / delete for one master-data entity. Subclasses declare fields,
/// how to read and write them, uniqueness rules and "in use" checks (in-use records cannot be deleted).
/// </summary>
public abstract class MasterDataDefinition<TEntity> : IMasterDataDefinition where TEntity : AuditableEntity, new()
{
    public abstract MasterDataTypeInfo Info { get; }
    public abstract IReadOnlyList<FieldDefinition> Fields { get; }
    public virtual bool AllowCreate => true;

    protected abstract Expression<Func<TEntity, int>> Id { get; }
    protected abstract Expression<Func<TEntity, string>> SortKey { get; }
    protected abstract Expression<Func<TEntity, bool>> Search(string term);

    /// <summary>Current values for the form, formatted as strings (dates yyyy-MM-dd, ints invariant, bools "true"/"false").</summary>
    protected abstract IReadOnlyDictionary<string, object?> Read(TEntity entity);

    /// <summary>Writes parsed values to the entity.</summary>
    protected abstract void Apply(TEntity entity, FieldValues values, bool isNew);

    /// <summary>Uniqueness and cross-field rules. <paramref name="id"/> is null for new records.</summary>
    protected virtual Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues values, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<Error>());

    /// <summary>Returns a reason when the record is referenced and must not be deleted; otherwise null.</summary>
    protected virtual Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>Runs after the form is applied and before saving (e.g. to create a related record in the same save).</summary>
    protected virtual Task PrepareAsync(IUnitOfWork uow, TEntity entity, FieldValues values, bool isNew, CancellationToken ct) => Task.CompletedTask;

    protected virtual bool IsActive(TEntity entity) => true;
    protected virtual Expression<Func<TEntity, bool>>? ActiveFilter => null;

    private Expression<Func<TEntity, bool>> IdEquals(int id)
    {
        var key = Id; // read once: body and parameter must come from the same lambda instance
        return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(key.Body, Expression.Constant(id)), key.Parameters);
    }

    public async Task<MasterDataRawPage> ListAsync(IUnitOfWork uow, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        IQueryable<TEntity> Filter(IQueryable<TEntity> q)
        {
            q = q.NotDeleted();
            if (!includeInactive && ActiveFilter is not null) q = q.Where(ActiveFilter);
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(Search(search.Trim()));
            return q;
        }
        var repo = uow.Repository<TEntity>();
        var total = await repo.QueryCountAsync(Filter, ct);
        pageSize = Math.Clamp(pageSize, 10, 100);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var entities = await repo.QueryAsync(q => Filter(q).OrderBy(SortKey).Skip((page - 1) * pageSize).Take(pageSize), ct);

        var idFunc = Id.Compile();
        var rows = entities.Select(e => new MasterDataRawRow(idFunc(e), Read(e), IsActive(e))).ToList();
        return new MasterDataRawPage(rows, total, page, pageSize);
    }

    public async Task<MasterDataRecord?> GetAsync(IUnitOfWork uow, int id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var rows = await uow.Repository<TEntity>().QueryAsync(q => q.NotDeleted().Where(IdEquals(id)), ct);
        if (rows.Count == 0) return null;
        var values = Read(rows[0]).ToDictionary(kv => kv.Key, kv => Format(kv.Value));
        return new MasterDataRecord(id, values, Convert.ToBase64String(rows[0].RowVersion));
    }

    public async Task<Result<int>> SaveAsync(IUnitOfWork uow, int? id, IReadOnlyDictionary<string, string?> form, string? rowVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(form);
        if (id is null && !AllowCreate) return Result<int>.Failure(Error.Forbidden($"New {Info.SingularTitle.ToLowerInvariant()} records cannot be added here."));

        var repo = uow.Repository<TEntity>();
        TEntity? entity = null;
        if (id is not null)
        {
            entity = await repo.FirstOrDefaultAsync(IdEquals(id.Value), ct);
            if (entity is null || entity.IsDeleted) return Result<int>.Failure(Error.NotFound("This record no longer exists."));
        }

        var (values, errors) = Parse(form, entity is null ? null : Read(entity), isNew: id is null);
        if (errors.Count == 0) errors.AddRange(await ValidateAsync(uow, id, values, ct));
        if (errors.Count > 0) return Result<int>.Failure(errors.ToArray());

        if (entity is null)
        {
            entity = new TEntity();
            Apply(entity, values, isNew: true);
            await PrepareAsync(uow, entity, values, isNew: true, ct);
            await repo.AddAsync(entity, ct);
        }
        else
        {
            if (string.IsNullOrEmpty(rowVersion)) return Result<int>.Failure(Error.Validation("The form is out of date. Reload and try again."));
            try { repo.SetOriginalRowVersion(entity, Convert.FromBase64String(rowVersion)); }
            catch (FormatException) { return Result<int>.Failure(Error.Validation("The form is out of date. Reload and try again.")); }
            Apply(entity, values, isNew: false);
            await PrepareAsync(uow, entity, values, isNew: false, ct);
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Assets.AssetService.ToError(ex) is { } error)
        {
            return Result<int>.Failure(error);
        }
        return Result<int>.Success(Id.Compile()(entity));
    }

    public async Task<Result> DeleteAsync(IUnitOfWork uow, int id, string? rowVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var repo = uow.Repository<TEntity>();
        var entity = await repo.FirstOrDefaultAsync(IdEquals(id), ct);
        if (entity is null || entity.IsDeleted) return Result.Failure(Error.NotFound("This record no longer exists."));
        if (await InUseAsync(uow, id, ct) is { } reason)
            return Result.Failure(Error.BusinessRule($"This {Info.SingularTitle.ToLowerInvariant()} cannot be deleted: {reason} Mark it inactive instead."));
        if (!string.IsNullOrEmpty(rowVersion))
        {
            try { repo.SetOriginalRowVersion(entity, Convert.FromBase64String(rowVersion)); }
            catch (FormatException) { return Result.Failure(Error.Validation("The page is out of date. Reload and try again.")); }
        }
        repo.Remove(entity);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Assets.AssetService.ToError(ex) is { } error)
        {
            return Result.Failure(error);
        }
        return Result.Success();
    }

    // ------------------------------------------------------------------ parsing

    private (FieldValues Values, List<Error> Errors) Parse(IReadOnlyDictionary<string, string?> form,
        IReadOnlyDictionary<string, object?>? current, bool isNew)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var errors = new List<Error>();
        foreach (var f in Fields)
        {
            if (!isNew && f.LockedOnEdit)
            {
                values[f.Name] = current?.GetValueOrDefault(f.Name);
                continue;
            }
            var raw = form.GetValueOrDefault(f.Name);
            var text = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

            if (f.Kind == FieldKind.Boolean)
            {
                values[f.Name] = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                                 || (raw?.Split(',').Any(p => string.Equals(p.Trim(), "true", StringComparison.OrdinalIgnoreCase)) ?? false);
                continue;
            }
            if (text is null)
            {
                if (f.Required) errors.Add(Error.Validation($"{f.Label} is required.", f.Name));
                values[f.Name] = null;
                continue;
            }
            if (f.MaxLength > 0 && text.Length > f.MaxLength)
            {
                errors.Add(Error.Validation($"{f.Label} must be {f.MaxLength} characters or fewer.", f.Name));
                continue;
            }
            switch (f.Kind)
            {
                case FieldKind.WholeNumber:
                case FieldKind.Select:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) values[f.Name] = n;
                    else errors.Add(Error.Validation($"{f.Label} must be a whole number.", f.Name));
                    break;
                case FieldKind.Date:
                    if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) values[f.Name] = d;
                    else errors.Add(Error.Validation($"{f.Label} must be a valid date.", f.Name));
                    break;
                case FieldKind.Email:
                    if (text.Contains('@', StringComparison.Ordinal) && text.LastIndexOf('.') > text.IndexOf('@', StringComparison.Ordinal) && !text.Contains(' ', StringComparison.Ordinal))
                        values[f.Name] = text;
                    else errors.Add(Error.Validation($"Enter a valid e-mail address for {f.Label}.", f.Name));
                    break;
                case FieldKind.Url:
                    if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                        values[f.Name] = text;
                    else errors.Add(Error.Validation($"{f.Label} must start with https://", f.Name));
                    break;
                case FieldKind.EnumSelect:
                    var enumType = ResolveEnum(f.OptionsKey);
                    if (enumType is not null && Enum.TryParse(enumType, text, ignoreCase: true, out var parsed) && Enum.IsDefined(enumType, parsed!))
                        values[f.Name] = parsed;
                    else errors.Add(Error.Validation($"Select a valid {f.Label.ToLowerInvariant()}.", f.Name));
                    break;
                default:
                    values[f.Name] = text;
                    break;
            }
        }
        return (new FieldValues(values), errors);
    }

    private static Type? ResolveEnum(string? optionsKey) =>
        optionsKey is not null && optionsKey.StartsWith(Lookups.LookupKeys.EnumPrefix, StringComparison.Ordinal)
            ? typeof(AuditableEntity).Assembly.GetType("AMGH.ITInventory.Domain.Enums." + optionsKey[Lookups.LookupKeys.EnumPrefix.Length..])
            : null;

    internal static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };


    /// <summary>Upper-cases and trims a code so "hq " and "HQ" are the same code.</summary>
    protected static string Code(FieldValues v, string name) => v.RequiredText(name).ToUpperInvariant();

    protected static Error Duplicate(string label, string field) => Error.Conflict($"Another record already uses this {label}.", field);
}
