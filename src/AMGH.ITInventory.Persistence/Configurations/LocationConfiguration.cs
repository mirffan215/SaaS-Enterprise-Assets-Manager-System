using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Location"/> to dbo.tblLocation. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("tblLocation", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.LocationId).HasName("PK_tblLocation");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.LocationId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.LocationCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.LocationName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(e => e.AddressLine).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.City).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.Country).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.LocationCode }, "UX_tblLocation_LocationCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblLocation_CompanyId").HasFilter(null);
    }
}
