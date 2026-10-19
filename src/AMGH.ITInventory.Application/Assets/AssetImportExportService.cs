using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;
using AMGH.ITInventory.Application.Abstractions.Storage;
using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Shared.Abstractions;
using AMGH.ITInventory.Shared.Results;
using FluentValidation;
using K = AMGH.ITInventory.Application.Abstractions.Spreadsheets.SheetColumnKind;

namespace AMGH.ITInventory.Application.Assets;

public sealed record ImportRowError(int RowNumber, string Message);

public sealed record ImportPreview(string? Token, int RowCount, IReadOnlyList<ImportRowError> Errors, IReadOnlyList<string> Notes)
{
    public bool CanImport => Token is not null && Errors.Count == 0 && RowCount > 0;
}

/// <summary>Import limits, shared with the screens (Release 2.14: assets raised from 1,000 to 10,000 rows per file).</summary>
public static class ImportLimits
{
    /// <summary>Rows per asset import file. All rows are saved in one transaction (all or nothing).</summary>
    public const int AssetRows = 10_000;
}

public interface IAssetImportExportService
{
    byte[] CreateTemplate(AssetAccess access, AssetFormLookups lookups);
    Task<byte[]> ExportAsync(AssetSearchCriteria criteria, AssetAccess access, CancellationToken cancellationToken = default);

    /// <summary>Validates an uploaded workbook. When it is clean, the file is kept and a token is returned for <see cref="CommitAsync"/>.</summary>
    Task<ImportPreview> PreviewAsync(Stream xlsx, AssetAccess access, CancellationToken cancellationToken = default);

    /// <summary>Re-validates the stored file and creates all assets in one transaction (all or nothing).</summary>
    Task<Result<IReadOnlyList<CreatedAsset>>> CommitAsync(string token, AssetAccess access, CancellationToken cancellationToken = default);
}

internal sealed class AssetImportExportService : IAssetImportExportService
{
    public const int MaxImportRows = ImportLimits.AssetRows;

    /// <summary>The template's sample row starts with this text and is refused if left in the file.</summary>
    internal const string ExampleMarker = "EXAMPLE:";
    public const int MaxExportRows = 50_000;

    // ---- import columns (header text is the contract; '*' marks required)
    private const string HCategory = "Category code*", HName = "Asset name*", HStatus = "Status code", HSub = "Sub-category code",
        HManufacturer = "Manufacturer code", HModel = "Model", HSerial = "Serial number", HTag = "Legacy asset tag", HDesc = "Description",
        HHost = "Hostname", HIp = "IP address", HMac = "MAC address", HOs = "Operating system", HOsVer = "OS version", HCpu = "Processor",
        HRam = "RAM (GB)", HStorage = "Storage (GB)", HFw = "Firmware version", HImei = "IMEI", HPhone = "Phone number",
        HVendor = "Vendor code", HPo = "PO number", HInvoice = "Invoice number", HPurchaseDate = "Purchase date", HCost = "Purchase cost",
        HCurrency = "Currency code", HLife = "Useful life (months)", HSalvage = "Salvage value",
        HDept = "Department code", HLoc = "Location code", HBld = "Building code", HFlr = "Floor code", HRoom = "Room code",
        HOwner = "Owner employee number", HClass = "Classification code", HCrit = "Criticality code", HRisk = "Risk level code",
        HSensitive = "Stores sensitive data (Y/N)", HEncrypted = "Disk encrypted (Y/N)", HEndpoint = "Endpoint protection (Y/N)",
        HMdm = "MDM managed (Y/N)", HBackup = "Backup required (Y/N)", HReview = "Last security review",
        HCommission = "Commission date", HEos = "End of support", HEol = "End of life", HAudit = "Next audit due", HNotes = "Notes";

    private static readonly SheetColumn[] GeneralColumns =
    {
        new(HCategory, K.Text, 16), new(HName, K.Text, 30), new(HStatus, K.Text, 14), new(HSub, K.Text, 18),
        new(HManufacturer, K.Text, 18), new(HModel, K.Text, 22), new(HSerial, K.Text, 20), new(HTag, K.Text, 16), new(HDesc, K.Text, 30),
        new(HHost, K.Text, 18), new(HIp, K.Text, 15), new(HMac, K.Text, 19), new(HOs, K.Text, 18), new(HOsVer, K.Text, 12),
        new(HCpu, K.Text, 20), new(HRam, K.Number, 10), new(HStorage, K.Number, 12), new(HFw, K.Text, 14), new(HImei, K.Text, 17),
        new(HPhone, K.Text, 15)
    };
    private static readonly SheetColumn[] FinancialColumns =
    {
        new(HVendor, K.Text, 14), new(HPo, K.Text, 14), new(HInvoice, K.Text, 14), new(HCost, K.Number, 14),
        new(HCurrency, K.Text, 12), new(HLife, K.WholeNumber, 12), new(HSalvage, K.Number, 12)
    };
    private static readonly SheetColumn[] PlacementColumns =
    {
        new(HPurchaseDate, K.Date, 13), new(HDept, K.Text, 14), new(HLoc, K.Text, 14), new(HBld, K.Text, 14), new(HFlr, K.Text, 12), new(HRoom, K.Text, 12)
    };
    private static readonly SheetColumn[] SecurityColumns =
    {
        new(HOwner, K.Text, 16), new(HClass, K.Text, 16), new(HCrit, K.Text, 14), new(HRisk, K.Text, 14),
        new(HSensitive, K.Text, 12), new(HEncrypted, K.Text, 12), new(HEndpoint, K.Text, 12), new(HMdm, K.Text, 12),
        new(HBackup, K.Text, 12), new(HReview, K.Date, 14)
    };
    private static readonly SheetColumn[] LifecycleColumns =
    {
        new(HCommission, K.Date, 13), new(HEos, K.Date, 13), new(HEol, K.Date, 13), new(HAudit, K.Date, 13), new(HNotes, K.Text, 30)
    };

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd.MM.yyyy" };

