namespace AMGH.ITInventory.Application.ConfigTransfer;

public sealed record SubCategoryConfig(string Code, string Name, string? Description, int SortOrder, bool IsActive);

public sealed record CategoryConfig(string Code, string Name, string SequenceCode, byte SequencePadding, int? DefaultUsefulLifeMonths,
    bool RequiresHostname, string? Description, int SortOrder, bool IsActive, IReadOnlyList<SubCategoryConfig> SubCategories);

public sealed record ClassificationConfig(string Code, string Name, byte Level, string? HandlingRequirements, string? ColorHex,
    string? Description, int SortOrder, bool IsActive);

public sealed record LocationConfig(string Code, string Name, string? AddressLine, string? City, string Country, bool IsActive);

public sealed record NumberingConfig(string Mode, byte FiscalYearStartMonth, string YearFormat, byte Digits);

/// <summary>The settings a company sets up once, independent of any database ids.</summary>
public sealed record ConfigurationDocument(string SourceCompanyCode, string SourceCompanyName, DateTime ExportedUtc,
    IReadOnlyList<CategoryConfig> Categories, IReadOnlyList<ClassificationConfig> Classifications, NumberingConfig? Numbering,
    IReadOnlyList<LocationConfig> Locations);

public enum ChangeKind { Added, Modified, Unchanged }

/// <summary>One field that differs between the company and the file.</summary>
public sealed record FieldChange(string Field, string? Current, string? Incoming);

/// <summary>
/// One row of the preview. <see cref="Key"/> identifies it ("category:LAP", "subcategory:LAP/STD", "classification:CONF",
/// "location:HQ", "numbering:company"); the commit receives only the keys the user ticked.
/// </summary>
public sealed record ConfigChange(string Key, string Section, string Title, ChangeKind Kind, IReadOnlyList<FieldChange> Fields);

public sealed record ConfigPreview(string Token, string Source, IReadOnlyList<ConfigChange> Changes, DateTime ExpiresUtc)
{
    public int Added => Changes.Count(c => c.Kind == ChangeKind.Added);
    public int Modified => Changes.Count(c => c.Kind == ChangeKind.Modified);
    public int Unchanged => Changes.Count(c => c.Kind == ChangeKind.Unchanged);
}

public sealed record ConfigImportResult(int Added, int Modified, int Skipped);
