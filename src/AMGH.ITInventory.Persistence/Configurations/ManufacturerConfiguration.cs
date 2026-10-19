using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Manufacturer"/> to dbo.tblManufacturer. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.ToTable("tblManufacturer", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.ManufacturerId).HasName("PK_tblManufacturer");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ManufacturerId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.ManufacturerCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ManufacturerName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Website).HasColumnType("nvarchar(250)").HasMaxLength(250);
        builder.Property(e => e.SupportEmail).HasColumnType("nvarchar(256)").HasMaxLength(256);
        builder.Property(e => e.SupportPhone).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.ManufacturerCode }, "UX_tblManufacturer_ManufacturerCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.ManufacturerName }, "UX_tblManufacturer_ManufacturerName").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblManufacturer_CompanyId").HasFilter(null);
    }
}