    private readonly IUnitOfWork _uow;
    private readonly IAssetNumberGenerator _numbers;
    private readonly IValidator<AssetEditModel> _validator;
    private readonly ISpreadsheetWriter _writer;
    private readonly ISpreadsheetReader _reader;
    private readonly IFileStorage _storage;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _user;

    public AssetImportExportService(IUnitOfWork uow, IAssetNumberGenerator numbers, IValidator<AssetEditModel> validator,
        ISpreadsheetWriter writer, ISpreadsheetReader reader, IFileStorage storage, IDateTimeProvider clock, ICurrentUserService user)
    {
        _user = user;
        _uow = uow;
        _numbers = numbers;
        _validator = validator;
        _writer = writer;
        _reader = reader;
        _storage = storage;
        _clock = clock;
    }

    private static List<SheetColumn> ImportColumns(AssetAccess access)
    {
        var cols = new List<SheetColumn>(GeneralColumns);
        cols.AddRange(PlacementColumns);
        if (access.CanViewFinancials) cols.AddRange(FinancialColumns);
        if (access.CanViewSecurityDetails) cols.AddRange(SecurityColumns);
        cols.AddRange(LifecycleColumns);
        return cols;
    }

    // ------------------------------------------------------------------ template

    public byte[] CreateTemplate(AssetAccess access, AssetFormLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(lookups);
        var columns = ImportColumns(access);
        var example = columns.Select(c => (object?)(c.Header switch
        {
            HCategory => "LAP", HName => ExampleMarker + " Dell Latitude 5440 (delete this row)", HStatus => "IN_STOCK", HManufacturer => "DELL",
            HModel => "Latitude 5440", HSerial => "7XK2Q73", HHost => "AMGH-LAP-0142", HOs => "Windows 11 Pro", HRam => 16m,
            HStorage => 512m, HPurchaseDate => _clock.UtcToday.AddDays(-30), HCost => 4850m, HCurrency => "SAR", HLife => 36,
            HDept => "IT", HLoc => "HQ", HBld => "MAIN", HFlr => "GF", HRoom => "ITSTORE", HClass => "INTERNAL",
            HCrit => "MEDIUM", HRisk => "LOW", HSensitive => "Y", HEncrypted => "Y", HEndpoint => "Y", HMdm => "Y", HBackup => "N",
            _ => null
        })).ToList();

        var help = new List<IReadOnlyList<object?>>
        {
            Row("How to use this template"),
            Row("1. Fill one row per asset on the 'Assets' sheet. Delete the example row first."),
            Row("2. Columns marked * are required. Use the codes listed below, not names."),
            Row("3. Dates: use an Excel date or the format yyyy-MM-dd. Yes/No columns: Y or N."),
            Row($"4. Up to {MaxImportRows} rows per file. Asset numbers are assigned automatically."),
            Row("5. Upload the file: nothing is created unless every row is valid."),
            Row("6. Custodians are not set here; use Assign after import so hand-over is recorded."),
            Row(),
            Row("Allowed status codes", string.Join(", ", AssetRules.InitialStatuses.OrderBy(s => s, StringComparer.Ordinal))),
        };
        void AddList(string title, IEnumerable<LookupOption> items, Func<LookupOption, string>? parent = null)
        {
            help.Add(Row());
            help.Add(Row(title, "Code", parent is null ? null : "Belongs to"));
            foreach (var i in items) help.Add(Row(i.Text, i.Code, parent?.Invoke(i)));
        }
        string Name(IReadOnlyList<LookupOption> list, int? id) => list.FirstOrDefault(x => x.Id == id)?.Code ?? string.Empty;

        AddList("Categories", lookups.Categories);
        AddList("Sub-categories", lookups.SubCategories, i => Name(lookups.Categories, i.ParentId));
        AddList("Manufacturers", lookups.Manufacturers);
        AddList("Departments", lookups.Departments);
        AddList("Locations", lookups.Locations);
        AddList("Buildings", lookups.Buildings, i => Name(lookups.Locations, i.ParentId));
        AddList("Floors", lookups.Floors, i => Name(lookups.Buildings, i.ParentId));
        AddList("Rooms", lookups.Rooms, i => Name(lookups.Floors, i.ParentId));
        if (access.CanViewFinancials)
        {
            AddList("Vendors", lookups.Vendors);
            AddList("Currencies", lookups.Currencies);
        }
        if (access.CanViewSecurityDetails)
        {
            AddList("Classifications", lookups.Classifications);
            AddList("Criticality", lookups.Criticalities);
            AddList("Risk levels", lookups.RiskLevels);
        }

        return _writer.Write(new[]
        {
            new SheetData("Assets", columns, new[] { example }),
            new SheetData("Codes and help", new[] { new SheetColumn("Item", K.Text, 50), new SheetColumn("Code", K.Text, 20), new SheetColumn("Belongs to", K.Text, 20) }, help)
        });
    }

