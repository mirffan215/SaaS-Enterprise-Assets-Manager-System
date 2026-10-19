using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Building"/> to dbo.tblBuilding. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("tblBuilding", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.BuildingId).HasName("PK_tblBuilding");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.BuildingId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.LocationId).HasColumnType("int").IsRequired();
        builder.Property(e => e.BuildingCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.BuildingName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.Location).WithMany(p => p.Buildings)
            .HasForeignKey(e => e.LocationId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblBuilding_LocationId");

        builder.HasIndex(e => new { e.CompanyId, e.LocationId, e.BuildingCode }, "UX_tblBuilding_LocationId_BuildingCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.LocationId, "IX_tblBuilding_LocationId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblBuilding_CompanyId").HasFilter(null);
    }
}
