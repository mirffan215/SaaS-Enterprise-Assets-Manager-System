using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAsset</c>.
/// </summary>
public class Asset : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int AssetId { get; set; }
    public string AssetNumber { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public int AssetCategoryId { get; set; }
    public int? AssetSubCategoryId { get; set; }
    public int AssetStatusId { get; set; }
    public int? ManufacturerId { get; set; }
    public string? ModelName { get; set; }
    public string? SerialNumber { get; set; }
    public string? LegacyAssetTag { get; set; }
    public string? Description { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? OperatingSystem { get; set; }
    public string? OsVersion { get; set; }
    public string? Processor { get; set; }
    public decimal? RamGb { get; set; }
    public decimal? StorageGb { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? ImeiNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public int? VendorId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public int? CurrencyId { get; set; }
    public int? UsefulLifeMonths { get; set; }
    public decimal? SalvageValue { get; set; }
    // Release 2.9: fixed-asset fields (script 16). The recorded book value comes from finance (D365); nothing is calculated here.
    public DateOnly? CapitalizationDate { get; set; }
    public string? DepreciationMethod { get; set; }
    public string? FixedAssetGroup { get; set; }
    public string? ErpFixedAssetId { get; set; }
    public decimal? RecordedBookValue { get; set; }
    public DateOnly? RecordedBookValueDate { get; set; }
    public string? CostCenter { get; set; }
    public string? AssetCondition { get; set; }
    public bool MaintenanceRequired { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public int? BuildingId { get; set; }
    public int? FloorId { get; set; }
    public int? RoomId { get; set; }
    /// <summary>Accountable asset owner (ISO 27001 A.5.9).</summary>
    public int? AssetOwnerEmployeeId { get; set; }
    public int? ClassificationId { get; set; }
    public int? CriticalityId { get; set; }
    public int? RiskLevelId { get; set; }
    public bool StoresSensitiveData { get; set; }
    public bool IsDiskEncrypted { get; set; }
    public bool EndpointProtectionInstalled { get; set; }
    public bool IsMdmManaged { get; set; }
    public bool BackupRequired { get; set; }
    public DateOnly? LastSecurityReviewDate { get; set; }
    public string? SecurityNotes { get; set; }
    public DateOnly? CommissionDate { get; set; }
    public DateOnly? EndOfSupportDate { get; set; }
    public DateOnly? EndOfLifeDate { get; set; }
    public DateOnly? NextAuditDueDate { get; set; }
    public DateOnly? DecommissionDate { get; set; }
    public string? Notes { get; set; }

    public virtual AssetCategory AssetCategory { get; set; } = null!;
    public virtual AssetSubCategory? AssetSubCategory { get; set; }
    public virtual AssetStatus AssetStatus { get; set; } = null!;
    public virtual Manufacturer? Manufacturer { get; set; }
    public virtual Vendor? Vendor { get; set; }
    public virtual Currency? Currency { get; set; }
    public virtual Employee? AssignedEmployee { get; set; }
    public virtual Department? Department { get; set; }
    public virtual Location? Location { get; set; }
    public virtual Building? Building { get; set; }
    public virtual Floor? Floor { get; set; }
    public virtual Room? Room { get; set; }
    public virtual Employee? AssetOwner { get; set; }
    public virtual Classification? Classification { get; set; }
    public virtual Criticality? Criticality { get; set; }
    public virtual RiskLevel? RiskLevel { get; set; }

    public virtual ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
    public virtual ICollection<AssetMovement> Movements { get; set; } = new List<AssetMovement>();
    public virtual ICollection<AssetMaintenance> MaintenanceRecords { get; set; } = new List<AssetMaintenance>();
    public virtual ICollection<AssetAudit> Audits { get; set; } = new List<AssetAudit>();
    public virtual ICollection<AssetDisposal> Disposals { get; set; } = new List<AssetDisposal>();
    public virtual ICollection<Warranty> Warranties { get; set; } = new List<Warranty>();
    public virtual ICollection<SoftwareLicenseAllocation> LicenseAllocations { get; set; } = new List<SoftwareLicenseAllocation>();
    public virtual ICollection<AssetQrCode> QrCodes { get; set; } = new List<AssetQrCode>();
    public virtual ICollection<AssetDocument> Documents { get; set; } = new List<AssetDocument>();
    public virtual ICollection<AssetHistory> History { get; set; } = new List<AssetHistory>();
}