    private static object?[] Row(params object?[] values) => values;

    // ------------------------------------------------------------------ export

    public async Task<byte[]> ExportAsync(AssetSearchCriteria criteria, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(access);
        var rows = await _uow.Repository<Asset>().QueryAsync(q => AssetQueries.Sort(AssetQueries.Filter(q, criteria), criteria.Sort, criteria.Descending)
            .Take(MaxExportRows)
            .Select(a => new
            {
                a.AssetNumber, a.AssetName,
                Category = a.AssetCategory.CategoryName,
                Sub = a.AssetSubCategory == null ? null : a.AssetSubCategory.SubCategoryName,
                Status = a.AssetStatus.StatusName,
                Manufacturer = a.Manufacturer == null ? null : a.Manufacturer.ManufacturerName,
                a.ModelName, a.SerialNumber, a.LegacyAssetTag, a.Hostname, a.IpAddress, a.MacAddress, a.OperatingSystem, a.OsVersion,
                a.Processor, a.RamGb, a.StorageGb,
                AssignedTo = a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName,
                EmployeeNo = a.AssignedEmployee == null ? null : a.AssignedEmployee.EmployeeNumber,
                Department = a.Department == null ? null : a.Department.DepartmentName,
                Location = a.Location == null ? null : a.Location.LocationName,
                Building = a.Building == null ? null : a.Building.BuildingName,
                Floor = a.Floor == null ? null : a.Floor.FloorName,
                Room = a.Room == null ? null : a.Room.RoomName,
                Vendor = a.Vendor == null ? null : a.Vendor.VendorName,
                a.PurchaseOrderNumber, a.InvoiceNumber, a.PurchaseDate, a.PurchaseCost,
                Currency = a.Currency == null ? null : a.Currency.CurrencyCode,
                a.UsefulLifeMonths, a.SalvageValue,
                Owner = a.AssetOwner == null ? null : a.AssetOwner.FirstName + " " + a.AssetOwner.LastName,
                Classification = a.Classification == null ? null : a.Classification.ClassificationName,
                Criticality = a.Criticality == null ? null : a.Criticality.CriticalityName,
                Risk = a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
                a.StoresSensitiveData, a.IsDiskEncrypted, a.EndpointProtectionInstalled, a.IsMdmManaged, a.BackupRequired, a.LastSecurityReviewDate,
                a.CommissionDate, a.EndOfSupportDate, a.EndOfLifeDate, a.NextAuditDueDate,
                WarrantyEnd = a.Warranties.Where(w => !w.IsDeleted).Max(w => (DateOnly?)w.EndDate),
                a.CreatedDate, a.CreatedBy
            }), cancellationToken);

        var columns = new List<SheetColumn>
        {
            new("Asset number", K.Text, 20), new("Asset name", K.Text, 30), new("Category", K.Text, 18), new("Sub-category", K.Text, 18),
            new("Status", K.Text, 16), new("Manufacturer", K.Text, 18), new("Model", K.Text, 20), new("Serial number", K.Text, 18),
            new("Legacy tag", K.Text, 14), new("Hostname", K.Text, 18), new("IP address", K.Text, 15), new("MAC address", K.Text, 19),
            new("Operating system", K.Text, 18), new("OS version", K.Text, 12), new("Processor", K.Text, 20), new("RAM (GB)", K.Number, 10),
            new("Storage (GB)", K.Number, 12), new("Assigned to", K.Text, 22), new("Employee no.", K.Text, 13), new("Department", K.Text, 18),
            new("Location", K.Text, 16), new("Building", K.Text, 16), new("Floor", K.Text, 12), new("Room", K.Text, 14), new("Purchase date", K.Date, 13)
        };
        if (access.CanViewFinancials)
            columns.AddRange(new SheetColumn[]
            {
                new("Vendor", K.Text, 18), new("PO number", K.Text, 14), new("Invoice number", K.Text, 14), new("Purchase cost", K.Number, 14),
                new("Currency", K.Text, 9), new("Useful life (months)", K.WholeNumber, 12), new("Salvage value", K.Number, 12), new("Book value", K.Number, 13)
            });
        if (access.CanViewSecurityDetails)
            columns.AddRange(new SheetColumn[]
            {
                new("Owner", K.Text, 20), new("Classification", K.Text, 15), new("Criticality", K.Text, 12), new("Risk level", K.Text, 12),
                new("Sensitive data", K.Text, 10), new("Disk encrypted", K.Text, 10), new("Endpoint protection", K.Text, 10),
                new("MDM managed", K.Text, 10), new("Backup required", K.Text, 10), new("Last security review", K.Date, 14)
            });
        columns.AddRange(new SheetColumn[]
        {
            new("Commission date", K.Date, 13), new("End of support", K.Date, 13), new("End of life", K.Date, 13),
            new("Next audit due", K.Date, 13), new("Warranty end", K.Date, 13), new("Created (UTC)", K.Date, 13), new("Created by", K.Text, 14)
        });

        var today = _clock.UtcToday;
        var data = rows.Select(r =>
        {
            var v = new List<object?>
            {
                r.AssetNumber, r.AssetName, r.Category, r.Sub, r.Status, r.Manufacturer, r.ModelName, r.SerialNumber, r.LegacyAssetTag,
                r.Hostname, r.IpAddress, r.MacAddress, r.OperatingSystem, r.OsVersion, r.Processor, r.RamGb, r.StorageGb,
                r.AssignedTo, r.EmployeeNo, r.Department, r.Location, r.Building, r.Floor, r.Room, r.PurchaseDate
            };
            if (access.CanViewFinancials)
                v.AddRange(new object?[] { r.Vendor, r.PurchaseOrderNumber, r.InvoiceNumber, r.PurchaseCost, r.Currency, r.UsefulLifeMonths, r.SalvageValue,
                    AssetRules.BookValue(r.PurchaseCost, r.SalvageValue, r.UsefulLifeMonths, r.PurchaseDate, today) });
            if (access.CanViewSecurityDetails)
                v.AddRange(new object?[] { r.Owner, r.Classification, r.Criticality, r.Risk, YesNo(r.StoresSensitiveData), YesNo(r.IsDiskEncrypted),
                    YesNo(r.EndpointProtectionInstalled), YesNo(r.IsMdmManaged), YesNo(r.BackupRequired), r.LastSecurityReviewDate });
            v.AddRange(new object?[] { r.CommissionDate, r.EndOfSupportDate, r.EndOfLifeDate, r.NextAuditDueDate, r.WarrantyEnd,
                DateOnly.FromDateTime(r.CreatedDate), r.CreatedBy });
            return (IReadOnlyList<object?>)v;
        }).ToList();

        await _uow.Repository<AuditLog>().AddAsync(new AuditLog
        {
            EntityName = nameof(Asset),
            EntityKey = "*",
            Action = Domain.Enums.AuditAction.Export,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                Rows = data.Count, criteria.SearchText, criteria.CategoryId, criteria.StatusId, criteria.DepartmentId,
                criteria.LocationId, IncludesFinancials = access.CanViewFinancials, IncludesSecurity = access.CanViewSecurityDetails
            }),
            PerformedBy = _user.UserName,
            IpAddress = _user.IpAddress,
            CorrelationId = _user.CorrelationId,
            EventDate = _clock.UtcNow
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return _writer.Write(new[] { new SheetData("Assets", columns, data) });
    }

