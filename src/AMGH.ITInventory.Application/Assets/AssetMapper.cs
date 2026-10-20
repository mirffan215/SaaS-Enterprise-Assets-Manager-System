using AMGH.ITInventory.Application.Common;
using AMGH.ITInventory.Domain.Entities;

namespace AMGH.ITInventory.Application.Assets;

/// <summary>
/// Explicit mapping between the asset form and the entity. Financial and security fields are only
/// read or written when the user holds the matching permission, so a user without
/// Assets.ViewFinancials can never see or overwrite costs.
/// </summary>
internal static class AssetMapper
{
    public static AssetEditModel ToEditModel(Asset a, AssetAccess access)
    {
        var m = new AssetEditModel
        {
            AssetId = a.AssetId,
            AssetNumber = a.AssetNumber,
            RowVersion = Convert.ToBase64String(a.RowVersion),
            AssetName = a.AssetName,
            AssetCategoryId = a.AssetCategoryId,
            AssetSubCategoryId = a.AssetSubCategoryId,
            AssetStatusId = a.AssetStatusId,
            ManufacturerId = a.ManufacturerId,
            ModelName = a.ModelName,
            SerialNumber = a.SerialNumber,
            LegacyAssetTag = a.LegacyAssetTag,
            Description = a.Description,
            Hostname = a.Hostname,
            IpAddress = a.IpAddress,
            MacAddress = a.MacAddress,
            OperatingSystem = a.OperatingSystem,
            OsVersion = a.OsVersion,
            Processor = a.Processor,
            RamGb = a.RamGb,
            StorageGb = a.StorageGb,
            FirmwareVersion = a.FirmwareVersion,
            ImeiNumber = a.ImeiNumber,
            PhoneNumber = a.PhoneNumber,
            PurchaseDate = a.PurchaseDate,
            DepartmentId = a.DepartmentId,
            LocationId = a.LocationId,
            BuildingId = a.BuildingId,
            FloorId = a.FloorId,
            RoomId = a.RoomId,
            CommissionDate = a.CommissionDate,
            EndOfSupportDate = a.EndOfSupportDate,
            EndOfLifeDate = a.EndOfLifeDate,
            NextAuditDueDate = a.NextAuditDueDate,
            Notes = a.Notes
        };
        if (access.CanViewFinancials)
        {
            m.VendorId = a.VendorId;
            m.PurchaseOrderNumber = a.PurchaseOrderNumber;
            m.InvoiceNumber = a.InvoiceNumber;
            m.PurchaseCost = a.PurchaseCost;
            m.CurrencyId = a.CurrencyId;
            m.UsefulLifeMonths = a.UsefulLifeMonths;
            m.SalvageValue = a.SalvageValue;
            m.CapitalizationDate = a.CapitalizationDate; m.DepreciationMethod = a.DepreciationMethod; m.FixedAssetGroup = a.FixedAssetGroup;
            m.ErpFixedAssetId = a.ErpFixedAssetId; m.RecordedBookValue = a.RecordedBookValue; m.RecordedBookValueDate = a.RecordedBookValueDate;
        }
        m.CostCenter = a.CostCenter; m.AssetCondition = a.AssetCondition; m.MaintenanceRequired = a.MaintenanceRequired;
        if (access.CanViewSecurityDetails)
        {
            m.AssetOwnerEmployeeId = a.AssetOwnerEmployeeId;
            m.ClassificationId = a.ClassificationId;
            m.CriticalityId = a.CriticalityId;
            m.RiskLevelId = a.RiskLevelId;
            m.StoresSensitiveData = a.StoresSensitiveData;
            m.IsDiskEncrypted = a.IsDiskEncrypted;
            m.EndpointProtectionInstalled = a.EndpointProtectionInstalled;
            m.IsMdmManaged = a.IsMdmManaged;
            m.BackupRequired = a.BackupRequired;
            m.LastSecurityReviewDate = a.LastSecurityReviewDate;
            m.SecurityNotes = a.SecurityNotes;
        }
        return m;
    }

