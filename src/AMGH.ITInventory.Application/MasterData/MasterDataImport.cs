using System.Globalization;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Abstractions.Spreadsheets;
using AMGH.ITInventory.Application.Abstractions.Storage;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Results;

namespace AMGH.ITInventory.Application.MasterData;

public sealed record MasterImportIssue(string Sheet, int Row, string Message);

public sealed record MasterImportSheetSummary(string Sheet, int Created, int Updated, int Unchanged, int Errors)
{
    public int Rows => Created + Updated + Unchanged + Errors;
}

/// <summary>Result of a preview (nothing saved) or an import (all saved, or nothing when there are issues).</summary>
public sealed record MasterImportResult(IReadOnlyList<MasterImportSheetSummary> Sheets, IReadOnlyList<MasterImportIssue> Issues,
    IReadOnlyList<string> Notes, string? Token, bool Committed)
{
    public bool IsClean => Issues.Count == 0;
    public int Changes => Sheets.Sum(s => s.Created + s.Updated);
}

/// <summary>
/// Release 2.14: import and export of all master data in one workbook (one sheet per list). Rows are matched by their code
/// (employee number for employees; buildings within their location, floors within their building, rooms within their floor,
/// sub-categories within their category). Existing records are updated, new ones created, nothing is deleted.
/// A column that is missing leaves that field unchanged; an empty cell clears it (an empty Yes/No cell keeps its value). Every row goes through the same rules as
/// the master data screens. The preview runs the whole import and rolls it back; the import is all or nothing.
/// </summary>
public interface IMasterDataImportService
{
    Task<byte[]> ExportAsync(CancellationToken cancellationToken = default);
    Task<MasterImportResult> PreviewAsync(Stream xlsx, CancellationToken cancellationToken = default);
    Task<MasterImportResult> CommitAsync(string token, CancellationToken cancellationToken = default);
}

internal sealed class MasterDataImportService : IMasterDataImportService
{
    public const int MaxRowsPerSheet = 10_000;

    private readonly IUnitOfWork _uow;
    private readonly ISpreadsheetReader _reader;
    private readonly ISpreadsheetWriter _writer;
    private readonly IFileStorage _storage;
    private readonly Dictionary<string, IMasterDataDefinition> _defs;

    public MasterDataImportService(IUnitOfWork uow, ISpreadsheetReader reader, ISpreadsheetWriter writer, IFileStorage storage,
        IEnumerable<IMasterDataDefinition> definitions)
    {
        _uow = uow;
        _reader = reader;
        _writer = writer;
        _storage = storage;
        _defs = definitions.ToDictionary(d => d.Info.Key, StringComparer.Ordinal);
    }

    // ================================================================== sheet layouts

    /// <summary>A column of a sheet: a plain field of the definition, or a reference written as a code.</summary>
    private sealed record Col(string Header, string? Field = null, RefKind Ref = RefKind.None, bool Deferred = false, bool Key = false);

    private enum RefKind { None, Location, Building, Floor, Category, Department, Employee, NumberSeries }

    private sealed record Sheet(string Name, string DefKey, IReadOnlyList<Col> Cols);

