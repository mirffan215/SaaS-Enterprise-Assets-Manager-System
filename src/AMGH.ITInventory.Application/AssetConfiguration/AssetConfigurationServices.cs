using System.Globalization;
using System.Text.RegularExpressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.AssetConfiguration;

// ============================================================================ numbering

public sealed record NumberSeriesItem(int Id, string Code, string Prefix, string Separator, byte PaddingLength, long LastNumber, bool IsActive,
    string? Description, string NextExample, IReadOnlyList<string> Categories);

public sealed class NumberSeriesForm
{
    public string? Prefix { get; set; }
    public string Separator { get; set; } = "-";
    public byte PaddingLength { get; set; } = 5;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public interface INumberingService
{
    static readonly IReadOnlyList<string> Separators = new[] { "-", "/", ".", "_", "" };
    Task<IReadOnlyList<NumberSeriesItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(int seriesId, NumberSeriesForm form, CancellationToken cancellationToken = default);
    /// <summary>Moves the series forward so the next asset gets <paramref name="nextNumber"/>. Never backwards, never onto an issued number.</summary>
    Task<Result> SetNextNumberAsync(int seriesId, long nextNumber, CancellationToken cancellationToken = default);
    /// <summary>Same rule as dbo.fn_FormatAssetNumber.</summary>
    static string Format(string prefix, string code, long number, byte padding, string separator) =>
        string.Concat(prefix, separator, code, separator, number.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0'));
}

internal sealed partial class NumberingService : INumberingService
{
    [GeneratedRegex("^[A-Z0-9-]{1,20}$")] private static partial Regex PrefixPattern();
    private readonly IUnitOfWork _uow;

    public NumberingService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<NumberSeriesItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q.OrderBy(s => s.SortOrder).ThenBy(s => s.SequenceCode)
            .Select(s => new { s.AssetNumberSequenceId, s.SequenceCode, s.Prefix, s.Separator, s.PaddingLength, s.LastNumber, s.IsActive, s.Description,
                Categories = s.AssetCategories.Where(c => !c.IsDeleted).Select(c => c.CategoryName).ToList() }), cancellationToken);
        return rows.Select(s => new NumberSeriesItem(s.AssetNumberSequenceId, s.SequenceCode, s.Prefix, s.Separator, s.PaddingLength, s.LastNumber, s.IsActive,
            s.Description, INumberingService.Format(s.Prefix, s.SequenceCode, s.LastNumber + 1, s.PaddingLength, s.Separator), s.Categories)).ToList();
    }