    private static string YesNo(bool b) => b ? "Y" : "N";

    // ------------------------------------------------------------------ import

    public async Task<ImportPreview> PreviewAsync(Stream xlsx, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        await using var buffer = new MemoryStream();
        await xlsx.CopyToAsync(buffer, cancellationToken);

        var (rows, errors, notes) = await ParseAndValidateAsync(buffer, access, cancellationToken);
        if (errors.Count > 0 || rows.Count == 0)
            return new ImportPreview(null, rows.Count, errors, notes);

        var token = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await _storage.SaveAsync(StorageAreas.PendingImports, token + ".xlsx", buffer, cancellationToken);
        return new ImportPreview(token, rows.Count, errors, notes);
    }

    public async Task<Result<IReadOnlyList<CreatedAsset>>> CommitAsync(string token, AssetAccess access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (string.IsNullOrEmpty(token) || token.Length != 32 || !token.All(Uri.IsHexDigit))
            return Result<IReadOnlyList<CreatedAsset>>.Failure(Error.Validation("The import has expired. Upload the file again."));

        var fileName = token + ".xlsx";
        await using var stream = await _storage.OpenReadAsync(StorageAreas.PendingImports, fileName, cancellationToken);
        if (stream is null)
            return Result<IReadOnlyList<CreatedAsset>>.Failure(Error.Validation("The import has expired. Upload the file again."));
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        // Validate again: data may have changed since the preview.
        var (rows, errors, _) = await ParseAndValidateAsync(buffer, access, cancellationToken);
        if (errors.Count > 0)
            return Result<IReadOnlyList<CreatedAsset>>.Failure(errors.Take(20).Select(e => Error.Validation($"Row {e.RowNumber}: {e.Message}")).ToArray());

        try
        {
            var created = await _uow.ExecuteInTransactionAsync(async ct =>
            {
                var sequenceByCategory = (await _uow.Repository<AssetCategory>().QueryAsync(q => q
                    .Select(c => new { c.AssetCategoryId, c.AssetNumberSequence.SequenceCode }), ct))
                    .ToDictionary(x => x.AssetCategoryId, x => x.SequenceCode);
                var assets = new List<Asset>(rows.Count);
                foreach (var (_, model) in rows)
                {
                    var asset = new Asset { AssetNumber = await _numbers.NextAsync(sequenceByCategory[model.AssetCategoryId], ct) };
                    AssetMapper.Apply(model, asset, access, isNew: true);
                    asset.NextAuditDueDate ??= _clock.UtcToday.AddMonths(AssetRules.DefaultAuditIntervalMonths);
                    await _uow.Repository<Asset>().AddAsync(asset, ct);
                    assets.Add(asset);
                }
                await _uow.SaveChangesAsync(ct);
                return (IReadOnlyList<CreatedAsset>)assets.Select(a => new CreatedAsset(a.AssetId, a.AssetNumber)).ToList();
            }, cancellationToken);

            await _storage.DeleteAsync(StorageAreas.PendingImports, fileName, CancellationToken.None);
            return Result<IReadOnlyList<CreatedAsset>>.Success(created);
        }
        catch (Exception ex) when (AssetService.ToError(ex) is { } error)
        {
            return Result<IReadOnlyList<CreatedAsset>>.Failure(error);
        }
    }

