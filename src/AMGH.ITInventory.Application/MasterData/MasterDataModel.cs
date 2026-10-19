using AMGH.ITInventory.Application.Common;

namespace AMGH.ITInventory.Application.MasterData;

public enum FieldKind
{
    Text,
    TextArea,
    Email,
    Url,
    WholeNumber,
    Date,
    Boolean,
    Select,
    EnumSelect
}

/// <summary>One field of a master-data form. <see cref="OptionsKey"/> names a lookup list for selects.</summary>
public sealed record FieldDefinition(
    string Name,
    string Label,
    FieldKind Kind = FieldKind.Text,
    bool Required = false,
    int MaxLength = 0,
    string? OptionsKey = null,
    bool ShowInList = false,
    bool LockedOnEdit = false,
    string? Help = null);

public sealed record MasterDataRow(int Id, IReadOnlyList<string?> Cells, bool IsActive);

/// <summary>Raw list values before select ids are turned into names.</summary>
public sealed record MasterDataRawRow(int Id, IReadOnlyDictionary<string, object?> Values, bool IsActive);

public sealed record MasterDataRawPage(IReadOnlyList<MasterDataRawRow> Rows, int TotalCount, int Page, int PageSize);

public sealed record MasterDataPage(IReadOnlyList<FieldDefinition> Columns, IReadOnlyList<MasterDataRow> Rows, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>Form values as strings keyed by field name (exactly what the browser posts).</summary>
public sealed record MasterDataRecord(int Id, IReadOnlyDictionary<string, string?> Values, string? RowVersion);

public sealed record MasterDataTypeInfo(string Key, string Title, string SingularTitle, string Description);

/// <summary>Parsed, typed form values handed to a definition's Apply method.</summary>
public sealed class FieldValues
{
    private readonly Dictionary<string, object?> _values;
    public FieldValues(Dictionary<string, object?> values) => _values = values;
    public string? Text(string name) => _values.GetValueOrDefault(name) as string;
    public string RequiredText(string name) => (string)_values[name]!;
    public int? WholeNumber(string name) => _values.GetValueOrDefault(name) as int?;
    public DateOnly? Date(string name) => _values.GetValueOrDefault(name) as DateOnly?;
    public bool Bool(string name) => _values.GetValueOrDefault(name) is true;
    public TEnum Enum<TEnum>(string name, TEnum fallback) where TEnum : struct, System.Enum =>
        _values.GetValueOrDefault(name) is TEnum e ? e : fallback;
}

public sealed record SelectOptions(string Key, IReadOnlyList<LookupOption> Options);
