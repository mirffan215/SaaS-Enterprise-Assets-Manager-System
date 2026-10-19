using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetAudit"/> to dbo.tblAssetAudit. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetAuditConfiguration : IEntityTypeConfiguration<AssetAudit>
{
    public void Configure(EntityTypeBuilder<AssetAudit> builder)
    {
        builder.ToTable("tblAssetAudit", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetAudit_AuditResult_Domain", "[AuditResult] IN (N'Verified', N'Missing', N'Damaged', N'LocationMismatch', N'CustodianMismatch', N'LabelMissing')");
            tb.HasCheckConstraint("CK_tblAssetAudit_ScanMethod_Domain", "[ScanMethod] IN (N'QrScan', N'Manual')");
            tb.HasCheckConstraint("CK_tblAssetAudit_Rule01", "[IsResolved] = 0 OR [ResolvedDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetAudit_Rule02", "[AuditResult] = N'Verified' OR [Remarks] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetAuditId).HasName("PK_tblAssetAudit");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetAuditId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AuditCycle).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.AuditDate).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.AuditedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.AuditResult).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.ScanMethod).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.FoundLocationId).HasColumnType("int");
        builder.Property(e => e.FoundRoomId).HasColumnType("int");
        builder.Property(e => e.FoundEmployeeId).HasColumnType("int");
        builder.Property(e => e.Remarks).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.CorrectiveAction).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.IsResolved).HasColumnType("bit").IsRequired();
        builder.Property(e => e.ResolvedDate).HasColumnType("datetime2(3)");

        builder.HasOne(e => e.Asset).WithMany(p => p.Audits)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAudit_AssetId");
        builder.HasOne(e => e.FoundLocation).WithMany()
            .HasForeignKey(e => e.FoundLocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAudit_FoundLocationId");
        builder.HasOne(e => e.FoundRoom).WithMany()
            .HasForeignKey(e => e.FoundRoomId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAudit_FoundRoomId");
        builder.HasOne(e => e.FoundEmployee).WithMany()
            .HasForeignKey(e => e.FoundEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetAudit_FoundEmployeeId");

        builder.HasIndex(e => new { e.AssetId, e.AuditCycle }, "UX_tblAssetAudit_AssetId_AuditCycle").IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblAssetAudit_AssetId").HasFilter(null);
        builder.HasIndex(e => e.FoundLocationId, "IX_tblAssetAudit_FoundLocationId").HasFilter(null);
        builder.HasIndex(e => e.FoundRoomId, "IX_tblAssetAudit_FoundRoomId").HasFilter(null);
        builder.HasIndex(e => e.FoundEmployeeId, "IX_tblAssetAudit_FoundEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetAudit_CompanyId").HasFilter(null);
    }
}
