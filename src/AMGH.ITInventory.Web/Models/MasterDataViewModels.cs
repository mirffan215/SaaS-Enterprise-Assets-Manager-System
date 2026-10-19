using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Application.MasterData;

namespace AMGH.ITInventory.Web.Models;

public sealed class MasterDataListViewModel
{
    public required MasterDataTypeInfo Type { get; init; }
    public required MasterDataPage Page { get; init; }
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
    public bool CanManage { get; init; }
    public bool AllowCreate { get; init; }
}

public sealed class MasterDataFormViewModel
{
    public required MasterDataTypeInfo Type { get; init; }
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Options { get; init; }
    public int? Id { get; init; }
    public string? RowVersion { get; init; }
    public bool IsEdit => Id is not null;
    public string? Value(string name) => Values.TryGetValue(name, out var v) ? v : null;
}