    private static readonly Sheet[] Sheets =
    {
        new("Manufacturers", "manufacturers", new Col[]
        {
            new("Code", "Code", Key: true), new("Name", "Name"), new("Website", "Website"), new("Support e-mail", "SupportEmail"),
            new("Support phone", "SupportPhone"), new("Active", "IsActive")
        }),
        new("Vendors", "vendors", new Col[]
        {
            new("Code", "Code", Key: true), new("Name", "Name"), new("Contact person", "ContactPerson"), new("E-mail", "Email"), new("Phone", "Phone"),
            new("Address", "Address"), new("Commercial registration no.", "CommercialRegistrationNo"), new("VAT number", "VatNumber"),
            new("Approved supplier", "IsApproved"), new("Approval date", "ApprovalDate"), new("Last security assessment", "SecurityAssessmentDate"),
            new("Notes", "Notes"), new("Active", "IsActive")
        }),
        new("Locations", "locations", new Col[]
        {
            new("Code", "Code", Key: true), new("Name", "Name"), new("Address", "AddressLine"), new("City", "City"), new("Country", "Country"), new("Active", "IsActive")
        }),
        new("Buildings", "buildings", new Col[]
        {
            new("Location code", "LocationId", RefKind.Location, Key: true), new("Code", "Code", Key: true), new("Name", "Name"), new("Active", "IsActive")
        }),
        new("Floors", "floors", new Col[]
        {
            new("Location code", null, RefKind.Location, Key: true), new("Building code", "BuildingId", RefKind.Building, Key: true),
            new("Code", "Code", Key: true), new("Name", "Name"), new("Floor number", "FloorNumber"), new("Active", "IsActive")
        }),
        new("Rooms", "rooms", new Col[]
        {
            new("Location code", null, RefKind.Location, Key: true), new("Building code", null, RefKind.Building, Key: true),
            new("Floor code", "FloorId", RefKind.Floor, Key: true), new("Code", "Code", Key: true), new("Name", "Name"),
            new("Room type", "RoomType"), new("Secure area", "IsSecureArea"), new("Active", "IsActive")
        }),
        new("Asset categories", "categories", new Col[]
        {
            new("Code", "Code", Key: true), new("Name", "Name"), new("Number series", "AssetNumberSequenceId", RefKind.NumberSeries),
            new("Useful life (months)", "DefaultUsefulLifeMonths"), new("Requires hostname", "RequiresHostname"), new("Description", "Description"),
            new("Sort order", "SortOrder"), new("Active", "IsActive")
        }),
        new("Asset sub-categories", "subcategories", new Col[]
        {
            new("Category code", "AssetCategoryId", RefKind.Category, Key: true), new("Code", "Code", Key: true), new("Name", "Name"),
            new("Description", "Description"), new("Sort order", "SortOrder"), new("Active", "IsActive")
        }),
        new("Departments", "departments", new Col[]
        {
            new("Code", "Code", Key: true), new("Name", "Name"), new("Parent department code", "ParentDepartmentId", RefKind.Department, Deferred: true),
            new("Head (employee number)", "HeadEmployeeId", RefKind.Employee, Deferred: true), new("Cost center", "CostCenter"), new("Active", "IsActive")
        }),
        new("Employees", "employees", new Col[]
        {
            new("Employee number", "EmployeeNumber", Key: true), new("First name", "FirstName"), new("Last name", "LastName"), new("E-mail", "Email"),
            new("Phone", "Phone"), new("Job title", "JobTitle"), new("Department code", "DepartmentId", RefKind.Department),
            new("Work location code", "LocationId", RefKind.Location), new("Line manager (employee number)", "ManagerEmployeeId", RefKind.Employee, Deferred: true),
            new("Hire date", "HireDate"), new("Employment status", "EmploymentStatus"), new("Termination date", "TerminationDate")
        })
    };

    private FieldDefinition FieldOf(Sheet s, string field) => _defs[s.DefKey].Fields.First(f => f.Name == field);

    // ================================================================== code directory (codes -> ids), kept current while importing

    private sealed class Directory
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
        public Dictionary<string, int> Manufacturers = new(Ci), Vendors = new(Ci), Locations = new(Ci), Categories = new(Ci),
            Departments = new(Ci), Employees = new(Ci), Series = new(Ci);
        public Dictionary<(int, string), int> Buildings = new(new PairCi()), Floors = new(new PairCi()), Rooms = new(new PairCi()), SubCategories = new(new PairCi());

        private Dictionary<RefKind, HashSet<int>>? _known;
        /// <summary>Whether an id can be written as a code (false e.g. for a deleted record still referenced).</summary>
        public bool Known(RefKind kind, int id)
        {
            _known ??= new Dictionary<RefKind, HashSet<int>>
            {
                [RefKind.Location] = Locations.Values.ToHashSet(), [RefKind.Building] = Buildings.Values.ToHashSet(),
                [RefKind.Floor] = Floors.Values.ToHashSet(), [RefKind.Category] = Categories.Values.ToHashSet(),
                [RefKind.Department] = Departments.Values.ToHashSet(), [RefKind.Employee] = Employees.Values.ToHashSet(),
                [RefKind.NumberSeries] = Series.Values.ToHashSet()
            };
            return _known.TryGetValue(kind, out var set) && set.Contains(id);
        }
        /// <summary>Call after adding codes, so <see cref="Known"/> sees them.</summary>
        public void Changed() => _known = null;

