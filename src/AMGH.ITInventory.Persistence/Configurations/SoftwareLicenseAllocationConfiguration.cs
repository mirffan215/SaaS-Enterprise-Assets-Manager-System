using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="SoftwareLicenseAllocation"/> to dbo.tblSoftwareLicenseAllocation. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class SoftwareLicenseAllocationConfiguration : IEntityTypeConfiguration<SoftwareLicenseAllocation>
{
    public void Configure(EntityTypeBuilder<SoftwareLicenseAllocation> builder)
    {
        builder.ToTable("tblSoftwareLicenseAllocation", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblSoftwareLicenseAllocation_SeatLimit");
            tb.HasCheckConstraint("CK_tblSoftwareLicenseAllocation_Rule01", "[AssetId] IS NOT NULL OR [EmployeeId] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblSoftwareLicenseAllocation_Rule02", "[IsCurrent] = 0 OR [RevokedDate] IS NULL");
            tb.HasCheckConstraint("CK_tblSoftwareLicenseAllocation_Rule03", "[IsCurrent] = 1 OR [RevokedDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.SoftwareLicenseAllocationId).HasName("PK_tblSoftwareLicenseAllocation");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.SoftwareLicenseAllocationId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.SoftwareLicenseId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetId).HasColumnType("int");
        builder.Property(e => e.EmployeeId).HasColumnType("int");
        builder.Property(e => e.AllocatedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.RevokedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.IsCurrent).HasColumnType("bit").IsRequired();
        builder.Property(e => e.Notes).HasColumnType("nvarchar(500)").HasMaxLength(500);

        builder.HasOne(e => e.SoftwareLicense).WithMany(p => p.Allocations)
            .HasForeignKey(e => e.SoftwareLicenseId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicenseAllocation_SoftwareLicenseId");
        builder.HasOne(e => e.Asset).WithMany(p => p.LicenseAllocations)
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicenseAllocation_AssetId");
        builder.HasOne(e => e.Employee).WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblSoftwareLicenseAllocation_EmployeeId");

        builder.HasIndex(e => new { e.SoftwareLicenseId, e.IsCurrent }, "IX_tblSoftwareLicenseAllocation_SoftwareLicenseId_IsCurrent").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.SoftwareLicenseId, "IX_tblSoftwareLicenseAllocation_SoftwareLicenseId").HasFilter(null);
        builder.HasIndex(e => e.AssetId, "IX_tblSoftwareLicenseAllocation_AssetId").HasFilter(null);
        builder.HasIndex(e => e.EmployeeId, "IX_tblSoftwareLicenseAllocation_EmployeeId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblSoftwareLicenseAllocation_CompanyId").HasFilter(null);
    }
}
