using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetAssignment"/> to dbo.tblAssetAssignment. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> builder)
    {
        builder.ToTable("tblAssetAssignment", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetAssignment_AssignmentType_Domain", "[AssignmentType] IN (N'Permanent', N'Temporary', N'Loan')");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule01", "[EmployeeId] IS NOT NULL OR [DepartmentId] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule02", "[ReturnedDate] IS NULL OR [ReturnedDate] >= [AssignedDate]");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule03", "[IsCurrent] = 0 OR [ReturnedDate] IS NULL");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule04", "[IsCurrent] = 1 OR [ReturnedDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule05", "[AcknowledgementReceived] = 0 OR [AcknowledgementDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetAssignment_Rule06", "[AssignmentType] = N'Permanent' OR [ExpectedReturnDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetAssignmentId).HasName("PK_tblAssetAssignment");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetAssignmentId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnType("int");
        builder.Property(e => e.DepartmentId).HasColumnType("int");
        builder.Property(e => e.LocationId).HasColumnType("int");
        builder.Property(e => e.AssignmentType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.AssignedDate).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.ExpectedReturnDate).HasColumnType("date");
        builder.Property(e => e.ReturnedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.ConditionAtAssignment).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.ConditionAtReturn).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.AcknowledgementReceived).HasColumnType("bit").IsRequired();
        builder.Property(e => e.AcknowledgementDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.IsCurrent).HasColumnType("bit").IsRequired();
        builder.Property(e => e.AssignedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ReturnedTo).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.Notes).HasColumnType("nvarchar(1000)").HasMaxLength(1000);

        builder.HasOne(e => e.Asset).WithMany(p => p.Assignments)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAssignment_AssetId");
        builder.HasOne(e => e.Employee).WithMany(p => p.Assignments)
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAssignment_EmployeeId");
        builder.HasOne(e => e.Department).WithMany()
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAssignment_DepartmentId");
        builder.HasOne(e => e.Location).WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAssignment_LocationId");

        builder.HasIndex(e => e.AssetId, "UX_tblAssetAssignment_AssetId").IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblAssetAssignment_AssetId").HasFilter(null);
        builder.HasIndex(e => e.EmployeeId, "IX_tblAssetAssignment_EmployeeId").HasFilter(null);
        builder.HasIndex(e => e.DepartmentId, "IX_tblAssetAssignment_DepartmentId").HasFilter(null);
        builder.HasIndex(e => e.LocationId, "IX_tblAssetAssignment_LocationId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetAssignment_CompanyId").HasFilter(null);
    }
}