    /// <summary>Copies the form onto the entity. AssetNumber, custodian and audit columns are never touched.</summary>
    public static void Apply(AssetEditModel m, Asset a, AssetAccess access, bool isNew)
    {
        a.AssetName = m.AssetName;
        a.AssetCategoryId = m.AssetCategoryId;
        a.AssetSubCategoryId = m.AssetSubCategoryId;
        a.AssetStatusId = m.AssetStatusId;
        a.ManufacturerId = m.ManufacturerId;
        a.ModelName = m.ModelName;
        a.SerialNumber = m.SerialNumber;
        a.LegacyAssetTag = m.LegacyAssetTag;
        a.Description = m.Description;
        a.Hostname = m.Hostname;
        a.IpAddress = m.IpAddress;
        a.MacAddress = m.MacAddress;
        a.OperatingSystem = m.OperatingSystem;
        a.OsVersion = m.OsVersion;
        a.Processor = m.Processor;
        a.RamGb = m.RamGb;
        a.StorageGb = m.StorageGb;
        a.FirmwareVersion = m.FirmwareVersion;
        a.ImeiNumber = m.ImeiNumber;
        a.PhoneNumber = m.PhoneNumber;
        a.PurchaseDate = m.PurchaseDate;
        a.DepartmentId = m.DepartmentId;
        a.LocationId = m.LocationId;
        a.BuildingId = m.BuildingId;
        a.FloorId = m.FloorId;
        a.RoomId = m.RoomId;
        a.CommissionDate = m.CommissionDate;
        a.EndOfSupportDate = m.EndOfSupportDate;
        a.EndOfLifeDate = m.EndOfLifeDate;
        a.NextAuditDueDate = m.NextAuditDueDate;
        a.Notes = m.Notes;

        if (access.CanViewFinancials)
        {
            a.VendorId = m.VendorId;
            a.PurchaseOrderNumber = m.PurchaseOrderNumber;
            a.InvoiceNumber = m.InvoiceNumber;
            a.PurchaseCost = m.PurchaseCost;
            a.CurrencyId = m.CurrencyId;
            a.UsefulLifeMonths = m.UsefulLifeMonths;
            a.SalvageValue = m.SalvageValue;
            // the fixed-asset fields other than the D365 ID are no longer edited here: values already stored are left as they are
            a.ErpFixedAssetId = Clean(m.ErpFixedAssetId);
        }
        a.CostCenter = Clean(m.CostCenter); a.AssetCondition = Clean(m.AssetCondition); a.MaintenanceRequired = m.MaintenanceRequired;
        if (access.CanViewSecurityDetails)
        {
            a.AssetOwnerEmployeeId = m.AssetOwnerEmployeeId;
            a.ClassificationId = m.ClassificationId;
            a.CriticalityId = m.CriticalityId;
            a.RiskLevelId = m.RiskLevelId;
            a.StoresSensitiveData = m.StoresSensitiveData;
            a.IsDiskEncrypted = m.IsDiskEncrypted;
            a.EndpointProtectionInstalled = m.EndpointProtectionInstalled;
            a.IsMdmManaged = m.IsMdmManaged;
            a.BackupRequired = m.BackupRequired;
            a.LastSecurityReviewDate = m.LastSecurityReviewDate;
            a.SecurityNotes = m.SecurityNotes;
        }
        if (isNew && !access.CanViewFinancials) a.CurrencyId = null;
    }

    /// <summary>Discards values for sections the user is not allowed to set.</summary>
    public static void ClearRestricted(AssetEditModel m, AssetAccess access)
    {
        if (!access.CanViewFinancials)
        {
            m.VendorId = null; m.PurchaseOrderNumber = null; m.InvoiceNumber = null; m.PurchaseCost = null;
            m.CurrencyId = null; m.UsefulLifeMonths = null; m.SalvageValue = null;
            m.CapitalizationDate = null; m.DepreciationMethod = null; m.FixedAssetGroup = null; m.ErpFixedAssetId = null;
            m.RecordedBookValue = null; m.RecordedBookValueDate = null;
        }
        if (!access.CanViewSecurityDetails)
        {
            m.AssetOwnerEmployeeId = null; m.ClassificationId = null; m.CriticalityId = null; m.RiskLevelId = null;
            m.StoresSensitiveData = false; m.IsDiskEncrypted = false; m.EndpointProtectionInstalled = false;
            m.IsMdmManaged = false; m.BackupRequired = false; m.LastSecurityReviewDate = null; m.SecurityNotes = null;
        }
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
