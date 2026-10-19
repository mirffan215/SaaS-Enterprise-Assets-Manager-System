using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Asset"/> to dbo.tblAsset. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("tblAsset", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblAsset_NoHardDelete");
            tb.HasTrigger("trg_tblAsset_History");
            tb.HasCheckConstraint("CK_tblAsset_AssetNumber", "[AssetNumber] NOT LIKE N'%[^A-Z0-9/._-]%' AND LEN([AssetNumber]) BETWEEN 3 AND 30");
            tb.HasCheckConstraint("CK_tblAsset_RamGb", "[RamGb] IS NULL OR [RamGb] >= 0");
            tb.HasCheckConstraint("CK_tblAsset_StorageGb", "[StorageGb] IS NULL OR [StorageGb] >= 0");
            tb.HasCheckConstraint("CK_tblAsset_PurchaseCost", "[PurchaseCost] IS NULL OR [PurchaseCost] >= 0");
            tb.HasCheckConstraint("CK_tblAsset_UsefulLifeMonths", "[UsefulLifeMonths] IS NULL OR [UsefulLifeMonths] > 0");
            tb.HasCheckConstraint("CK_tblAsset_SalvageValue", "[SalvageValue] IS NULL OR [SalvageValue] >= 0");
            tb.HasCheckConstraint("CK_tblAsset_Rule01", "[SalvageValue] IS NULL OR [PurchaseCost] IS NULL OR [SalvageValue] <= [PurchaseCost]");
            tb.HasCheckConstraint("CK_tblAsset_Rule02", "[EndOfLifeDate] IS NULL OR [PurchaseDate] IS NULL OR [EndOfLifeDate] >= [PurchaseDate]");
            tb.HasCheckConstraint("CK_tblAsset_Rule03", "[CommissionDate] IS NULL OR [PurchaseDate] IS NULL OR [CommissionDate] >= [PurchaseDate]");
            tb.HasCheckConstraint("CK_tblAsset_Rule04", "[MacAddress] IS NULL OR [MacAddress] LIKE '[0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F][:-][0-9A-F][0-9A-F]'");
        });
        builder.Property(e => e.CapitalizationDate).HasColumnType("date");
        builder.Property(e => e.DepreciationMethod).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.FixedAssetGroup).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.ErpFixedAssetId).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.RecordedBookValue).HasColumnType("decimal(18,2)");
        builder.Property(e => e.RecordedBookValueDate).HasColumnType("date");
        builder.Property(e => e.CostCenter).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetCondition).HasColumnType("nvarchar(20)").HasMaxLength(20);

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetId).HasName("PK_tblAsset");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetNumber).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.AssetName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.AssetCategoryId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetSubCategoryId).HasColumnType("int");
        builder.Property(e => e.AssetStatusId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ManufacturerId).HasColumnType("int");
        builder.Property(e => e.ModelName).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.SerialNumber).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.LegacyAssetTag).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.Description).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.Hostname).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.IpAddress).HasColumnType("nvarchar(45)").HasMaxLength(45);
        builder.Property(e => e.MacAddress).HasColumnType("nvarchar(17)").HasMaxLength(17);
        builder.Property(e => e.OperatingSystem).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.OsVersion).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.Processor).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.RamGb).HasColumnType("decimal(9,2)");
        builder.Property(e => e.StorageGb).HasColumnType("decimal(12,2)");
        builder.Property(e => e.FirmwareVersion).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.ImeiNumber).HasColumnType("nvarchar(20)").HasMaxLength(20);
        builder.Property(e => e.PhoneNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.VendorId).HasColumnType("int");
        builder.Property(e => e.PurchaseOrderNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.InvoiceNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.PurchaseDate).HasColumnType("date");
        builder.Property(e => e.PurchaseCost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurrencyId).HasColumnType("int");
        builder.Property(e => e.UsefulLifeMonths).HasColumnType("int");
        builder.Property(e => e.SalvageValue).HasColumnType("decimal(18,2)");
        builder.Property(e => e.AssignedEmployeeId).HasColumnType("int");
        builder.Property(e => e.DepartmentId).HasColumnType("int");
        builder.Property(e => e.LocationId).HasColumnType("int");
        builder.Property(e => e.BuildingId).HasColumnType("int");
        builder.Property(e => e.FloorId).HasColumnType("int");
        builder.Property(e => e.RoomId).HasColumnType("int");
        builder.Property(e => e.AssetOwnerEmployeeId).HasColumnType("int");
        builder.Property(e => e.ClassificationId).HasColumnType("int");
        builder.Property(e => e.CriticalityId).HasColumnType("int");
        builder.Property(e => e.RiskLevelId).HasColumnType("int");
        builder.Property(e => e.StoresSensitiveData).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsDiskEncrypted).HasColumnType("bit").IsRequired();
        builder.Property(e => e.EndpointProtectionInstalled).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsMdmManaged).HasColumnType("bit").IsRequired();
        builder.Property(e => e.BackupRequired).HasColumnType("bit").IsRequired();
        builder.Property(e => e.LastSecurityReviewDate).HasColumnType("date");
        builder.Property(e => e.SecurityNotes).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.CommissionDate).HasColumnType("date");
        builder.Property(e => e.EndOfSupportDate).HasColumnType("date");
        builder.Property(e => e.EndOfLifeDate).HasColumnType("date");
        builder.Property(e => e.NextAuditDueDate).HasColumnType("date");
        builder.Property(e => e.DecommissionDate).HasColumnType("date");
        builder.Property(e => e.Notes).HasColumnType("nvarchar(2000)").HasMaxLength(2000);

        builder.HasOne(e => e.AssetCategory).WithMany(p => p.Assets)
            .HasForeignKey(e => e.AssetCategoryId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_AssetCategoryId");
        builder.HasOne(e => e.AssetSubCategory).WithMany(p => p.Assets)
            .HasForeignKey(e => e.AssetSubCategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_AssetSubCategoryId");
        builder.HasOne(e => e.AssetStatus).WithMany(p => p.Assets)
            .HasForeignKey(e => e.AssetStatusId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_AssetStatusId");
        builder.HasOne(e => e.Manufacturer).WithMany(p => p.Assets)
            .HasForeignKey(e => e.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_ManufacturerId");
        builder.HasOne(e => e.Vendor).WithMany(p => p.Assets)
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_VendorId");
        builder.HasOne(e => e.Currency).WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_CurrencyId");
        builder.HasOne(e => e.AssignedEmployee).WithMany(p => p.AssignedAssets)
            .HasForeignKey(e => e.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_AssignedEmployeeId");
        builder.HasOne(e => e.Department).WithMany(p => p.Assets)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_DepartmentId");
        builder.HasOne(e => e.Location).WithMany(p => p.Assets)
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_LocationId");
        builder.HasOne(e => e.Building).WithMany()
            .HasForeignKey(e => e.BuildingId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_BuildingId");
        builder.HasOne(e => e.Floor).WithMany()
            .HasForeignKey(e => e.FloorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_FloorId");
        builder.HasOne(e => e.Room).WithMany(p => p.Assets)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_RoomId");
        builder.HasOne(e => e.AssetOwner).WithMany(p => p.OwnedAssets)
            .HasForeignKey(e => e.AssetOwnerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_AssetOwnerEmployeeId");
        builder.HasOne(e => e.Classification).WithMany()
            .HasForeignKey(e => e.ClassificationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_ClassificationId");
        builder.HasOne(e => e.Criticality).WithMany()
            .HasForeignKey(e => e.CriticalityId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_CriticalityId");
        builder.HasOne(e => e.RiskLevel).WithMany()
            .HasForeignKey(e => e.RiskLevelId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAsset_RiskLevelId");

        builder.HasIndex(e => e.AssetNumber, "UX_tblAsset_AssetNumber").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.ManufacturerId, e.SerialNumber }, "UX_tblAsset_ManufacturerId_SerialNumber").IsUnique().HasFilter("[SerialNumber] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(e => new { e.CompanyId, e.Hostname }, "UX_tblAsset_Hostname").IsUnique().HasFilter("[Hostname] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(e => new { e.IsDeleted, e.AssetStatusId, e.AssetCategoryId }, "IX_tblAsset_IsDeleted_AssetStatusId_AssetCategoryId").HasFilter(null);
        builder.HasIndex(e => e.AssetName, "IX_tblAsset_AssetName").HasFilter(null);
        builder.HasIndex(e => e.SerialNumber, "IX_tblAsset_SerialNumber").HasFilter(null);
        builder.HasIndex(e => e.NextAuditDueDate, "IX_tblAsset_NextAuditDueDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.AssetCategoryId, "IX_tblAsset_AssetCategoryId").HasFilter(null);
        builder.HasIndex(e => e.AssetSubCategoryId, "IX_tblAsset_AssetSubCategoryId").HasFilter(null);
        builder.HasIndex(e => e.AssetStatusId, "IX_tblAsset_AssetStatusId").HasFilter(null);
        builder.HasIndex(e => e.ManufacturerId, "IX_tblAsset_ManufacturerId").HasFilter(null);
        builder.HasIndex(e => e.VendorId, "IX_tblAsset_VendorId").HasFilter(null);
        builder.HasIndex(e => e.CurrencyId, "IX_tblAsset_CurrencyId").HasFilter(null);
        builder.HasIndex(e => e.AssignedEmployeeId, "IX_tblAsset_AssignedEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.DepartmentId, "IX_tblAsset_DepartmentId").HasFilter(null);
        builder.HasIndex(e => e.LocationId, "IX_tblAsset_LocationId").HasFilter(null);
        builder.HasIndex(e => e.BuildingId, "IX_tblAsset_BuildingId").HasFilter(null);
        builder.HasIndex(e => e.FloorId, "IX_tblAsset_FloorId").HasFilter(null);
        builder.HasIndex(e => e.RoomId, "IX_tblAsset_RoomId").HasFilter(null);
        builder.HasIndex(e => e.AssetOwnerEmployeeId, "IX_tblAsset_AssetOwnerEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.ClassificationId, "IX_tblAsset_ClassificationId").HasFilter(null);
        builder.HasIndex(e => e.CriticalityId, "IX_tblAsset_CriticalityId").HasFilter(null);
        builder.HasIndex(e => e.RiskLevelId, "IX_tblAsset_RiskLevelId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAsset_CompanyId").HasFilter(null);
    }
}
