using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="SoftwareLicense"/> to dbo.tblSoftwareLicense. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class SoftwareLicenseConfiguration : IEntityTypeConfiguration<SoftwareLicense>
{
    public void Configure(EntityTypeBuilder<SoftwareLicense> builder)
    {
        builder.ToTable("tblSoftwareLicense", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblSoftwareLicense_NoHardDelete");
            tb.HasTrigger("trg_tblSoftwareLicense_ProtectNumber");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_LicenseNumber", "[LicenseNumber] NOT LIKE N'%[^A-Z0-9/._-]%' AND [LicenseNumber] LIKE N'%LIC%' AND LEN([LicenseNumber]) BETWEEN 3 AND 30");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_LicenseType_Domain", "[LicenseType] IN (N'Perpetual', N'Subscription', N'Oem', N'Volume', N'PerUser', N'PerDevice', N'OpenSource', N'Freeware')");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_TotalSeats", "[TotalSeats] IS NULL OR [TotalSeats] > 0");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_Cost", "[Cost] IS NULL OR [Cost] >= 0");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_RenewalReminderDays", "[RenewalReminderDays] BETWEEN 0 AND 365");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_Rule01", "[ExpiryDate] IS NULL OR [StartDate] IS NULL OR [ExpiryDate] >= [StartDate]");
            tb.HasCheckConstraint("CK_tblSoftwareLicense_Rule02", "[LicenseType] NOT IN (N'Subscription') OR [ExpiryDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.SoftwareLicenseId).HasName("PK_tblSoftwareLicense");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.SoftwareLicenseId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.LicenseNumber).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.SoftwareName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.SoftwareVersion).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.PublisherManufacturerId).HasColumnType("int");
        builder.Property(e => e.VendorId).HasColumnType("int");
        builder.Property(e => e.LicenseType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.LicenseKeyEncrypted).HasColumnType("varbinary(max)");
        builder.Property(e => e.TotalSeats).HasColumnType("int");
        builder.Property(e => e.PurchaseDate).HasColumnType("date");
        builder.Property(e => e.StartDate).HasColumnType("date");
        builder.Property(e => e.ExpiryDate).HasColumnType("date");
        builder.Property(e => e.PurchaseOrderNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.InvoiceNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.Cost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurrencyId).HasColumnType("int");
        builder.Property(e => e.RenewalReminderDays).HasColumnType("int").IsRequired();
        builder.Property(e => e.ContractOwnerEmployeeId).HasColumnType("int");
        builder.Property(e => e.Notes).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.Publisher).WithMany()
            .HasForeignKey(e => e.PublisherManufacturerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicense_PublisherManufacturerId");
        builder.HasOne(e => e.Vendor).WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicense_VendorId");
        builder.HasOne(e => e.Currency).WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicense_CurrencyId");
        builder.HasOne(e => e.ContractOwner).WithMany()
            .HasForeignKey(e => e.ContractOwnerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicense_ContractOwnerEmployeeId");

        builder.HasIndex(e => e.LicenseNumber, "UX_tblSoftwareLicense_LicenseNumber").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.ExpiryDate, "IX_tblSoftwareLicense_ExpiryDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.PublisherManufacturerId, "IX_tblSoftwareLicense_PublisherManufacturerId").HasFilter(null);
        builder.HasIndex(e => e.VendorId, "IX_tblSoftwareLicense_VendorId").HasFilter(null);
        builder.HasIndex(e => e.CurrencyId, "IX_tblSoftwareLicense_CurrencyId").HasFilter(null);
        builder.HasIndex(e => e.ContractOwnerEmployeeId, "IX_tblSoftwareLicense_ContractOwnerEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblSoftwareLicense_CompanyId").HasFilter(null);
    }
}
