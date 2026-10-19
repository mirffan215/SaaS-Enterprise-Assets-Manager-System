using System.Linq.Expressions;
using AMGH.ITInventory.Application.Abstractions.Persistence;
using AMGH.ITInventory.Application.Lookups;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Results;
using F = AMGH.ITInventory.Application.MasterData.FieldDefinition;
using FK = AMGH.ITInventory.Application.MasterData.FieldKind;

namespace AMGH.ITInventory.Application.MasterData;

// ============================================================ Manufacturers
internal sealed class ManufacturerDefinition : MasterDataDefinition<Manufacturer>
{
    public override MasterDataTypeInfo Info { get; } = new("manufacturers", "Manufacturers", "Manufacturer", "Makers of hardware and publishers of software.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true, Help: "Short code used in imports, e.g. DELL."),
        new("Name", "Name", Required: true, MaxLength: 150, ShowInList: true),
        new("Website", "Website", FK.Url, MaxLength: 250),
        new("SupportEmail", "Support e-mail", FK.Email, MaxLength: 256, ShowInList: true),
        new("SupportPhone", "Support phone", MaxLength: 30, ShowInList: true),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Manufacturer, int>> Id => x => x.ManufacturerId;
    protected override Expression<Func<Manufacturer, string>> SortKey => x => x.ManufacturerName;
    protected override Expression<Func<Manufacturer, bool>> Search(string t) => x => x.ManufacturerCode.Contains(t) || x.ManufacturerName.Contains(t);
    protected override Expression<Func<Manufacturer, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Manufacturer e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Manufacturer e) => new Dictionary<string, object?>
    {
        ["Code"] = e.ManufacturerCode, ["Name"] = e.ManufacturerName, ["Website"] = e.Website,
        ["SupportEmail"] = e.SupportEmail, ["SupportPhone"] = e.SupportPhone, ["IsActive"] = e.IsActive
    };
    protected override void Apply(Manufacturer e, FieldValues v, bool isNew)
    {
        e.ManufacturerCode = Code(v, "Code"); e.ManufacturerName = v.RequiredText("Name"); e.Website = v.Text("Website");
        e.SupportEmail = v.Text("SupportEmail"); e.SupportPhone = v.Text("SupportPhone"); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var code = Code(v, "Code"); var name = v.RequiredText("Name"); var r = uow.Repository<Manufacturer>(); var e = new List<Error>();
        if (await r.AnyAsync(x => x.ManufacturerCode == code && x.ManufacturerId != id, ct)) e.Add(Duplicate("code", "Code"));
        if (await r.AnyAsync(x => x.ManufacturerName == name && x.ManufacturerId != id, ct)) e.Add(Duplicate("name", "Name"));
        return e;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.ManufacturerId == id, ct) ? "assets refer to it." :
        await uow.Repository<SoftwareLicense>().AnyAsync(l => l.PublisherManufacturerId == id, ct) ? "software licenses refer to it." : null;
}

