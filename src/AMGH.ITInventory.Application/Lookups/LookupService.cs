using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Assets;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Application.Lookups;

public interface ILookupService
{
    Task<AssetFormLookups> GetAssetFormLookupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Options for a named list (used by master-data forms), e.g. "departments", "buildings".</summary>
    Task<IReadOnlyList<LookupOption>> GetOptionsAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Drop-down data. Only active, non-deleted values are offered for new selections.</summary>
internal sealed class LookupService : ILookupService
{
    private readonly IUnitOfWork _uow;

    public LookupService(IUnitOfWork uow) => _uow = uow;

    public async Task<AssetFormLookups> GetAssetFormLookupsAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        return new AssetFormLookups(
            await GetOptionsAsync(LookupKeys.Categories, ct),
            await GetOptionsAsync(LookupKeys.SubCategories, ct),
            await GetOptionsAsync(LookupKeys.Statuses, ct),
            await GetOptionsAsync(LookupKeys.Manufacturers, ct),
            await GetOptionsAsync(LookupKeys.Vendors, ct),
            await GetOptionsAsync(LookupKeys.Currencies, ct),
            await GetOptionsAsync(LookupKeys.Departments, ct),
            await GetOptionsAsync(LookupKeys.Employees, ct),
            await GetOptionsAsync(LookupKeys.Locations, ct),
            await GetOptionsAsync(LookupKeys.Buildings, ct),
            await GetOptionsAsync(LookupKeys.Floors, ct),
            await GetOptionsAsync(LookupKeys.Rooms, ct),
            await GetOptionsAsync(LookupKeys.Classifications, ct),
            await GetOptionsAsync(LookupKeys.Criticalities, ct),
            await GetOptionsAsync(LookupKeys.RiskLevels, ct));
    }

