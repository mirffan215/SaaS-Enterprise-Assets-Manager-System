using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Warranty"/> to dbo.tblWarranty. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class WarrantyConfiguration : IEntityTypeConfiguration<Warranty>
{
    public void Configure(EntityTypeBuilder<Warranty> builder)
    {
        builder.ToTable("tblWarranty", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblWarranty_WarrantyType_Domain", "[WarrantyType] IN (N'Manufacturer', N'Extended', N'SupportContract')");
            tb.HasCheckConstraint("CK_tblWarranty_Cost", "[Cost] IS NULL OR [Cost] >= 0");
            tb.HasCheckConstraint("CK_tblWarranty_Rule01", "[EndDate] >= [StartDate]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.WarrantyId).HasName("PK_tblWarranty");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.WarrantyId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.WarrantyStatusId).HasColumnType("int").IsRequired();
        builder.Property(e => e.WarrantyType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.ProviderVendorId).HasColumnType("int");
        builder.Property(e => e.ContractNumber).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.StartDate).HasColumnType("date").IsRequired();
        builder.Property(e => e.EndDate).HasColumnType("date").IsRequired();
        builder.Property(e => e.SupportLevel).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.Cost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurrencyId).HasColumnType("int");
        builder.Property(e => e.Terms).HasColumnType("nvarchar(2000)").HasMaxLength(2000);

        builder.HasOne(e => e.Asset).WithMany(p => p.Warranties)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblWarranty_AssetId");
        builder.HasOne(e => e.WarrantyStatus).WithMany()
            .HasForeignKey(e => e.WarrantyStatusId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblWarranty_WarrantyStatusId");
        builder.HasOne(e => e.ProviderVendor).WithMany()
            .HasForeignKey(e => e.ProviderVendorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblWarranty_ProviderVendorId");
        builder.HasOne(e => e.Currency).WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblWarranty_CurrencyId");

        builder.HasIndex(e => e.EndDate, "IX_tblWarranty_EndDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblWarranty_AssetId").HasFilter(null);
        builder.HasIndex(e => e.WarrantyStatusId, "IX_tblWarranty_WarrantyStatusId").HasFilter(null);
        builder.HasIndex(e => e.ProviderVendorId, "IX_tblWarranty_ProviderVendorId").HasFilter(null);
        builder.HasIndex(e => e.CurrencyId, "IX_tblWarranty_CurrencyId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblWarranty_CompanyId").HasFilter(null);
    }
}