// ============================================================ Vendors
internal sealed class VendorDefinition : MasterDataDefinition<Vendor>
{
    public override MasterDataTypeInfo Info { get; } = new("vendors", "Vendors", "Vendor", "Suppliers of equipment, software and support (ISO 27001 A.5.19).");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 200, ShowInList: true),
        new("ContactPerson", "Contact person", MaxLength: 150, ShowInList: true),
        new("Email", "E-mail", FK.Email, MaxLength: 256),
        new("Phone", "Phone", MaxLength: 30, ShowInList: true),
        new("Address", "Address", FK.TextArea, MaxLength: 500),
        new("CommercialRegistrationNo", "Commercial registration no.", MaxLength: 30),
        new("VatNumber", "VAT number", MaxLength: 30),
        new("IsApproved", "Approved supplier", FK.Boolean, ShowInList: true),
        new("ApprovalDate", "Approval date", FK.Date, Help: "Required when the supplier is approved."),
        new("SecurityAssessmentDate", "Last security assessment", FK.Date, Help: "Supplier information-security review date."),
        new("Notes", "Notes", FK.TextArea, MaxLength: 1000),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Vendor, int>> Id => x => x.VendorId;
    protected override Expression<Func<Vendor, string>> SortKey => x => x.VendorName;
    protected override Expression<Func<Vendor, bool>> Search(string t) => x => x.VendorCode.Contains(t) || x.VendorName.Contains(t)
        || (x.ContactPerson != null && x.ContactPerson.Contains(t));
    protected override Expression<Func<Vendor, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Vendor e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Vendor e) => new Dictionary<string, object?>
    {
        ["Code"] = e.VendorCode, ["Name"] = e.VendorName, ["ContactPerson"] = e.ContactPerson, ["Email"] = e.Email, ["Phone"] = e.Phone,
        ["Address"] = e.Address, ["CommercialRegistrationNo"] = e.CommercialRegistrationNo, ["VatNumber"] = e.VatNumber,
        ["IsApproved"] = e.IsApproved, ["ApprovalDate"] = e.ApprovalDate, ["SecurityAssessmentDate"] = e.SecurityAssessmentDate,
        ["Notes"] = e.Notes, ["IsActive"] = e.IsActive
    };
    protected override void Apply(Vendor e, FieldValues v, bool isNew)
    {
        e.VendorCode = Code(v, "Code"); e.VendorName = v.RequiredText("Name"); e.ContactPerson = v.Text("ContactPerson");
        e.Email = v.Text("Email"); e.Phone = v.Text("Phone"); e.Address = v.Text("Address");
        e.CommercialRegistrationNo = v.Text("CommercialRegistrationNo"); e.VatNumber = v.Text("VatNumber");
        e.IsApproved = v.Bool("IsApproved"); e.ApprovalDate = v.Date("ApprovalDate"); e.SecurityAssessmentDate = v.Date("SecurityAssessmentDate");
        e.Notes = v.Text("Notes"); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var e = new List<Error>(); var code = Code(v, "Code");
        if (await uow.Repository<Vendor>().AnyAsync(x => x.VendorCode == code && x.VendorId != id, ct)) e.Add(Duplicate("code", "Code"));
        if (v.Bool("IsApproved") && v.Date("ApprovalDate") is null) e.Add(Error.Validation("Enter the approval date for an approved supplier.", "ApprovalDate"));
        return e;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.VendorId == id, ct) ? "assets were bought from it." :
        await uow.Repository<SoftwareLicense>().AnyAsync(l => l.VendorId == id, ct) ? "software licenses refer to it." :
        await uow.Repository<Warranty>().AnyAsync(w => w.ProviderVendorId == id, ct) ? "warranties refer to it." :
        await uow.Repository<AssetMaintenance>().AnyAsync(m => m.VendorId == id, ct) ? "maintenance records refer to it." : null;
}