        private sealed class PairCi : IEqualityComparer<(int, string)>
        {
            public bool Equals((int, string) a, (int, string) b) => a.Item1 == b.Item1 && Ci.Equals(a.Item2, b.Item2);
            public int GetHashCode((int, string) k) => HashCode.Combine(k.Item1, Ci.GetHashCode(k.Item2));
        }
    }

    private async Task<Directory> LoadDirectoryAsync(CancellationToken ct)
    {
        var d = new Directory();
        foreach (var x in await _uow.Repository<Manufacturer>().QueryAsync(q => q.NotDeleted().Select(e => new { e.ManufacturerCode, e.ManufacturerId }), ct)) d.Manufacturers[x.ManufacturerCode] = x.ManufacturerId;
        foreach (var x in await _uow.Repository<Vendor>().QueryAsync(q => q.NotDeleted().Select(e => new { e.VendorCode, e.VendorId }), ct)) d.Vendors[x.VendorCode] = x.VendorId;
        foreach (var x in await _uow.Repository<Location>().QueryAsync(q => q.NotDeleted().Select(e => new { e.LocationCode, e.LocationId }), ct)) d.Locations[x.LocationCode] = x.LocationId;
        foreach (var x in await _uow.Repository<Building>().QueryAsync(q => q.NotDeleted().Select(e => new { e.LocationId, e.BuildingCode, e.BuildingId }), ct)) d.Buildings[(x.LocationId, x.BuildingCode)] = x.BuildingId;
        foreach (var x in await _uow.Repository<Floor>().QueryAsync(q => q.NotDeleted().Select(e => new { e.BuildingId, e.FloorCode, e.FloorId }), ct)) d.Floors[(x.BuildingId, x.FloorCode)] = x.FloorId;
        foreach (var x in await _uow.Repository<Room>().QueryAsync(q => q.NotDeleted().Select(e => new { e.FloorId, e.RoomCode, e.RoomId }), ct)) d.Rooms[(x.FloorId, x.RoomCode)] = x.RoomId;
        foreach (var x in await _uow.Repository<AssetCategory>().QueryAsync(q => q.NotDeleted().Select(e => new { e.CategoryCode, e.AssetCategoryId }), ct)) d.Categories[x.CategoryCode] = x.AssetCategoryId;
        foreach (var x in await _uow.Repository<AssetSubCategory>().QueryAsync(q => q.NotDeleted().Select(e => new { e.AssetCategoryId, e.SubCategoryCode, e.AssetSubCategoryId }), ct)) d.SubCategories[(x.AssetCategoryId, x.SubCategoryCode)] = x.AssetSubCategoryId;
        foreach (var x in await _uow.Repository<Department>().QueryAsync(q => q.NotDeleted().Select(e => new { e.DepartmentCode, e.DepartmentId }), ct)) d.Departments[x.DepartmentCode] = x.DepartmentId;
        foreach (var x in await _uow.Repository<Employee>().QueryAsync(q => q.NotDeleted().Select(e => new { e.EmployeeNumber, e.EmployeeId }), ct)) d.Employees[x.EmployeeNumber] = x.EmployeeId;
        foreach (var x in await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q.Select(e => new { e.SequenceCode, e.AssetNumberSequenceId }), ct)) d.Series[x.SequenceCode] = x.AssetNumberSequenceId;
        return d;
    }

    // ================================================================== export (also the template: an empty company gives headers only)

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var d = await LoadDirectoryAsync(ct);
        var sheets = new List<SheetData> { Instructions() };
        // reverse maps id -> code for references
        var loc = d.Locations.ToDictionary(kv => kv.Value, kv => kv.Key);
        var bld = d.Buildings.ToDictionary(kv => kv.Value, kv => (Loc: kv.Key.Item1, Code: kv.Key.Item2));
        var flr = d.Floors.ToDictionary(kv => kv.Value, kv => (Bld: kv.Key.Item1, Code: kv.Key.Item2));
        var cat = d.Categories.ToDictionary(kv => kv.Value, kv => kv.Key);
        var dep = d.Departments.ToDictionary(kv => kv.Value, kv => kv.Key);
        var emp = d.Employees.ToDictionary(kv => kv.Value, kv => kv.Key);
        var ser = d.Series.ToDictionary(kv => kv.Value, kv => kv.Key);

        foreach (var s in Sheets)
        {
            var def = _defs[s.DefKey];
            var rows = new List<IReadOnlyList<object?>>();
            for (var page = 1; ; page++)
            {
                var p = await def.ListAsync(_uow, null, includeInactive: true, page, 100, ct);
                foreach (var r in p.Rows)
                {
                    var rec = await def.GetAsync(_uow, r.Id, ct);
                    if (rec is null) continue;
                    rows.Add(s.Cols.Select(c => ExportCell(s, c, rec.Values, loc, bld, flr, cat, dep, emp, ser)).ToList());
                }
                if (page >= (int)Math.Ceiling(p.TotalCount / (double)p.PageSize)) break;
            }
            var cols = s.Cols.Select(c => new SheetColumn(c.Header + (IsRequired(s, c) ? " *" : ""), KindOf(s, c), Math.Max(14, c.Header.Length + 4))).ToList();
            sheets.Add(new SheetData(s.Name, cols, rows));
        }
        return _writer.Write(sheets);
    }

    private bool IsRequired(Sheet s, Col c) => c.Key || (c.Field is not null && FieldOf(s, c.Field).Required);

    private SheetColumnKind KindOf(Sheet s, Col c) => c.Field is null || c.Ref != RefKind.None ? SheetColumnKind.Text : FieldOf(s, c.Field).Kind switch
    {
        FieldKind.Date => SheetColumnKind.Date,
        FieldKind.WholeNumber => SheetColumnKind.WholeNumber,
        _ => SheetColumnKind.Text
    };

    private object? ExportCell(Sheet s, Col c, IReadOnlyDictionary<string, string?> v,
        Dictionary<int, string> loc, Dictionary<int, (int Loc, string Code)> bld, Dictionary<int, (int Bld, string Code)> flr,
        Dictionary<int, string> cat, Dictionary<int, string> dep, Dictionary<int, string> emp, Dictionary<int, string> ser)
    {
        int? Id(string field) => int.TryParse(v.GetValueOrDefault(field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        // parent codes of floors and rooms are derived from the referenced building / floor
        if (c.Field is null)
            return (s.DefKey, c.Ref) switch
            {
                ("floors", RefKind.Location) => Id("BuildingId") is { } b && bld.TryGetValue(b, out var bb) ? loc.GetValueOrDefault(bb.Loc) : null,
                ("rooms", RefKind.Building) => Id("FloorId") is { } f && flr.TryGetValue(f, out var ff) ? bld.GetValueOrDefault(ff.Bld).Code : null,
                ("rooms", RefKind.Location) => Id("FloorId") is { } f2 && flr.TryGetValue(f2, out var ff2) && bld.TryGetValue(ff2.Bld, out var bb2) ? loc.GetValueOrDefault(bb2.Loc) : null,
                _ => null
            };
        if (c.Ref != RefKind.None)
        {
            var id = Id(c.Field);
            if (id is null) return null;
            return c.Ref switch
            {
                RefKind.Location => loc.GetValueOrDefault(id.Value),
                RefKind.Building => bld.TryGetValue(id.Value, out var b) ? b.Code : null,
                RefKind.Floor => flr.TryGetValue(id.Value, out var f) ? f.Code : null,
                RefKind.Category => cat.GetValueOrDefault(id.Value),
                RefKind.Department => dep.GetValueOrDefault(id.Value),
                RefKind.Employee => emp.GetValueOrDefault(id.Value),
                RefKind.NumberSeries => ser.GetValueOrDefault(id.Value),
                _ => null
            };
        }
        var f2d = FieldOf(s, c.Field);
        var text = v.GetValueOrDefault(c.Field);
        if (string.IsNullOrEmpty(text)) return f2d.Kind == FieldKind.Boolean ? "No" : null;
        return f2d.Kind switch
        {
            FieldKind.Boolean => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No",
            FieldKind.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : text,
            FieldKind.WholeNumber => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : text,
            _ => text
        };
    }

    private static SheetData Instructions() => new("Instructions", new[] { new SheetColumn("How to use this workbook", Width: 120) }, new[]
    {
        Line("One sheet per list. Keep the sheet names and the column headings; columns marked * are required."),
        Line("Rows are matched by code (employees by employee number). Existing records are updated, new ones are created. Nothing is deleted."),
        Line("Buildings are matched within their location, floors within their building, rooms within their floor, sub-categories within their category."),
        Line("A column you remove leaves that field unchanged. An empty cell clears the field."),
        Line("References are written as codes: e.g. an employee's department is its department code; a line manager is an employee number."),
        Line("Yes / No columns: Yes, No (also True, False, 1, 0); an empty Yes / No cell keeps the current value. Dates: Excel dates or yyyy-MM-dd."),
        Line("Room type: ServerRoom, Office, Store, MeetingRoom, ... Employment status: Active, OnLeave, Suspended, Terminated (spaces allowed)."),
        Line("Asset categories: 'Number series' is a series code; a code that does not exist yet creates a new number series."),
        Line($"Up to {MaxRowsPerSheet:N0} rows per sheet. Upload the file: you first see a preview; nothing is saved until you confirm."),
        Line("Sheets you leave out (or delete) are not touched.")
    });

    private static object?[] Line(string text) => new object?[] { text };

    // ================================================================== preview / commit

    private sealed class RollbackSignal : Exception { }

    public async Task<MasterImportResult> PreviewAsync(Stream xlsx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        await using var buffer = new MemoryStream();
        await xlsx.CopyToAsync(buffer, cancellationToken);
        var result = await RunAsync(buffer, commit: false, cancellationToken);
        if (!result.IsClean || result.Changes == 0) return result;
        var token = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await _storage.SaveAsync(StorageAreas.PendingImports, "md-" + token + ".xlsx", buffer, cancellationToken);
        return result with { Token = token };
    }

    public async Task<MasterImportResult> CommitAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 32 || !token.All(Uri.IsHexDigit))
            return Expired();
        var name = "md-" + token + ".xlsx";
        await using var stream = await _storage.OpenReadAsync(StorageAreas.PendingImports, name, cancellationToken);
        if (stream is null) return Expired();
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        // validated again: data may have changed since the preview
        var result = await RunAsync(buffer, commit: true, cancellationToken);
        if (result.Committed) await _storage.DeleteAsync(StorageAreas.PendingImports, name, CancellationToken.None);
        return result;
    }

    private static MasterImportResult Expired() => new(Array.Empty<MasterImportSheetSummary>(),
        new[] { new MasterImportIssue("", 0, "The import has expired. Upload the file again.") }, Array.Empty<string>(), null, false);

    private async Task<MasterImportResult> RunAsync(MemoryStream buffer, bool commit, CancellationToken ct)
    {
        buffer.Position = 0;
        IReadOnlyDictionary<string, SheetTable> book;
        try
        {
            book = _reader.ReadAllSheets(buffer, MaxRowsPerSheet);
        }
        catch (InvalidDataException ex)
        {
            return new MasterImportResult(Array.Empty<MasterImportSheetSummary>(), new[] { new MasterImportIssue("", 0, ex.Message) }, Array.Empty<string>(), null, false);
        }

        var run = new Run();
        foreach (var name in book.Keys.Where(n => !Sheets.Any(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) && !n.Equals("Instructions", StringComparison.OrdinalIgnoreCase)))
            run.Notes.Add($"Sheet '{name}' is not a master data list and was ignored.");
        foreach (var (name, table) in book)
            if (table.Truncated) run.Issue(name, 0, $"More than {MaxRowsPerSheet:N0} rows: split the sheet into several files.");
        if (run.Issues.Count > 0) return run.Result(null, false);

        try
        {
            await _uow.ExecuteInTransactionAsync(async c =>
            {
                var dir = await LoadDirectoryAsync(c);
                var pending = new List<(Sheet Sheet, SheetTable Table, Dictionary<int, int> RowIds)>();
                foreach (var s in Sheets)
                {
                    if (!book.TryGetValue(s.Name, out var table)) continue;
                    var rowIds = await ImportSheetAsync(s, table, dir, run, c);
                    if (s.Cols.Any(x => x.Deferred)) pending.Add((s, table, rowIds));
                }
                // second pass: references that may point to rows further down or on a later sheet
                foreach (var (s, table, rowIds) in pending)
                    await ImportDeferredAsync(s, table, rowIds, dir, run, c);
                if (!commit || run.Issues.Count > 0) throw new RollbackSignal();
                return 0;
            }, ct);
        }
        catch (RollbackSignal)
        {
            return run.Result(null, false);
        }
        return run.Result(null, true);
    }

    private sealed class Run
    {
        public readonly List<MasterImportIssue> Issues = new();
        public readonly List<string> Notes = new();
        public readonly Dictionary<string, (int Created, int Updated, int Unchanged, HashSet<int> ErrorRows)> Counts = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<(string, int), string> Status = new();   // per row: Created / Updated / Unchanged

        public void Issue(string sheet, int row, string message)
        {
            if (Issues.Count < 2000) Issues.Add(new MasterImportIssue(sheet, row, message));
            if (row > 0) C(sheet).ErrorRows.Add(row);
        }

        private (int Created, int Updated, int Unchanged, HashSet<int> ErrorRows) C(string sheet) =>
            Counts.TryGetValue(sheet, out var c) ? c : Counts[sheet] = (0, 0, 0, new HashSet<int>());

        public void Mark(string sheet, int row, string status)
        {
            var key = (sheet, row);
            var before = Status.GetValueOrDefault(key);
            if (before == status || before == "Created") return;
            var c = C(sheet);
            if (before == "Unchanged") c.Unchanged--;
            if (status == "Created") c.Created++; else if (status == "Updated") c.Updated++; else c.Unchanged++;
            Counts[sheet] = c;
            Status[key] = status;
        }

        public MasterImportResult Result(string? token, bool committed) => new(
            Sheets.Where(s => Counts.ContainsKey(s.Name)).Select(s =>
            {
                var c = Counts[s.Name];
                return new MasterImportSheetSummary(s.Name, c.Created, c.Updated, c.Unchanged, c.ErrorRows.Count);
            }).ToList(),
            Issues.OrderBy(i => Array.FindIndex(Sheets, s => s.Name.Equals(i.Sheet, StringComparison.OrdinalIgnoreCase))).ThenBy(i => i.Row).ToList(),
            Notes, token, committed);
    }

    // ------------------------------------------------------------------ one sheet

    private static string Norm(string h) => h.Replace("*", "", StringComparison.Ordinal).Trim();

    private async Task<Dictionary<int, int>> ImportSheetAsync(Sheet s, SheetTable table, Directory dir, Run run, CancellationToken ct)
    {
        var def = _defs[s.DefKey];
        var rowIds = new Dictionary<int, int>();
        var index = new Dictionary<Col, int>();
        for (var i = 0; i < table.Headers.Count; i++)
        {
            var col = s.Cols.FirstOrDefault(c => c.Header.Equals(Norm(table.Headers[i] ?? ""), StringComparison.OrdinalIgnoreCase));
            if (col is not null && !index.ContainsKey(col)) index[col] = i;
            else if (!string.IsNullOrWhiteSpace(table.Headers[i]) && col is null)
                run.Notes.Add($"{s.Name}: column '{table.Headers[i]}' is not known and was ignored.");
        }
        foreach (var k in s.Cols.Where(c => c.Key && !index.ContainsKey(c)))
        {
            run.Issue(s.Name, 1, $"The column '{k.Header}' is missing.");
            return rowIds;
        }
        run.Counts.TryAdd(s.Name, (0, 0, 0, new HashSet<int>()));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows.Where(r => !r.IsEmpty))
        {
            ct.ThrowIfCancellationRequested();
            string? Cell(Col c) => index.TryGetValue(c, out var i) ? row[i]?.Trim() : null;
            var errors = new List<string>();

            // --- the row's identity: parents (resolved by code) and its own code
            var (parentId, code) = ResolveKey(s, Cell, dir, errors);
            if (code is null) { foreach (var e in errors) run.Issue(s.Name, row.RowNumber, e); continue; }
            if (!seen.Add($"{parentId}|{code}")) { run.Issue(s.Name, row.RowNumber, $"'{code}' appears more than once on this sheet."); continue; }
            if (errors.Count > 0) { foreach (var e in errors) run.Issue(s.Name, row.RowNumber, e); continue; }
            var existingId = Lookup(s, dir, parentId, code);

            // --- the form: current values (missing columns stay as they are), then the cells of this row
            MasterDataRecord? current = existingId is { } eid ? await def.GetAsync(_uow, eid, ct) : null;
            var form = current is null ? new Dictionary<string, string?>(StringComparer.Ordinal) : current.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (current is null && s.DefKey is "manufacturers" or "vendors" or "locations" or "buildings" or "floors" or "rooms" or "categories" or "subcategories" or "departments")
                form["IsActive"] = "true";   // new records are active unless the sheet says otherwise
            if (current is null && s.DefKey == "employees") form["EmploymentStatus"] = nameof(EmploymentStatus.Active);
            foreach (var c in s.Cols.Where(c => c.Field is not null && !c.Deferred && index.ContainsKey(c)))
                if (Convert(s, c, Cell(c), dir, form, errors) is { } value) form[c.Field!] = value.Length == 0 ? null : value;
            if (parentId is not null && s.Cols.FirstOrDefault(c => c.Key && c.Field is not null && c.Ref != RefKind.None) is { } pc) form[pc.Field!] = parentId.Value.ToString(CultureInfo.InvariantCulture);
            if (s.DefKey == "employees") form["EmployeeNumber"] = code; else form["Code"] = code;
            if (errors.Count > 0) { foreach (var e in errors) run.Issue(s.Name, row.RowNumber, e); continue; }

            if (current is not null && Same(current.Values, form)) { rowIds[row.RowNumber] = existingId!.Value; run.Mark(s.Name, row.RowNumber, "Unchanged"); continue; }
            var saved = await def.SaveAsync(_uow, existingId, form, current?.RowVersion, ct);
            if (saved.IsFailure) { foreach (var e in saved.Errors) run.Issue(s.Name, row.RowNumber, Describe(s, e)); continue; }
            rowIds[row.RowNumber] = saved.Value;
            Remember(s, dir, parentId, code, saved.Value);
            if (s.DefKey == "categories" && form.GetValueOrDefault("NewSeriesCode") is { Length: > 0 } newSeries)
                foreach (var x in await _uow.Repository<AssetNumberSequence>().QueryAsync(q => q.Where(e => e.SequenceCode == newSeries).Select(e => new { e.SequenceCode, e.AssetNumberSequenceId }), ct))
                    dir.Series[x.SequenceCode] = x.AssetNumberSequenceId;
            run.Mark(s.Name, row.RowNumber, current is null ? "Created" : "Updated");
        }
        return rowIds;
    }

    private async Task ImportDeferredAsync(Sheet s, SheetTable table, Dictionary<int, int> rowIds, Directory dir, Run run, CancellationToken ct)
    {
        var def = _defs[s.DefKey];
        var index = new Dictionary<Col, int>();
        for (var i = 0; i < table.Headers.Count; i++)
            if (s.Cols.FirstOrDefault(c => c.Deferred && c.Header.Equals(Norm(table.Headers[i] ?? ""), StringComparison.OrdinalIgnoreCase)) is { } col) index[col] = i;
        if (index.Count == 0) return;
        foreach (var row in table.Rows.Where(r => !r.IsEmpty && rowIds.ContainsKey(r.RowNumber)))
        {
            var current = await def.GetAsync(_uow, rowIds[row.RowNumber], ct);
            if (current is null) continue;
            var form = current.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (var (c, i) in index)
                if (Convert(s, c, row[i]?.Trim(), dir, form, errors) is { } value) form[c.Field!] = value.Length == 0 ? null : value;
            if (errors.Count > 0) { foreach (var e in errors) run.Issue(s.Name, row.RowNumber, e); continue; }
            if (Same(current.Values, form)) continue;
            var saved = await def.SaveAsync(_uow, rowIds[row.RowNumber], form, current.RowVersion, ct);
            if (saved.IsFailure) { foreach (var e in saved.Errors) run.Issue(s.Name, row.RowNumber, Describe(s, e)); continue; }
            run.Mark(s.Name, row.RowNumber, "Updated");
        }
    }

    private static bool Same(IReadOnlyDictionary<string, string?> before, Dictionary<string, string?> after) =>
        after.All(kv => string.Equals(Blank(before.GetValueOrDefault(kv.Key)), Blank(kv.Value), StringComparison.Ordinal));

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Error messages name the sheet's column, not the internal field.</summary>
    private static string Describe(Sheet s, Error e)
    {
        var col = s.Cols.FirstOrDefault(c => c.Field == e.Code);
        return col is null || e.Message.Contains(col.Header, StringComparison.OrdinalIgnoreCase) ? e.Message : $"{col.Header}: {e.Message}";
    }

    // ------------------------------------------------------------------ keys and references

    private static (int? ParentId, string? Code) ResolveKey(Sheet s, Func<Col, string?> cell, Directory d, List<string> errors)
    {
        string? Need(string header)
        {
            var v = cell(s.Cols.First(c => c.Header == header));
            if (string.IsNullOrWhiteSpace(v)) errors.Add($"{header} is required.");
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        string? code = Need(s.DefKey == "employees" ? "Employee number" : "Code");
        int? parent = null;
        switch (s.DefKey)
        {
            case "buildings":
                parent = Loc(Need("Location code"));
                break;
            case "floors":
                var l = Loc(Need("Location code")); var b = Need("Building code");
                if (l is { } li && b is not null) parent = d.Buildings.TryGetValue((li, b), out var bi) ? bi : Missing($"Building '{b}' does not exist at location '{cell(s.Cols[0])}'.");
                break;
            case "rooms":
                var l2 = Loc(Need("Location code")); var b2 = Need("Building code"); var f2 = Need("Floor code");
                if (l2 is { } li2 && b2 is not null && f2 is not null)
                {
                    if (!d.Buildings.TryGetValue((li2, b2), out var bi2)) Missing($"Building '{b2}' does not exist at location '{cell(s.Cols[0])}'.");
                    else parent = d.Floors.TryGetValue((bi2, f2), out var fi2) ? fi2 : Missing($"Floor '{f2}' does not exist in building '{b2}'.");
                }
                break;
            case "subcategories":
                var cc = Need("Category code");
                if (cc is not null) parent = d.Categories.TryGetValue(cc, out var ci) ? ci : Missing($"Category '{cc}' does not exist.");
                break;
        }
        return (parent, code);

        int? Loc(string? lc) => lc is null ? null : d.Locations.TryGetValue(lc, out var id) ? id : Missing($"Location '{lc}' does not exist.");
        int? Missing(string message) { errors.Add(message + " Add it on its sheet (or check the spelling)."); return null; }
    }

    private static int? Lookup(Sheet s, Directory d, int? parent, string code) => s.DefKey switch
    {
        "manufacturers" => d.Manufacturers.TryGetValue(code, out var i) ? i : null,
        "vendors" => d.Vendors.TryGetValue(code, out var i) ? i : null,
        "locations" => d.Locations.TryGetValue(code, out var i) ? i : null,
        "categories" => d.Categories.TryGetValue(code, out var i) ? i : null,
        "departments" => d.Departments.TryGetValue(code, out var i) ? i : null,
        "employees" => d.Employees.TryGetValue(code, out var i) ? i : null,
        "buildings" => d.Buildings.TryGetValue((parent!.Value, code), out var i) ? i : null,
        "floors" => d.Floors.TryGetValue((parent!.Value, code), out var i) ? i : null,
        "rooms" => d.Rooms.TryGetValue((parent!.Value, code), out var i) ? i : null,
        "subcategories" => d.SubCategories.TryGetValue((parent!.Value, code), out var i) ? i : null,
        _ => null
    };

    private static void Remember(Sheet s, Directory d, int? parent, string code, int id)
    {
        d.Changed();
        switch (s.DefKey)
        {
            case "manufacturers": d.Manufacturers[code] = id; break;
            case "vendors": d.Vendors[code] = id; break;
            case "locations": d.Locations[code] = id; break;
            case "categories": d.Categories[code] = id; break;
            case "departments": d.Departments[code] = id; break;
            case "employees": d.Employees[code] = id; break;
            case "buildings": d.Buildings[(parent!.Value, code)] = id; break;
            case "floors": d.Floors[(parent!.Value, code)] = id; break;
            case "rooms": d.Rooms[(parent!.Value, code)] = id; break;
            case "subcategories": d.SubCategories[(parent!.Value, code)] = id; break;
        }
    }

    /// <summary>Converts one cell to the form's text format; null when the column is not written (or on error, added to <paramref name="errors"/>).</summary>
    private string? Convert(Sheet s, Col c, string? cell, Directory d, Dictionary<string, string?> form, List<string> errors)
    {
        if (c.Field is null || c.Key && c.Ref == RefKind.None) return null;   // keys are handled separately
        if (c.Ref != RefKind.None)
        {
            if (c.Key) return null;
            if (string.IsNullOrWhiteSpace(cell))
            {
                // empty, but the current reference cannot be written as a code (e.g. a deleted record): the export left this
                // cell empty, so it means "not touched", not "clear it"
                if (form.GetValueOrDefault(c.Field) is { } cur && int.TryParse(cur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var curId) && !d.Known(c.Ref, curId))
                    return null;
                if (c.Ref == RefKind.NumberSeries) form["NewSeriesCode"] = null;
                return "";
            }
            int? id = c.Ref switch
            {
                RefKind.Location => d.Locations.TryGetValue(cell, out var i) ? i : null,
                RefKind.Category => d.Categories.TryGetValue(cell, out var i) ? i : null,
                RefKind.Department => d.Departments.TryGetValue(cell, out var i) ? i : null,
                RefKind.Employee => d.Employees.TryGetValue(cell, out var i) ? i : null,
                RefKind.NumberSeries => d.Series.TryGetValue(cell, out var i) ? i : null,
                _ => null
            };
            if (c.Ref == RefKind.NumberSeries && id is null)
            {
                form["NewSeriesCode"] = cell.ToUpperInvariant();   // a new number series, created by the category's own rules
                return "";
            }
            if (id is null)
            {
                errors.Add($"{c.Header}: '{cell}' does not exist. Add it on its sheet (or check the spelling).");
                return null;
            }
            if (c.Ref == RefKind.NumberSeries) form["NewSeriesCode"] = null;
            return id.Value.ToString(CultureInfo.InvariantCulture);
        }

        var f = FieldOf(s, c.Field);
        // an empty Yes/No cell keeps the current value (new records: Active = Yes, others No) - never deactivates by accident
        if (string.IsNullOrWhiteSpace(cell)) return f.Kind == FieldKind.Boolean ? null : "";
        switch (f.Kind)
        {
            case FieldKind.Boolean:
                var b = cell.Trim().ToUpperInvariant();
                if (b is "YES" or "Y" or "TRUE" or "1") return "true";
                if (b is "NO" or "N" or "FALSE" or "0") return "false";
                errors.Add($"{c.Header}: write Yes or No (not '{cell}').");
                return null;
            case FieldKind.Date:
                if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2958466)
                    return DateOnly.FromDateTime(DateTime.FromOADate(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                foreach (var fmt in new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MMM-yyyy", "d MMM yyyy", "dd MMM yyyy" })
                    if (DateOnly.TryParseExact(cell, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                errors.Add($"{c.Header}: '{cell}' is not a date (use an Excel date or yyyy-MM-dd).");
                return null;
            case FieldKind.WholeNumber:
                if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == Math.Floor(n) && Math.Abs(n) < int.MaxValue)
                    return ((int)n).ToString(CultureInfo.InvariantCulture);
                errors.Add($"{c.Header}: '{cell}' is not a whole number.");
                return null;
            case FieldKind.EnumSelect:
                // "On leave" / "onleave" -> OnLeave: the exact name, so an unchanged row is recognised as unchanged
                var compact = new string(cell.Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '_').ToArray());
                var enumType = f.OptionsKey is { } ok && ok.StartsWith(Lookups.LookupKeys.EnumPrefix, StringComparison.Ordinal)
                    ? typeof(EmploymentStatus).Assembly.GetType("AMGH.ITInventory.Domain.Enums." + ok[Lookups.LookupKeys.EnumPrefix.Length..]) : null;
                return enumType is not null && Enum.TryParse(enumType, compact, ignoreCase: true, out var parsed) && Enum.IsDefined(enumType, parsed!)
                    ? parsed!.ToString()!
                    : compact;   // not a valid value: the list's own rules report it
            default:
                return cell;
        }
    }
}