    public async Task<IReadOnlyList<LookupOption>> GetOptionsAsync(string key, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        return key switch
        {
            LookupKeys.Categories => await _uow.Repository<AssetCategory>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.CategoryName)
                .Select(x => new LookupOption(x.AssetCategoryId, x.CategoryName, null, x.CategoryCode)), ct),
            LookupKeys.DisposalMethods => await _uow.Repository<DisposalMethod>().QueryAsync(q => q
                .Where(x => x.IsActive).OrderBy(x => x.SortOrder)
                .Select(x => new LookupOption(x.DisposalMethodId, x.MethodName + (x.RequiresCertificate ? " (certificate required)" : x.RequiresDataSanitization ? " (data sanitisation required)" : ""), null, x.MethodCode)), ct),
            LookupKeys.NumberSeries => await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.SequenceCode)
                .Select(x => new LookupOption(x.AssetNumberSequenceId, x.Prefix + "-" + x.SequenceCode + "-…  (" + (x.Description ?? x.SequenceCode) + ")", null, x.SequenceCode)), ct),
            LookupKeys.SubCategories => await _uow.Repository<AssetSubCategory>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.SubCategoryName)
                .Select(x => new LookupOption(x.AssetSubCategoryId, x.SubCategoryName, x.AssetCategoryId, x.SubCategoryCode)), ct),
            LookupKeys.Statuses => await _uow.Repository<AssetStatus>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.SortOrder)
                .Select(x => new LookupOption(x.AssetStatusId, x.StatusName, null, x.StatusCode)), ct),
            LookupKeys.Manufacturers => await _uow.Repository<Manufacturer>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.ManufacturerName)
                .Select(x => new LookupOption(x.ManufacturerId, x.ManufacturerName, null, x.ManufacturerCode)), ct),
            LookupKeys.Vendors => await _uow.Repository<Vendor>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.VendorName)
                .Select(x => new LookupOption(x.VendorId, x.VendorName, null, x.VendorCode)), ct),
            LookupKeys.Currencies => await _uow.Repository<Currency>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderByDescending(x => x.IsBaseCurrency).ThenBy(x => x.CurrencyCode)
                .Select(x => new LookupOption(x.CurrencyId, x.CurrencyCode + " - " + x.CurrencyName, null, x.CurrencyCode)), ct),
            LookupKeys.Departments => await _uow.Repository<Department>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.DepartmentName)
                .Select(x => new LookupOption(x.DepartmentId, x.DepartmentName, null, x.DepartmentCode)), ct),
            LookupKeys.Employees => await _uow.Repository<Employee>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.EmploymentStatus != EmploymentStatus.Terminated).OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
                .Select(x => new LookupOption(x.EmployeeId, x.FirstName + " " + x.LastName + " (" + x.EmployeeNumber + ")", x.DepartmentId, x.EmployeeNumber)), ct),
            LookupKeys.Locations => await _uow.Repository<Location>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.LocationName)
                .Select(x => new LookupOption(x.LocationId, x.LocationName, null, x.LocationCode)), ct),
            LookupKeys.Buildings => await _uow.Repository<Building>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.BuildingName)
                .Select(x => new LookupOption(x.BuildingId, x.BuildingName, x.LocationId, x.BuildingCode)), ct),
            LookupKeys.Floors => await _uow.Repository<Floor>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.FloorNumber).ThenBy(x => x.FloorName)
                .Select(x => new LookupOption(x.FloorId, x.FloorName, x.BuildingId, x.FloorCode)), ct),
            LookupKeys.Rooms => await _uow.Repository<Room>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.RoomName)
                .Select(x => new LookupOption(x.RoomId, x.RoomName, x.FloorId, x.RoomCode)), ct),
            LookupKeys.Classifications => await _uow.Repository<Classification>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.ClassificationLevel)
                .Select(x => new LookupOption(x.ClassificationId, x.ClassificationName, null, x.ClassificationCode)), ct),
            LookupKeys.Criticalities => await _uow.Repository<Criticality>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.CriticalityLevel)
                .Select(x => new LookupOption(x.CriticalityId, x.CriticalityName, null, x.CriticalityCode)), ct),
            LookupKeys.RiskLevels => await _uow.Repository<RiskLevel>().QueryAsync(q => q
                .Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.MinScore)
                .Select(x => new LookupOption(x.RiskLevelId, x.RiskLevelName, null, x.RiskLevelCode)), ct),
            // Parent options show their parent for clarity ("Head Office / Main Building").
            LookupKeys.BuildingsWithLocation => await _uow.Repository<Building>().QueryAsync(q => q
                .Where(x => !x.IsDeleted).OrderBy(x => x.Location.LocationName).ThenBy(x => x.BuildingName)
                .Select(x => new LookupOption(x.BuildingId, x.Location.LocationName + " / " + x.BuildingName, x.LocationId, x.BuildingCode)), ct),
            LookupKeys.FloorsWithBuilding => await _uow.Repository<Floor>().QueryAsync(q => q
                .Where(x => !x.IsDeleted).OrderBy(x => x.Building.Location.LocationName).ThenBy(x => x.Building.BuildingName).ThenBy(x => x.FloorNumber)
                .Select(x => new LookupOption(x.FloorId, x.Building.Location.LocationName + " / " + x.Building.BuildingName + " / " + x.FloorName, x.BuildingId, x.FloorCode)), ct),
            _ when key.StartsWith(LookupKeys.EnumPrefix, StringComparison.Ordinal) => EnumOptions(key[LookupKeys.EnumPrefix.Length..]),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown lookup list.")
        };
    }

    private static List<LookupOption> EnumOptions(string enumName)
    {
        var type = typeof(EmploymentStatus).Assembly.GetType($"AMGH.ITInventory.Domain.Enums.{enumName}")
                   ?? throw new ArgumentOutOfRangeException(nameof(enumName), enumName, "Unknown enum.");
        return Enum.GetValues(type).Cast<object>()
            .Select(v => new LookupOption(Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture), DisplayText.Humanize(v.ToString()!), null, v.ToString()))
            .ToList();
    }
}

public static class LookupKeys
{
    public const string Categories = "categories";
    public const string SubCategories = "subcategories";
    public const string NumberSeries = "numberseries";
    public const string DisposalMethods = "disposalmethods";
    public const string Statuses = "statuses";
    public const string Manufacturers = "manufacturers";
    public const string Vendors = "vendors";
    public const string Currencies = "currencies";
    public const string Departments = "departments";
    public const string Employees = "employees";
    public const string Locations = "locations";
    public const string Buildings = "buildings";
    public const string Floors = "floors";
    public const string Rooms = "rooms";
    public const string Classifications = "classifications";
    public const string Criticalities = "criticalities";
    public const string RiskLevels = "risklevels";
    public const string BuildingsWithLocation = "buildings-full";
    public const string FloorsWithBuilding = "floors-full";
    public const string EnumPrefix = "enum:";
}