    public async Task<Result> UpdateAsync(int seriesId, NumberSeriesForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        var s = await _uow.Repository<AssetNumberSequence>().FirstOrDefaultAsync(x => x.AssetNumberSequenceId == seriesId, cancellationToken);
        if (s is null) return Result.Failure(Error.NotFound("Number series not found."));
        var prefix = (form.Prefix ?? string.Empty).Trim().ToUpperInvariant();
        var errors = new List<Error>();
        if (!PrefixPattern().IsMatch(prefix)) errors.Add(Error.Validation("Prefix: 1-20 capital letters, digits or '-' (e.g. AMGH).", nameof(form.Prefix)));
        if (!INumberingService.Separators.Contains(form.Separator ?? string.Empty)) errors.Add(Error.Validation("Choose a separator from the list.", nameof(form.Separator)));
        if (form.PaddingLength is < 3 or > 10) errors.Add(Error.Validation("Number of digits: 3-10.", nameof(form.PaddingLength)));
        var sample = INumberingService.Format(prefix, s.SequenceCode, Math.Max(s.LastNumber + 1, 1), form.PaddingLength, form.Separator ?? string.Empty);
        if (sample.Length > 30) errors.Add(Error.Validation($"Generated numbers would be {sample.Length} characters ({sample}); the maximum is 30.", nameof(form.Prefix)));
        if (!form.IsActive && await _uow.Repository<AssetCategory>().AnyAsync(c => c.AssetNumberSequenceId == seriesId && c.IsActive, cancellationToken))
            errors.Add(Error.BusinessRule("Active categories use this series: move or deactivate them first.", nameof(form.IsActive)));
        if (form.Description?.Trim().Length > 200) errors.Add(Error.Validation("Description: up to 200 characters.", nameof(form.Description)));
        if (errors.Count > 0) return Result.Failure(errors.ToArray());
        // Issued numbers are never changed: a new prefix or separator applies to assets registered from now on.
        s.Prefix = prefix;
        s.Separator = form.Separator ?? string.Empty;
        s.PaddingLength = form.PaddingLength;
        s.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        s.IsActive = form.IsActive;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetNextNumberAsync(int seriesId, long nextNumber, CancellationToken cancellationToken = default)
    {
        var s = await _uow.Repository<AssetNumberSequence>().FirstOrDefaultAsync(x => x.AssetNumberSequenceId == seriesId, cancellationToken);
        if (s is null) return Result.Failure(Error.NotFound("Number series not found."));
        if (nextNumber <= s.LastNumber)
            return Result.Failure(Error.BusinessRule($"Numbers up to {s.LastNumber} may already be issued: the next number must be higher (sequences never go back).", "nextNumber"));
        if (nextNumber > 9_999_999_999) return Result.Failure(Error.Validation("Too large.", "nextNumber"));
        // also check the numbers actually in use with this series' current format (e.g. imported assets)
        var start = INumberingService.Format(s.Prefix, s.SequenceCode, 0, 0, s.Separator);
        start = start[..^1];     // prefix + separators, without the number
        var issued = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.AssetNumber.StartsWith(start)).Select(a => a.AssetNumber), cancellationToken);
        var highest = issued.Select(n => long.TryParse(n[start.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).DefaultIfEmpty(0).Max();
        if (nextNumber <= highest) return Result.Failure(Error.BusinessRule($"Number {highest} is already in use in this series: the next number must be higher.", "nextNumber"));
        s.LastNumber = nextNumber - 1;
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================================ starter categories (global templates)

public sealed record CategoryTemplate(string Code, string Name, int? UsefulLifeMonths, bool RequiresHostname, IReadOnlyList<string> SubCategories);

public sealed record CategoryOverview(int Id, string Code, string Name, string Series, string NextNumber, int SubCategories, int Assets, bool IsActive);

public interface IStarterCategoryService
{
    /// <summary>Templates shared by all companies (no company data). Copied into a company on request; each company then changes its own freely.</summary>
    static readonly IReadOnlyList<CategoryTemplate> Templates = new CategoryTemplate[]
    {
        new("IT", "IT Equipment", 48, false, Array.Empty<string>()),
        new("FA", "Furniture & Fixtures", 84, false, new[] { "Office Desk", "Office Chair", "Meeting Table", "Cabinet", "Sofa", "Workstation", "Reception Furniture" }),
        new("VEH", "Vehicles & Fleet", 60, false, new[] { "Sedan", "SUV", "Pickup", "Bus", "Truck", "Forklift", "Motorcycle" }),
        new("ELE", "Electrical & Electronic Equipment", 60, false, Array.Empty<string>()),
        new("MACH", "Machinery & Production Equipment", 120, false, new[] { "Production Machine", "Generator", "Compressor", "Pump", "Packaging Equipment" }),
        new("TOOL", "Tools & Instruments", 60, false, Array.Empty<string>()),
        new("BLD", "Buildings & Facilities", 300, false, Array.Empty<string>()),
        new("LAND", "Land & Property", null, false, Array.Empty<string>()),
        new("OFF", "Office Equipment", 60, false, Array.Empty<string>()),
        new("TEL", "Telecommunication Equipment", 60, false, Array.Empty<string>()),
        new("SEC", "Security & Surveillance Equipment", 60, false, Array.Empty<string>()),
        new("MED", "Medical & Laboratory Equipment", 84, false, Array.Empty<string>()),
        new("HVAC", "HVAC & Mechanical Systems", 180, false, Array.Empty<string>()),
        new("WH", "Warehouse & Material Handling", 120, false, Array.Empty<string>()),
        new("CON", "Construction Equipment", 120, false, Array.Empty<string>()),
        new("HOSP", "Hospitality Equipment", 84, false, Array.Empty<string>()),
        new("OTH", "Other Assets", 60, false, Array.Empty<string>())
    };

    Task<IReadOnlyList<CategoryOverview>> OverviewAsync(CancellationToken cancellationToken = default);
    /// <summary>Adds the chosen templates that the company does not have yet (by code), each with its own number series.</summary>
    Task<Result<int>> AddAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default);
}

internal sealed class StarterCategoryService : IStarterCategoryService
{
    private readonly IUnitOfWork _uow;
    private readonly ICompanyContext _company;

    public StarterCategoryService(IUnitOfWork uow, ICompanyContext company)
    {
        _uow = uow;
        _company = company;
    }

    public async Task<IReadOnlyList<CategoryOverview>> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _uow.Repository<AssetCategory>().QueryAsync(q => q.OrderBy(c => c.SortOrder).ThenBy(c => c.CategoryCode).Select(c => new
        {
            c.AssetCategoryId, c.CategoryCode, c.CategoryName, c.IsActive, s = c.AssetNumberSequence,
            Subs = c.SubCategories.Count(x => !x.IsDeleted), Assets = c.Assets.Count(x => !x.IsDeleted)
        }), cancellationToken);
        return rows.Select(r => new CategoryOverview(r.AssetCategoryId, r.CategoryCode, r.CategoryName, r.s.SequenceCode,
            INumberingService.Format(r.s.Prefix, r.s.SequenceCode, r.s.LastNumber + 1, r.s.PaddingLength, r.s.Separator), r.Subs, r.Assets, r.IsActive)).ToList();
    }

    public async Task<Result<int>> AddAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var ct = cancellationToken;
        var companyId = _company.CompanyId ?? throw new InvalidOperationException("No company is active.");
        var companyCode = (await _uow.Repository<Company>().QueryAsync(q => q.Where(c => c.CompanyId == companyId).Select(c => c.CompanyCode), ct)).Single();
        var wanted = IStarterCategoryService.Templates.Where(t => codes.Contains(t.Code, StringComparer.Ordinal)).ToList();
        if (wanted.Count == 0) return Result<int>.Failure(Error.Validation("Choose at least one category."));
        var haveCategories = (await _uow.Repository<AssetCategory>().QueryAsync(q => q.Select(c => c.CategoryCode), ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var series = (await _uow.Repository<AssetNumberSequence>().ListAsync(null, ct)).ToDictionary(s => s.SequenceCode, StringComparer.OrdinalIgnoreCase);
        var nextSort = (await _uow.Repository<AssetCategory>().QueryAsync(q => q.Select(c => c.SortOrder), ct)).DefaultIfEmpty(0).Max() + 10;
        var added = 0;
        await _uow.ExecuteInTransactionAsync(async tct =>
        {
            foreach (var t in wanted.Where(t => !haveCategories.Contains(t.Code)))
            {
                // its own series, e.g. AMGH-FA-00001 (an existing series with this code is reused, never reset)
                if (!series.TryGetValue(t.Code, out var s))
                {
                    s = new AssetNumberSequence { CompanyId = companyId, SequenceCode = t.Code, Prefix = companyCode, Separator = "-", PaddingLength = 5,
                        LastNumber = 0, Description = t.Name, SortOrder = nextSort, IsActive = true };
                    await _uow.Repository<AssetNumberSequence>().AddAsync(s, tct);
                    await _uow.SaveChangesAsync(tct);
                    series[t.Code] = s;
                }
                var c = new AssetCategory { CompanyId = companyId, CategoryCode = t.Code, CategoryName = t.Name, AssetNumberSequenceId = s.AssetNumberSequenceId,
                    DefaultUsefulLifeMonths = t.UsefulLifeMonths, RequiresHostname = t.RequiresHostname, SortOrder = nextSort, IsActive = true };
                await _uow.Repository<AssetCategory>().AddAsync(c, tct);
                await _uow.SaveChangesAsync(tct);
                var i = 0;
                foreach (var sub in t.SubCategories)
                {
                    var code = new string(sub.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(10).ToArray());
                    await _uow.Repository<AssetSubCategory>().AddAsync(new AssetSubCategory { CompanyId = companyId, AssetCategoryId = c.AssetCategoryId,
                        SubCategoryCode = code, SubCategoryName = sub, SortOrder = (++i) * 10, IsActive = true }, tct);
                }
                await _uow.SaveChangesAsync(tct);
                nextSort += 10;
                added++;
            }
        }, ct);
        return Result<int>.Success(added);
    }
}