// ============================================================ Departments
internal sealed class DepartmentDefinition : MasterDataDefinition<Department>
{
    public override MasterDataTypeInfo Info { get; } = new("departments", "Departments", "Department", "Organisational units that own and use assets.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 150, ShowInList: true),
        new("ParentDepartmentId", "Parent department", FK.Select, OptionsKey: LookupKeys.Departments, ShowInList: true),
        new("HeadEmployeeId", "Head of department", FK.Select, OptionsKey: LookupKeys.Employees, ShowInList: true),
        new("CostCenter", "Cost center", MaxLength: 30, ShowInList: true),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Department, int>> Id => x => x.DepartmentId;
    protected override Expression<Func<Department, string>> SortKey => x => x.DepartmentName;
    protected override Expression<Func<Department, bool>> Search(string t) => x => x.DepartmentCode.Contains(t) || x.DepartmentName.Contains(t);
    protected override Expression<Func<Department, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Department e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Department e) => new Dictionary<string, object?>
    {
        ["Code"] = e.DepartmentCode, ["Name"] = e.DepartmentName, ["ParentDepartmentId"] = e.ParentDepartmentId,
        ["HeadEmployeeId"] = e.HeadEmployeeId, ["CostCenter"] = e.CostCenter, ["IsActive"] = e.IsActive
    };
    protected override void Apply(Department e, FieldValues v, bool isNew)
    {
        e.DepartmentCode = Code(v, "Code"); e.DepartmentName = v.RequiredText("Name"); e.ParentDepartmentId = v.WholeNumber("ParentDepartmentId");
        e.HeadEmployeeId = v.WholeNumber("HeadEmployeeId"); e.CostCenter = v.Text("CostCenter"); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var e = new List<Error>(); var code = Code(v, "Code");
        if (await uow.Repository<Department>().AnyAsync(x => x.DepartmentCode == code && x.DepartmentId != id, ct)) e.Add(Duplicate("code", "Code"));
        if (id is not null && v.WholeNumber("ParentDepartmentId") == id) e.Add(Error.Validation("A department cannot be its own parent.", "ParentDepartmentId"));
        return e;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Employee>().AnyAsync(x => x.DepartmentId == id && !x.IsDeleted, ct) ? "employees belong to it." :
        await uow.Repository<Asset>().AnyAsync(a => a.DepartmentId == id, ct) ? "assets belong to it." :
        await uow.Repository<Department>().AnyAsync(x => x.ParentDepartmentId == id && !x.IsDeleted, ct) ? "it has sub-departments." : null;
}

// ============================================================ Employees
internal sealed class EmployeeDefinition : MasterDataDefinition<Employee>
{
    public override MasterDataTypeInfo Info { get; } = new("employees", "Employees", "Employee", "People who can be custodians or owners of assets.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("EmployeeNumber", "Employee number", Required: true, MaxLength: 30, ShowInList: true),
        new("FirstName", "First name", Required: true, MaxLength: 100, ShowInList: true),
        new("LastName", "Last name", Required: true, MaxLength: 100, ShowInList: true),
        new("Email", "E-mail", FK.Email, MaxLength: 256, ShowInList: true),
        new("Phone", "Phone", MaxLength: 30),
        new("JobTitle", "Job title", MaxLength: 150, ShowInList: true),
        new("DepartmentId", "Department", FK.Select, Required: true, OptionsKey: LookupKeys.Departments, ShowInList: true),
        new("LocationId", "Work location", FK.Select, OptionsKey: LookupKeys.Locations),
        new("ManagerEmployeeId", "Line manager", FK.Select, OptionsKey: LookupKeys.Employees),
        new("HireDate", "Hire date", FK.Date),
        new("EmploymentStatus", "Employment status", FK.EnumSelect, Required: true, OptionsKey: LookupKeys.EnumPrefix + nameof(EmploymentStatus), ShowInList: true),
        new("TerminationDate", "Termination date", FK.Date, Help: "Required when the status is Terminated. Return the person's assets first.")
    };
    protected override Expression<Func<Employee, int>> Id => x => x.EmployeeId;
    protected override Expression<Func<Employee, string>> SortKey => x => x.FirstName + " " + x.LastName;
    protected override Expression<Func<Employee, bool>> Search(string t) => x => x.EmployeeNumber.Contains(t) || x.FirstName.Contains(t)
        || x.LastName.Contains(t) || (x.Email != null && x.Email.Contains(t));
    protected override Expression<Func<Employee, bool>>? ActiveFilter => x => x.EmploymentStatus != EmploymentStatus.Terminated;
    protected override bool IsActive(Employee e) => e.EmploymentStatus != EmploymentStatus.Terminated;
    protected override IReadOnlyDictionary<string, object?> Read(Employee e) => new Dictionary<string, object?>
    {
        ["EmployeeNumber"] = e.EmployeeNumber, ["FirstName"] = e.FirstName, ["LastName"] = e.LastName, ["Email"] = e.Email,
        ["Phone"] = e.Phone, ["JobTitle"] = e.JobTitle, ["DepartmentId"] = e.DepartmentId, ["LocationId"] = e.LocationId,
        ["ManagerEmployeeId"] = e.ManagerEmployeeId, ["HireDate"] = e.HireDate, ["EmploymentStatus"] = e.EmploymentStatus,
        ["TerminationDate"] = e.TerminationDate
    };
    protected override void Apply(Employee e, FieldValues v, bool isNew)
    {
        e.EmployeeNumber = v.RequiredText("EmployeeNumber").ToUpperInvariant(); e.FirstName = v.RequiredText("FirstName");
        e.LastName = v.RequiredText("LastName"); e.Email = v.Text("Email")?.ToLowerInvariant(); e.Phone = v.Text("Phone");
        e.JobTitle = v.Text("JobTitle"); e.DepartmentId = v.WholeNumber("DepartmentId")!.Value; e.LocationId = v.WholeNumber("LocationId");
        e.ManagerEmployeeId = v.WholeNumber("ManagerEmployeeId"); e.HireDate = v.Date("HireDate");
        e.EmploymentStatus = v.Enum("EmploymentStatus", EmploymentStatus.Active); e.TerminationDate = v.Date("TerminationDate");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var e = new List<Error>(); var r = uow.Repository<Employee>();
        var number = v.RequiredText("EmployeeNumber").ToUpperInvariant(); var email = v.Text("Email")?.ToLowerInvariant();
        if (await r.AnyAsync(x => x.EmployeeNumber == number && x.EmployeeId != id, ct)) e.Add(Duplicate("employee number", "EmployeeNumber"));
        if (email is not null && await r.AnyAsync(x => x.Email == email && x.EmployeeId != id, ct)) e.Add(Duplicate("e-mail address", "Email"));
        var status = v.Enum("EmploymentStatus", EmploymentStatus.Active);
        if (status == EmploymentStatus.Terminated && v.Date("TerminationDate") is null)
            e.Add(Error.Validation("Enter the termination date.", "TerminationDate"));
        if (v.Date("TerminationDate") is { } td && v.Date("HireDate") is { } hd && td < hd)
            e.Add(Error.Validation("The termination date cannot be before the hire date.", "TerminationDate"));
        if (id is not null && v.WholeNumber("ManagerEmployeeId") == id) e.Add(Error.Validation("An employee cannot be their own manager.", "ManagerEmployeeId"));
        if (id is not null && status == EmploymentStatus.Terminated
            && await uow.Repository<Asset>().AnyAsync(a => a.AssignedEmployeeId == id, ct))
            e.Add(Error.BusinessRule("This employee still holds assets. Return them before recording the termination (ISO 27001 A.5.11)."));
        return e;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.AssignedEmployeeId == id || a.AssetOwnerEmployeeId == id, ct) ? "assets are assigned to or owned by this person." :
        await uow.Repository<AssetAssignment>().AnyAsync(a => a.EmployeeId == id, ct) ? "the assignment history refers to this person." :
        await uow.Repository<CompanyUser>().AnyAsync(m => m.EmployeeId == id, ct) ? "a user account is linked to this person." : null;
}

// ============================================================ Locations, buildings, floors, rooms
internal sealed class LocationDefinition : MasterDataDefinition<Location>
{
    public override MasterDataTypeInfo Info { get; } = new("locations", "Locations", "Location", "Sites and offices. Each location contains buildings, floors and rooms.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 150, ShowInList: true),
        new("AddressLine", "Address", FK.TextArea, MaxLength: 500),
        new("City", "City", MaxLength: 100, ShowInList: true),
        new("Country", "Country", Required: true, MaxLength: 100, ShowInList: true),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Location, int>> Id => x => x.LocationId;
    protected override Expression<Func<Location, string>> SortKey => x => x.LocationName;
    protected override Expression<Func<Location, bool>> Search(string t) => x => x.LocationCode.Contains(t) || x.LocationName.Contains(t)
        || (x.City != null && x.City.Contains(t));
    protected override Expression<Func<Location, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Location e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Location e) => new Dictionary<string, object?>
    {
        ["Code"] = e.LocationCode, ["Name"] = e.LocationName, ["AddressLine"] = e.AddressLine, ["City"] = e.City,
        ["Country"] = e.Country, ["IsActive"] = e.IsActive
    };
    protected override void Apply(Location e, FieldValues v, bool isNew)
    {
        e.LocationCode = Code(v, "Code"); e.LocationName = v.RequiredText("Name"); e.AddressLine = v.Text("AddressLine");
        e.City = v.Text("City"); e.Country = v.RequiredText("Country"); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var code = Code(v, "Code");
        return await uow.Repository<Location>().AnyAsync(x => x.LocationCode == code && x.LocationId != id, ct)
            ? new[] { Duplicate("code", "Code") } : Array.Empty<Error>();
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Building>().AnyAsync(x => x.LocationId == id && !x.IsDeleted, ct) ? "it has buildings." :
        await uow.Repository<Asset>().AnyAsync(a => a.LocationId == id, ct) ? "assets are at this location." :
        await uow.Repository<Employee>().AnyAsync(x => x.LocationId == id && !x.IsDeleted, ct) ? "employees work here." : null;
}

internal sealed class BuildingDefinition : MasterDataDefinition<Building>
{
    public override MasterDataTypeInfo Info { get; } = new("buildings", "Buildings", "Building", "Buildings at each location.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("LocationId", "Location", FK.Select, Required: true, OptionsKey: LookupKeys.Locations, ShowInList: true),
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 150, ShowInList: true),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Building, int>> Id => x => x.BuildingId;
    protected override Expression<Func<Building, string>> SortKey => x => x.BuildingName;
    protected override Expression<Func<Building, bool>> Search(string t) => x => x.BuildingCode.Contains(t) || x.BuildingName.Contains(t);
    protected override Expression<Func<Building, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Building e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Building e) => new Dictionary<string, object?>
    { ["LocationId"] = e.LocationId, ["Code"] = e.BuildingCode, ["Name"] = e.BuildingName, ["IsActive"] = e.IsActive };
    protected override void Apply(Building e, FieldValues v, bool isNew)
    {
        e.LocationId = v.WholeNumber("LocationId")!.Value; e.BuildingCode = Code(v, "Code"); e.BuildingName = v.RequiredText("Name");
        e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var code = Code(v, "Code"); var loc = v.WholeNumber("LocationId");
        return await uow.Repository<Building>().AnyAsync(x => x.LocationId == loc && x.BuildingCode == code && x.BuildingId != id, ct)
            ? new[] { Duplicate("code at this location", "Code") } : Array.Empty<Error>();
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Floor>().AnyAsync(x => x.BuildingId == id && !x.IsDeleted, ct) ? "it has floors." :
        await uow.Repository<Asset>().AnyAsync(a => a.BuildingId == id, ct) ? "assets are in this building." : null;
}

internal sealed class FloorDefinition : MasterDataDefinition<Floor>
{
    public override MasterDataTypeInfo Info { get; } = new("floors", "Floors", "Floor", "Floors in each building.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("BuildingId", "Building", FK.Select, Required: true, OptionsKey: LookupKeys.BuildingsWithLocation, ShowInList: true),
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 100, ShowInList: true),
        new("FloorNumber", "Floor number", FK.WholeNumber, Required: true, ShowInList: true, Help: "0 = ground floor, negative = basement."),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Floor, int>> Id => x => x.FloorId;
    protected override Expression<Func<Floor, string>> SortKey => x => x.FloorName;
    protected override Expression<Func<Floor, bool>> Search(string t) => x => x.FloorCode.Contains(t) || x.FloorName.Contains(t);
    protected override Expression<Func<Floor, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Floor e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Floor e) => new Dictionary<string, object?>
    { ["BuildingId"] = e.BuildingId, ["Code"] = e.FloorCode, ["Name"] = e.FloorName, ["FloorNumber"] = (int)e.FloorNumber, ["IsActive"] = e.IsActive };
    protected override void Apply(Floor e, FieldValues v, bool isNew)
    {
        e.BuildingId = v.WholeNumber("BuildingId")!.Value; e.FloorCode = Code(v, "Code"); e.FloorName = v.RequiredText("Name");
        e.FloorNumber = (short)Math.Clamp(v.WholeNumber("FloorNumber") ?? 0, -20, 200); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var e = new List<Error>(); var code = Code(v, "Code"); var b = v.WholeNumber("BuildingId");
        if (await uow.Repository<Floor>().AnyAsync(x => x.BuildingId == b && x.FloorCode == code && x.FloorId != id, ct)) e.Add(Duplicate("code in this building", "Code"));
        if (v.WholeNumber("FloorNumber") is < -20 or > 200) e.Add(Error.Validation("Floor number must be between -20 and 200.", "FloorNumber"));
        return e;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Room>().AnyAsync(x => x.FloorId == id && !x.IsDeleted, ct) ? "it has rooms." :
        await uow.Repository<Asset>().AnyAsync(a => a.FloorId == id, ct) ? "assets are on this floor." : null;
}

internal sealed class RoomDefinition : MasterDataDefinition<Room>
{
    public override MasterDataTypeInfo Info { get; } = new("rooms", "Rooms", "Room", "Rooms, server rooms and stores. Secure areas are flagged for physical-security reporting.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("FloorId", "Floor", FK.Select, Required: true, OptionsKey: LookupKeys.FloorsWithBuilding, ShowInList: true),
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 100, ShowInList: true),
        new("RoomType", "Room type", FK.EnumSelect, Required: true, OptionsKey: LookupKeys.EnumPrefix + nameof(RoomType), ShowInList: true),
        new("IsSecureArea", "Secure area", FK.Boolean, ShowInList: true, Help: "Inside a physical security perimeter (ISO 27001 A.7.1)."),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<Room, int>> Id => x => x.RoomId;
    protected override Expression<Func<Room, string>> SortKey => x => x.RoomName;
    protected override Expression<Func<Room, bool>> Search(string t) => x => x.RoomCode.Contains(t) || x.RoomName.Contains(t);
    protected override Expression<Func<Room, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(Room e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(Room e) => new Dictionary<string, object?>
    {
        ["FloorId"] = e.FloorId, ["Code"] = e.RoomCode, ["Name"] = e.RoomName, ["RoomType"] = e.RoomType,
        ["IsSecureArea"] = e.IsSecureArea, ["IsActive"] = e.IsActive
    };
    protected override void Apply(Room e, FieldValues v, bool isNew)
    {
        e.FloorId = v.WholeNumber("FloorId")!.Value; e.RoomCode = Code(v, "Code"); e.RoomName = v.RequiredText("Name");
        e.RoomType = v.Enum("RoomType", RoomType.Office); e.IsSecureArea = v.Bool("IsSecureArea"); e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var code = Code(v, "Code"); var f = v.WholeNumber("FloorId");
        return await uow.Repository<Room>().AnyAsync(x => x.FloorId == f && x.RoomCode == code && x.RoomId != id, ct)
            ? new[] { Duplicate("code on this floor", "Code") } : Array.Empty<Error>();
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.RoomId == id, ct) ? "assets are in this room." : null;
}

// ============================================================ Sub-categories
internal sealed class SubCategoryDefinition : MasterDataDefinition<AssetSubCategory>
{
    public override MasterDataTypeInfo Info { get; } = new("subcategories", "Asset sub-categories", "Sub-category", "Finer types within each asset category.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("AssetCategoryId", "Category", FK.Select, Required: true, OptionsKey: LookupKeys.Categories, ShowInList: true, LockedOnEdit: true),
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true),
        new("Name", "Name", Required: true, MaxLength: 100, ShowInList: true),
        new("Description", "Description", FK.TextArea, MaxLength: 500),
        new("SortOrder", "Sort order", FK.WholeNumber),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<AssetSubCategory, int>> Id => x => x.AssetSubCategoryId;
    protected override Expression<Func<AssetSubCategory, string>> SortKey => x => x.SubCategoryName;
    protected override Expression<Func<AssetSubCategory, bool>> Search(string t) => x => x.SubCategoryCode.Contains(t) || x.SubCategoryName.Contains(t);
    protected override Expression<Func<AssetSubCategory, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(AssetSubCategory e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(AssetSubCategory e) => new Dictionary<string, object?>
    {
        ["AssetCategoryId"] = e.AssetCategoryId, ["Code"] = e.SubCategoryCode, ["Name"] = e.SubCategoryName,
        ["Description"] = e.Description, ["SortOrder"] = e.SortOrder, ["IsActive"] = e.IsActive
    };
    protected override void Apply(AssetSubCategory e, FieldValues v, bool isNew)
    {
        if (isNew) e.AssetCategoryId = v.WholeNumber("AssetCategoryId")!.Value;
        e.SubCategoryCode = Code(v, "Code"); e.SubCategoryName = v.RequiredText("Name"); e.Description = v.Text("Description");
        e.SortOrder = v.WholeNumber("SortOrder") ?? 0; e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var code = Code(v, "Code"); var cat = v.WholeNumber("AssetCategoryId");
        return await uow.Repository<AssetSubCategory>().AnyAsync(x => x.AssetCategoryId == cat && x.SubCategoryCode == code && x.AssetSubCategoryId != id, ct)
            ? new[] { Duplicate("code in this category", "Code") } : Array.Empty<Error>();
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.AssetSubCategoryId == id, ct) ? "assets use it." : null;
}

/// <summary>
/// Asset categories. Each category numbers its assets from a number series (e.g. LAP gives AMGH-IT-LAP-00001):
/// pick an existing series or type a new series code and it is created with the company's prefix.
/// </summary>
internal sealed partial class CategoryDefinition : MasterDataDefinition<AssetCategory>
{
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial System.Text.RegularExpressions.Regex SeriesCodePattern();