    private async Task<(List<(int Row, AssetEditModel Model)> Rows, List<ImportRowError> Errors, List<string> Notes)> ParseAndValidateAsync(
        MemoryStream buffer, AssetAccess access, CancellationToken ct)
    {
        var errors = new List<ImportRowError>();
        var notes = new List<string>();
        var parsed = new List<(int Row, AssetEditModel Model)>();

        SheetTable table;
        try
        {
            buffer.Position = 0;
            table = _reader.ReadFirstSheet(buffer, MaxImportRows + 1);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new ImportRowError(0, $"The file could not be read as an Excel workbook (.xlsx). {ex.Message}"));
            return (parsed, errors, notes);
        }

        var index = table.Headers.Select((h, i) => (Key: NormalizeHeader(h), i))
            .Where(x => x.Key.Length > 0).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { HCategory, HName })
            if (!index.ContainsKey(NormalizeHeader(required)))
                errors.Add(new ImportRowError(1, $"Column '{required.TrimEnd('*')}' is missing. Start from the downloadable template."));
        if (!access.CanViewFinancials && FinancialColumns.Any(c => index.ContainsKey(NormalizeHeader(c.Header))))
            notes.Add("Procurement columns were ignored: your role cannot set financial details.");
        if (!access.CanViewSecurityDetails && SecurityColumns.Any(c => index.ContainsKey(NormalizeHeader(c.Header))))
            notes.Add("Security columns were ignored: your role cannot set information-security details.");
        if (errors.Count > 0) return (parsed, errors, notes);

        var dataRows = table.Rows.Where(r => !r.IsEmpty).ToList();
        if (dataRows.Count > MaxImportRows)
        {
            errors.Add(new ImportRowError(0, $"The file has more than {MaxImportRows} rows. Split it into smaller files."));
            return (parsed, errors, notes);
        }
        if (dataRows.Count == 0)
        {
            errors.Add(new ImportRowError(0, "The 'Assets' sheet has no data rows."));
            return (parsed, errors, notes);
        }

        var codes = await CodeMaps.LoadAsync(_uow, ct);
        foreach (var row in dataRows)
        {
            var rowErrors = new List<string>();
            var cell = new CellReader(row, index, rowErrors);
            var m = new AssetEditModel { AssetName = cell.Text(HName) ?? string.Empty };
            if (m.AssetName.StartsWith(ExampleMarker, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ImportRowError(row.RowNumber, "This is the template's example row. Delete it before importing."));
                continue;
            }

            m.AssetCategoryId = cell.Code(HCategory, codes.Categories, "category") ?? 0;
            var statusCode = cell.Text(HStatus) ?? "IN_STOCK";
            if (!AssetRules.InitialStatuses.Contains(statusCode)) rowErrors.Add($"Status '{statusCode}' is not allowed for new assets.");
            else m.AssetStatusId = codes.Statuses.GetValueOrDefault(statusCode);
            if (cell.Text(HSub) is { } sub)
            {
                if (codes.SubCategories.TryGetValue((m.AssetCategoryId, sub), out var subId)) m.AssetSubCategoryId = subId;
                else rowErrors.Add($"Sub-category '{sub}' does not exist in this category.");
            }
            m.ManufacturerId = cell.Code(HManufacturer, codes.Manufacturers, "manufacturer");
            m.ModelName = cell.Text(HModel); m.SerialNumber = cell.Text(HSerial); m.LegacyAssetTag = cell.Text(HTag); m.Description = cell.Text(HDesc);
            m.Hostname = cell.Text(HHost); m.IpAddress = cell.Text(HIp); m.MacAddress = cell.Text(HMac); m.OperatingSystem = cell.Text(HOs);
            m.OsVersion = cell.Text(HOsVer); m.Processor = cell.Text(HCpu); m.RamGb = cell.Decimal(HRam); m.StorageGb = cell.Decimal(HStorage);
            m.FirmwareVersion = cell.Text(HFw); m.ImeiNumber = cell.Text(HImei); m.PhoneNumber = cell.Text(HPhone);
            m.PurchaseDate = cell.Date(HPurchaseDate);
            m.DepartmentId = cell.Code(HDept, codes.Departments, "department");
            m.LocationId = cell.Code(HLoc, codes.Locations, "location");
            if (cell.Text(HBld) is { } bld)
            {
                if (m.LocationId is { } l && codes.Buildings.TryGetValue((l, bld), out var bid)) m.BuildingId = bid;
                else rowErrors.Add($"Building '{bld}' does not exist at the given location.");
            }
            if (cell.Text(HFlr) is { } flr)
            {
                if (m.BuildingId is { } b && codes.Floors.TryGetValue((b, flr), out var fid)) m.FloorId = fid;
                else rowErrors.Add($"Floor '{flr}' does not exist in the given building.");
            }
            if (cell.Text(HRoom) is { } room)
            {
                if (m.FloorId is { } f && codes.Rooms.TryGetValue((f, room), out var rid)) m.RoomId = rid;
                else rowErrors.Add($"Room '{room}' does not exist on the given floor.");
            }
            if (access.CanViewFinancials)
            {
                m.VendorId = cell.Code(HVendor, codes.Vendors, "vendor");
                m.PurchaseOrderNumber = cell.Text(HPo); m.InvoiceNumber = cell.Text(HInvoice);
                m.PurchaseCost = cell.Decimal(HCost); m.CurrencyId = cell.Code(HCurrency, codes.Currencies, "currency");
                m.UsefulLifeMonths = cell.Integer(HLife); m.SalvageValue = cell.Decimal(HSalvage);
                if (m.PurchaseCost is not null && m.CurrencyId is null && cell.Text(HCurrency) is null) m.CurrencyId = codes.BaseCurrencyId;
            }
            if (access.CanViewSecurityDetails)
            {
                m.AssetOwnerEmployeeId = cell.Code(HOwner, codes.Employees, "employee number");
                m.ClassificationId = cell.Code(HClass, codes.Classifications, "classification");
                m.CriticalityId = cell.Code(HCrit, codes.Criticalities, "criticality");
                m.RiskLevelId = cell.Code(HRisk, codes.RiskLevels, "risk level");
                m.StoresSensitiveData = cell.Bool(HSensitive); m.IsDiskEncrypted = cell.Bool(HEncrypted);
                m.EndpointProtectionInstalled = cell.Bool(HEndpoint); m.IsMdmManaged = cell.Bool(HMdm); m.BackupRequired = cell.Bool(HBackup);
                m.LastSecurityReviewDate = cell.Date(HReview);
            }
            m.CommissionDate = cell.Date(HCommission); m.EndOfSupportDate = cell.Date(HEos); m.EndOfLifeDate = cell.Date(HEol);
            m.NextAuditDueDate = cell.Date(HAudit); m.Notes = cell.Text(HNotes);

            AssetRules.Normalize(m);
            if (rowErrors.Count == 0)
            {
                var result = await _validator.ValidateAsync(m, ct);
                rowErrors.AddRange(result.Errors.Select(e => e.ErrorMessage));
            }
            errors.AddRange(rowErrors.Select(e => new ImportRowError(row.RowNumber, e)));
            parsed.Add((row.RowNumber, m));
        }

        // Uniqueness inside the file and against the register (two queries in total).
        foreach (var dup in parsed.Where(p => p.Model.SerialNumber is not null)
                     .GroupBy(p => (p.Model.ManufacturerId, p.Model.SerialNumber)).Where(g => g.Count() > 1))
            errors.Add(new ImportRowError(dup.Skip(1).First().Row, $"Serial number '{dup.Key.SerialNumber}' appears more than once in the file."));
        foreach (var dup in parsed.Where(p => p.Model.Hostname is not null).GroupBy(p => p.Model.Hostname).Where(g => g.Count() > 1))
            errors.Add(new ImportRowError(dup.Skip(1).First().Row, $"Hostname '{dup.Key}' appears more than once in the file."));

        var serials = parsed.Select(p => p.Model.SerialNumber).OfType<string>().Distinct().ToList();
        var hostnames = parsed.Select(p => p.Model.Hostname).OfType<string>().Distinct().ToList();
        if (serials.Count > 0)
        {
            var existing = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.SerialNumber != null && serials.Contains(a.SerialNumber))
                .Select(a => new { a.ManufacturerId, a.SerialNumber, a.AssetNumber }), ct);
            foreach (var p in parsed)
                if (existing.FirstOrDefault(e => e.ManufacturerId == p.Model.ManufacturerId
                        && string.Equals(e.SerialNumber, p.Model.SerialNumber, StringComparison.OrdinalIgnoreCase)) is { } hit)
                    errors.Add(new ImportRowError(p.Row, $"Serial number '{p.Model.SerialNumber}' is already registered as {hit.AssetNumber}."));
        }
        if (hostnames.Count > 0)
        {
            var existing = await _uow.Repository<Asset>().QueryAsync(q => q.Where(a => a.Hostname != null && hostnames.Contains(a.Hostname))
                .Select(a => new { a.Hostname, a.AssetNumber }), ct);
            foreach (var p in parsed)
                if (existing.FirstOrDefault(e => string.Equals(e.Hostname, p.Model.Hostname, StringComparison.OrdinalIgnoreCase)) is { } hit)
                    errors.Add(new ImportRowError(p.Row, $"Hostname '{p.Model.Hostname}' is already used by {hit.AssetNumber}."));
        }

        return (parsed, errors.OrderBy(e => e.RowNumber).ToList(), notes);
    }

    private static string NormalizeHeader(string? h) =>
        (h ?? string.Empty).Replace("*", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    // ------------------------------------------------------------------ helpers

    private sealed class CellReader
    {
        private readonly SheetRow _row;
        private readonly IReadOnlyDictionary<string, int> _index;
        private readonly List<string> _errors;

        public CellReader(SheetRow row, IReadOnlyDictionary<string, int> index, List<string> errors)
        {
            _row = row;
            _index = index;
            _errors = errors;
        }

        private static string Label(string header) => header.Replace("*", string.Empty, StringComparison.Ordinal).Trim();

        public string? Text(string header)
        {
            if (!_index.TryGetValue(NormalizeHeader(header), out var i)) return null;
            var v = _row[i];
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        public int? Code(string header, Dictionary<string, int> map, string what)
        {
            var v = Text(header);
            if (v is null) return null;
            if (map.TryGetValue(v, out var id)) return id;
            _errors.Add($"{Label(header)}: '{v}' is not a known {what} code.");
            return null;
        }

        public decimal? Decimal(string header)
        {
            var v = Text(header);
            if (v is null) return null;
            if (decimal.TryParse(v, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)) return d;
            _errors.Add($"{Label(header)}: '{v}' is not a number.");
            return null;
        }

        public int? Integer(string header)
        {
            var d = Decimal(header);
            if (d is null) return null;
            if (d == Math.Truncate(d.Value) && d is >= int.MinValue and <= int.MaxValue) return (int)d.Value;
            _errors.Add($"{Label(header)}: must be a whole number.");
            return null;
        }

        public DateOnly? Date(string header)
        {
            var v = Text(header);
            if (v is null) return null;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2958466)
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            if (DateOnly.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)) return d;
            if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)) return DateOnly.FromDateTime(dt);
            _errors.Add($"{Label(header)}: '{v}' is not a date (use yyyy-MM-dd).");
            return null;
        }

        public bool Bool(string header)
        {
            var v = Text(header);
            if (v is null) return false;
            switch (v.ToUpperInvariant())
            {
                case "Y": case "YES": case "TRUE": case "1": return true;
                case "N": case "NO": case "FALSE": case "0": return false;
                default:
                    _errors.Add($"{Label(header)}: use Y or N.");
                    return false;
            }
        }
    }

    private sealed class CodeMaps
    {
        public Dictionary<string, int> Categories { get; private init; } = new();
        public Dictionary<string, int> Statuses { get; private init; } = new();
        public Dictionary<(int, string), int> SubCategories { get; private init; } = new();
        public Dictionary<string, int> Manufacturers { get; private init; } = new();
        public Dictionary<string, int> Vendors { get; private init; } = new();
        public Dictionary<string, int> Currencies { get; private init; } = new();
        public Dictionary<string, int> Departments { get; private init; } = new();
        public Dictionary<string, int> Locations { get; private init; } = new();
        public Dictionary<(int, string), int> Buildings { get; private init; } = new();
        public Dictionary<(int, string), int> Floors { get; private init; } = new();
        public Dictionary<(int, string), int> Rooms { get; private init; } = new();
        public Dictionary<string, int> Employees { get; private init; } = new();
        public Dictionary<string, int> Classifications { get; private init; } = new();
        public Dictionary<string, int> Criticalities { get; private init; } = new();
        public Dictionary<string, int> RiskLevels { get; private init; } = new();
        public int? BaseCurrencyId { get; private init; }

        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
        private sealed class PairComparer : IEqualityComparer<(int, string)>
        {
            public bool Equals((int, string) x, (int, string) y) => x.Item1 == y.Item1 && Ci.Equals(x.Item2, y.Item2);
            public int GetHashCode((int, string) obj) => HashCode.Combine(obj.Item1, Ci.GetHashCode(obj.Item2));
        }
        private static readonly PairComparer Pc = new();

        public static async Task<CodeMaps> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            static Dictionary<string, int> Map<T>(IEnumerable<T> rows, Func<T, string> code, Func<T, int> id) =>
                rows.GroupBy(code, Ci).ToDictionary(g => g.Key, g => id(g.First()), Ci);
            static Dictionary<(int, string), int> Map2<T>(IEnumerable<T> rows, Func<T, int> parent, Func<T, string> code, Func<T, int> id) =>
                rows.GroupBy(r => (parent(r), code(r)), Pc).ToDictionary(g => g.Key, g => id(g.First()), Pc);

            var cats = await uow.Repository<AssetCategory>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.AssetCategoryId, x.CategoryCode }), ct);
            var subs = await uow.Repository<AssetSubCategory>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.AssetSubCategoryId, x.AssetCategoryId, x.SubCategoryCode }), ct);
            var sts = await uow.Repository<AssetStatus>().QueryAsync(q => q.Where(x => !x.IsDeleted).Select(x => new { x.AssetStatusId, x.StatusCode }), ct);
            var mans = await uow.Repository<Manufacturer>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.ManufacturerId, x.ManufacturerCode }), ct);
            var vens = await uow.Repository<Vendor>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.VendorId, x.VendorCode }), ct);
            var curs = await uow.Repository<Currency>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.CurrencyId, x.CurrencyCode, x.IsBaseCurrency }), ct);
            var deps = await uow.Repository<Department>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.DepartmentId, x.DepartmentCode }), ct);
            var locs = await uow.Repository<Location>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.LocationId, x.LocationCode }), ct);
            var blds = await uow.Repository<Building>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.BuildingId, x.LocationId, x.BuildingCode }), ct);
            var flrs = await uow.Repository<Floor>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.FloorId, x.BuildingId, x.FloorCode }), ct);
            var rooms = await uow.Repository<Room>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.RoomId, x.FloorId, x.RoomCode }), ct);
            var emps = await uow.Repository<Employee>().QueryAsync(q => q.Where(x => !x.IsDeleted).Select(x => new { x.EmployeeId, x.EmployeeNumber }), ct);
            var cls = await uow.Repository<Classification>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.ClassificationId, x.ClassificationCode }), ct);
            var cri = await uow.Repository<Criticality>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.CriticalityId, x.CriticalityCode }), ct);
            var rsk = await uow.Repository<RiskLevel>().QueryAsync(q => q.Where(x => !x.IsDeleted && x.IsActive).Select(x => new { x.RiskLevelId, x.RiskLevelCode }), ct);

            return new CodeMaps
            {
                Categories = Map(cats, x => x.CategoryCode, x => x.AssetCategoryId),
                SubCategories = Map2(subs, x => x.AssetCategoryId, x => x.SubCategoryCode, x => x.AssetSubCategoryId),
                Statuses = Map(sts, x => x.StatusCode, x => x.AssetStatusId),
                Manufacturers = Map(mans, x => x.ManufacturerCode, x => x.ManufacturerId),
                Vendors = Map(vens, x => x.VendorCode, x => x.VendorId),
                Currencies = Map(curs, x => x.CurrencyCode, x => x.CurrencyId),
                BaseCurrencyId = curs.FirstOrDefault(x => x.IsBaseCurrency)?.CurrencyId,
                Departments = Map(deps, x => x.DepartmentCode, x => x.DepartmentId),
                Locations = Map(locs, x => x.LocationCode, x => x.LocationId),
                Buildings = Map2(blds, x => x.LocationId, x => x.BuildingCode, x => x.BuildingId),
                Floors = Map2(flrs, x => x.BuildingId, x => x.FloorCode, x => x.FloorId),
                Rooms = Map2(rooms, x => x.FloorId, x => x.RoomCode, x => x.RoomId),
                Employees = Map(emps, x => x.EmployeeNumber, x => x.EmployeeId),
                Classifications = Map(cls, x => x.ClassificationCode, x => x.ClassificationId),
                Criticalities = Map(cri, x => x.CriticalityCode, x => x.CriticalityId),
                RiskLevels = Map(rsk, x => x.RiskLevelCode, x => x.RiskLevelId)
            };
        }
    }
}
