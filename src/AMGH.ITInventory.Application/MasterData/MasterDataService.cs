using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.MasterData;

public interface IMasterDataService
{
    IReadOnlyList<MasterDataTypeInfo> Types { get; }
    IMasterDataDefinition? Find(string key);
    Task<MasterDataPage> ListAsync(string key, string? search, bool includeInactive, int page, CancellationToken cancellationToken = default);
    Task<MasterDataRecord?> GetAsync(string key, int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SelectOptions>> GetFormOptionsAsync(string key, CancellationToken cancellationToken = default);
    Task<Result<int>> SaveAsync(string key, int? id, IReadOnlyDictionary<string, string?> form, string? rowVersion, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(string key, int id, string? rowVersion, CancellationToken cancellationToken = default);
}

internal sealed class MasterDataService : IMasterDataService
{
    private const int PageSize = 25;

    private readonly IUnitOfWork _uow;
    private readonly ILookupService _lookups;
    private readonly Dictionary<string, IMasterDataDefinition> _definitions;

    public MasterDataService(IUnitOfWork uow, ILookupService lookups, IEnumerable<IMasterDataDefinition> definitions)
    {
        _uow = uow;
        _lookups = lookups;
        _definitions = definitions.ToDictionary(d => d.Info.Key, StringComparer.OrdinalIgnoreCase);
        Types = _definitions.Values.Select(d => d.Info).ToList();
    }

    public IReadOnlyList<MasterDataTypeInfo> Types { get; }

    public IMasterDataDefinition? Find(string key) => _definitions.GetValueOrDefault(key ?? string.Empty);

    private IMasterDataDefinition Get(string key) =>
        Find(key) ?? throw new KeyNotFoundException($"Unknown master-data type '{key}'.");

    public async Task<MasterDataPage> ListAsync(string key, string? search, bool includeInactive, int page, CancellationToken cancellationToken = default)
    {
        var def = Get(key);
        var raw = await def.ListAsync(_uow, search, includeInactive, page, PageSize, cancellationToken);
        var columns = def.Fields.Where(f => f.ShowInList).ToList();

        // Turn select ids into names (one lookup query per list column).
        var names = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        foreach (var f in columns.Where(c => c.Kind == FieldKind.Select && c.OptionsKey is not null))
            names[f.Name] = (await _lookups.GetOptionsAsync(f.OptionsKey!, cancellationToken)).ToDictionary(o => o.Id, o => o.Text);

        var rows = raw.Rows.Select(r => new MasterDataRow(r.Id, columns.Select(c =>
        {
            var v = r.Values.GetValueOrDefault(c.Name);
            return c.Kind switch
            {
                FieldKind.Select when v is int id => names.TryGetValue(c.Name, out var map) && map.TryGetValue(id, out var text) ? text : $"#{id}",
                FieldKind.EnumSelect => DisplayText.Humanize(v?.ToString()),
                FieldKind.Boolean => v is true ? "Yes" : "No",
                FieldKind.Date when v is DateOnly d => d.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                _ => MasterDataDefinition<Domain.Entities.Location>.Format(v)
            };
        }).ToList(), r.IsActive)).ToList();

        return new MasterDataPage(columns, rows, raw.TotalCount, raw.Page, raw.PageSize);
    }

    public Task<MasterDataRecord?> GetAsync(string key, int id, CancellationToken cancellationToken = default) =>
        Get(key).GetAsync(_uow, id, cancellationToken);

    public async Task<IReadOnlyList<SelectOptions>> GetFormOptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = new List<SelectOptions>();
        foreach (var f in Get(key).Fields.Where(f => f.Kind is FieldKind.Select or FieldKind.EnumSelect && f.OptionsKey is not null))
            result.Add(new SelectOptions(f.Name, await _lookups.GetOptionsAsync(f.OptionsKey!, cancellationToken)));
        return result;
    }

    public Task<Result<int>> SaveAsync(string key, int? id, IReadOnlyDictionary<string, string?> form, string? rowVersion,
        CancellationToken cancellationToken = default) =>
        Get(key).SaveAsync(_uow, id, form, rowVersion, cancellationToken);

    public Task<Result> DeleteAsync(string key, int id, string? rowVersion, CancellationToken cancellationToken = default) =>
        Get(key).DeleteAsync(_uow, id, rowVersion, cancellationToken);
}
