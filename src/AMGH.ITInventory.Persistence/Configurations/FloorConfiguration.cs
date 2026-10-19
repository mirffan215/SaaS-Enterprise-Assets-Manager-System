using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Floor"/> to dbo.tblFloor. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class FloorConfiguration : IEntityTypeConfiguration<Floor>
{
    public void Configure(EntityTypeBuilder<Floor> builder)
    {
        builder.ToTable("tblFloor", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.FloorId).HasName("PK_tblFloor");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.FloorId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.BuildingId).HasColumnType("int").IsRequired();
        builder.Property(e => e.FloorCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.FloorName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.FloorNumber).HasColumnType("smallint").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.Building).WithMany(p => p.Floors)
            .HasForeignKey(e => e.BuildingId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblFloor_BuildingId");

        builder.HasIndex(e => new { e.CompanyId, e.BuildingId, e.FloorCode }, "UX_tblFloor_BuildingId_FloorCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.BuildingId, "IX_tblFloor_BuildingId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblFloor_CompanyId").HasFilter(null);
    }
}
