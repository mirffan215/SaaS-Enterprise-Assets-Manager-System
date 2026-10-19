using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMGH.ITInventory.Persistence;

// GENERATED - one DbSet per table in deployment/SQLScripts/02_Tables.sql.
public sealed partial class AMGHDbContext
{
    /// <summary>dbo.tblAssetNumberSequence</summary>
    public DbSet<AssetNumberSequence> AssetNumberSequences => Set<AssetNumberSequence>();
    /// <summary>dbo.tblAssetCategory</summary>
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    /// <summary>dbo.tblAssetSubCategory</summary>
    public DbSet<AssetSubCategory> AssetSubCategories => Set<AssetSubCategory>();
    /// <summary>dbo.tblManufacturer</summary>
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    /// <summary>dbo.tblVendor</summary>
    public DbSet<Vendor> Vendors => Set<Vendor>();
    /// <summary>dbo.tblLocation</summary>
    public DbSet<Location> Locations => Set<Location>();
    /// <summary>dbo.tblBuilding</summary>
    public DbSet<Building> Buildings => Set<Building>();
    /// <summary>dbo.tblFloor</summary>
    public DbSet<Floor> Floors => Set<Floor>();
    /// <summary>dbo.tblRoom</summary>
    public DbSet<Room> Rooms => Set<Room>();
    /// <summary>dbo.tblDepartment</summary>
    public DbSet<Department> Departments => Set<Department>();
    /// <summary>dbo.tblEmployee</summary>
    public DbSet<Employee> Employees => Set<Employee>();
    /// <summary>dbo.tblClassification</summary>
    public DbSet<Classification> Classifications => Set<Classification>();
    /// <summary>dbo.tblRiskLevel</summary>
    public DbSet<RiskLevel> RiskLevels => Set<RiskLevel>();
    /// <summary>dbo.tblCriticality</summary>
    public DbSet<Criticality> Criticalities => Set<Criticality>();
    /// <summary>dbo.tblAssetStatus</summary>
    public DbSet<AssetStatus> AssetStatuses => Set<AssetStatus>();
    /// <summary>dbo.tblCurrency</summary>
    public DbSet<Currency> Currencies => Set<Currency>();
    /// <summary>dbo.tblDisposalMethod</summary>
    public DbSet<DisposalMethod> DisposalMethods => Set<DisposalMethod>();
    /// <summary>dbo.tblWarrantyStatus</summary>
    public DbSet<WarrantyStatus> WarrantyStatuses => Set<WarrantyStatus>();
    /// <summary>dbo.tblUser</summary>
    public DbSet<User> Users => Set<User>();
    /// <summary>dbo.tblRole</summary>
    public DbSet<Role> Roles => Set<Role>();
    /// <summary>dbo.tblPermission</summary>
    public DbSet<Permission> Permissions => Set<Permission>();
    /// <summary>dbo.tblRolePermission</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    /// <summary>dbo.tblUserRole</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    /// <summary>dbo.tblUserPasswordHistory</summary>
    public DbSet<UserPasswordHistory> UserPasswordHistories => Set<UserPasswordHistory>();
    /// <summary>dbo.tblLoginAudit</summary>
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    /// <summary>dbo.tblAuditLog</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    /// <summary>dbo.tblAsset</summary>
    public DbSet<Asset> Assets => Set<Asset>();
    /// <summary>dbo.tblAssetAssignment</summary>
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    /// <summary>dbo.tblAssetMovement</summary>
    public DbSet<AssetMovement> AssetMovements => Set<AssetMovement>();
    /// <summary>dbo.tblAssetMaintenance</summary>
    public DbSet<AssetMaintenance> AssetMaintenances => Set<AssetMaintenance>();
    /// <summary>dbo.tblAssetAudit</summary>
    public DbSet<AssetAudit> AssetAudits => Set<AssetAudit>();
    /// <summary>dbo.tblDataExportRequest (script 19)</summary>
    public DbSet<DataExportRequest> DataExportRequests => Set<DataExportRequest>();
    /// <summary>dbo.tblAssetDisposal</summary>
    public DbSet<AssetDisposal> AssetDisposals => Set<AssetDisposal>();
    /// <summary>dbo.tblWarranty</summary>
    public DbSet<Warranty> Warranties => Set<Warranty>();
    /// <summary>dbo.tblSoftwareLicense</summary>
    public DbSet<SoftwareLicense> SoftwareLicenses => Set<SoftwareLicense>();
    /// <summary>dbo.tblSoftwareLicenseAllocation</summary>
    public DbSet<SoftwareLicenseAllocation> SoftwareLicenseAllocations => Set<SoftwareLicenseAllocation>();
    /// <summary>dbo.tblAssetQRCode</summary>
    public DbSet<AssetQrCode> AssetQrCodes => Set<AssetQrCode>();
    /// <summary>dbo.tblQrLabelPrintLog</summary>
    public DbSet<QrLabelPrintLog> QrLabelPrintLogs => Set<QrLabelPrintLog>();
    /// <summary>dbo.tblAssetDocument</summary>
    public DbSet<AssetDocument> AssetDocuments => Set<AssetDocument>();
    /// <summary>dbo.tblAssetHistory</summary>
    public DbSet<AssetHistory> AssetHistories => Set<AssetHistory>();
    /// <summary>dbo.tblNotification</summary>
    public DbSet<Notification> Notifications => Set<Notification>();
    /// <summary>dbo.tblNotificationQueue</summary>
    public DbSet<NotificationQueueItem> NotificationQueueItems => Set<NotificationQueueItem>();

    /// <summary>
    /// Master/lookup entities are NOT hidden by the soft-delete query filter, so historic
    /// records keep resolving their references after a lookup value is retired. Pickers must
    /// filter explicitly with <c>.NotDeleted()</c> (and IsActive where present).
    /// </summary>
    internal static readonly IReadOnlySet<Type> UnfilteredLookupTypes = new HashSet<Type>
    {
        typeof(AssetNumberSequence),
        typeof(AssetCategory),
        typeof(AssetSubCategory),
        typeof(Manufacturer),
        typeof(Vendor),
        typeof(Location),
        typeof(Building),
        typeof(Floor),
        typeof(Room),
        typeof(Department),
        typeof(Employee),
        typeof(Classification),
        typeof(RiskLevel),
        typeof(Criticality),
        typeof(AssetStatus),
        typeof(Currency),
        typeof(DisposalMethod),
        typeof(WarrantyStatus)
    };
}