    public override MasterDataTypeInfo Info { get; } = new("categories", "Asset categories", "Category",
        "Types of asset (laptop, server, printer…). Each category gives its assets numbers from a number series.");
    public override IReadOnlyList<F> Fields { get; } = new F[]
    {
        new("Code", "Code", Required: true, MaxLength: 20, ShowInList: true, LockedOnEdit: true),
        new("Name", "Name", Required: true, MaxLength: 100, ShowInList: true),
        new("AssetNumberSequenceId", "Number series", FK.Select, OptionsKey: LookupKeys.NumberSeries, ShowInList: true,
            Help: "Existing series for the asset numbers. Leave empty to create a new one below."),
        new("NewSeriesCode", "…or new series code", MaxLength: 10,
            Help: "2-10 letters or digits, e.g. PRN for printers. Assets get numbers like PREFIX-PRN-00001."),
        new("DefaultUsefulLifeMonths", "Useful life (months)", FK.WholeNumber, Help: "Used for book value; e.g. 48 for laptops."),
        new("RequiresHostname", "Requires hostname", FK.Boolean, Help: "Computers and servers: a hostname must be entered."),
        new("Description", "Description", FK.TextArea, MaxLength: 500),
        new("SortOrder", "Sort order", FK.WholeNumber),
        new("IsActive", "Active", FK.Boolean, ShowInList: true)
    };
    protected override Expression<Func<AssetCategory, int>> Id => x => x.AssetCategoryId;
    protected override Expression<Func<AssetCategory, string>> SortKey => x => x.CategoryName;
    protected override Expression<Func<AssetCategory, bool>> Search(string t) => x => x.CategoryCode.Contains(t) || x.CategoryName.Contains(t);
    protected override Expression<Func<AssetCategory, bool>>? ActiveFilter => x => x.IsActive;
    protected override bool IsActive(AssetCategory e) => e.IsActive;
    protected override IReadOnlyDictionary<string, object?> Read(AssetCategory e) => new Dictionary<string, object?>
    {
        ["Code"] = e.CategoryCode, ["Name"] = e.CategoryName, ["AssetNumberSequenceId"] = e.AssetNumberSequenceId, ["NewSeriesCode"] = null,
        ["DefaultUsefulLifeMonths"] = e.DefaultUsefulLifeMonths, ["RequiresHostname"] = e.RequiresHostname,
        ["Description"] = e.Description, ["SortOrder"] = e.SortOrder, ["IsActive"] = e.IsActive
    };
    protected override void Apply(AssetCategory e, FieldValues v, bool isNew)
    {
        if (isNew) e.CategoryCode = Code(v, "Code");
        e.CategoryName = v.RequiredText("Name");
        if (v.WholeNumber("AssetNumberSequenceId") is { } seq && string.IsNullOrWhiteSpace(v.Text("NewSeriesCode")))
            e.AssetNumberSequenceId = seq;
        e.DefaultUsefulLifeMonths = v.WholeNumber("DefaultUsefulLifeMonths");
        e.RequiresHostname = v.Bool("RequiresHostname");
        e.Description = v.Text("Description");
        e.SortOrder = v.WholeNumber("SortOrder") ?? 0;
        e.IsActive = isNew || v.Bool("IsActive");
    }
    protected override async Task<IEnumerable<Error>> ValidateAsync(IUnitOfWork uow, int? id, FieldValues v, CancellationToken ct)
    {
        var e = new List<Error>();
        var r = uow.Repository<AssetCategory>();
        var name = v.RequiredText("Name");
        if (id is null && await r.AnyAsync(x => x.CategoryCode == Code(v, "Code"), ct)) e.Add(Duplicate("code", "Code"));
        if (await r.AnyAsync(x => x.CategoryName == name && x.AssetCategoryId != id, ct)) e.Add(Duplicate("name", "Name"));

        var newCode = v.Text("NewSeriesCode")?.ToUpperInvariant();
        var existing = v.WholeNumber("AssetNumberSequenceId");
        if (newCode is null && existing is null)
            e.Add(Error.Validation("Choose a number series or enter a new series code.", "AssetNumberSequenceId"));
        if (newCode is not null)
        {
            if (!SeriesCodePattern().IsMatch(newCode))
                e.Add(Error.Validation("Series code: 2-10 letters or digits.", "NewSeriesCode"));
            else if (await uow.Repository<AssetNumberSequence>().AnyAsync(s => s.SequenceCode == newCode, ct))
                e.Add(Error.Conflict("This series already exists: choose it from the list.", "NewSeriesCode"));
            if (await uow.Repository<AssetNumberSequence>().CountAsync(null, ct) == 0)
                e.Add(Error.BusinessRule("This company has no asset number prefix yet. Ask the platform administrator to complete its setup.", "NewSeriesCode"));
        }
        else if (existing is not null && !await uow.Repository<AssetNumberSequence>().AnyAsync(s => s.AssetNumberSequenceId == existing && s.IsActive, ct))
            e.Add(Error.Validation("Choose a number series from the list.", "AssetNumberSequenceId"));
        return e;
    }
    /// <summary>A new series takes the company's prefix (all series of a company share it) and starts at 1.</summary>
    protected override async Task PrepareAsync(IUnitOfWork uow, AssetCategory e, FieldValues v, bool isNew, CancellationToken ct)
    {
        var newCode = v.Text("NewSeriesCode")?.ToUpperInvariant();
        if (newCode is null) return;
        var template = (await uow.Repository<AssetNumberSequence>().QueryAsync(q => q.OrderBy(s => s.AssetNumberSequenceId)
            .Select(s => new { s.Prefix, s.PaddingLength }).Take(1), ct))[0];
        var series = new AssetNumberSequence
        {
            SequenceCode = newCode, Prefix = template.Prefix, LastNumber = 0, PaddingLength = template.PaddingLength,
            Description = e.CategoryName, SortOrder = 100, IsActive = true
        };
        await uow.Repository<AssetNumberSequence>().AddAsync(series, ct);
        e.AssetNumberSequence = series;
    }
    protected override async Task<string?> InUseAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Repository<Asset>().AnyAsync(a => a.AssetCategoryId == id, ct) ? "assets use it." :
        await uow.Repository<AssetSubCategory>().AnyAsync(s => s.AssetCategoryId == id, ct) ? "it has sub-categories." : null;
}
