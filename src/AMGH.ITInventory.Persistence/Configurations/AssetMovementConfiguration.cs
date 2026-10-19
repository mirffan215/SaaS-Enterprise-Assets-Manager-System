using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetMovement"/> to dbo.tblAssetMovement. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetMovementConfiguration : IEntityTypeConfiguration<AssetMovement>
{
    public void Configure(EntityTypeBuilder<AssetMovement> builder)
    {
        builder.ToTable("tblAssetMovement", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetMovement_MovementType_Domain", "[MovementType] IN (N'Relocation', N'Transfer', N'Repair', N'Return', N'Storage')");
            tb.HasCheckConstraint("CK_tblAssetMovement_MovementStatus_Domain", "[MovementStatus] IN (N'Requested', N'Approved', N'Rejected', N'Completed', N'Cancelled')");
            tb.HasCheckConstraint("CK_tblAssetMovement_Rule01", "[MovementStatus] NOT IN (N'Approved', N'Completed') OR ([ApprovedBy] IS NOT NULL AND [ApprovedDate] IS NOT NULL)");
            tb.HasCheckConstraint("CK_tblAssetMovement_Rule02", "[MovementStatus] <> N'Completed' OR [CompletedDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetMovement_Rule03", "[MovementStatus] <> N'Rejected' OR [RejectionReason] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetMovement_Rule04", "[ApprovedBy] IS NULL OR [ApprovedBy] <> [RequestedBy]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetMovementId).HasName("PK_tblAssetMovement");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetMovementId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.FromLocationId).HasColumnType("int");
        builder.Property(e => e.FromBuildingId).HasColumnType("int");
        builder.Property(e => e.FromFloorId).HasColumnType("int");
        builder.Property(e => e.FromRoomId).HasColumnType("int");
        builder.Property(e => e.ToLocationId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ToBuildingId).HasColumnType("int");
        builder.Property(e => e.ToFloorId).HasColumnType("int");
        builder.Property(e => e.ToRoomId).HasColumnType("int");
        builder.Property(e => e.MovementType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.MovementStatus).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.MovementReason).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(e => e.ChangeRequestNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.RequestedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RequestedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ApprovedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ApprovedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.CompletedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.RejectionReason).HasColumnType("nvarchar(500)").HasMaxLength(500);

        builder.HasOne(e => e.Asset).WithMany(p => p.Movements)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_AssetId");
        builder.HasOne(e => e.FromLocation).WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_FromLocationId");
        builder.HasOne(e => e.FromBuilding).WithMany()
            .HasForeignKey(e => e.FromBuildingId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_FromBuildingId");
        builder.HasOne(e => e.FromFloor).WithMany()
            .HasForeignKey(e => e.FromFloorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_FromFloorId");
        builder.HasOne(e => e.FromRoom).WithMany()
            .HasForeignKey(e => e.FromRoomId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_FromRoomId");
        builder.HasOne(e => e.ToLocation).WithMany()
            .HasForeignKey(e => e.ToLocationId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_ToLocationId");
        builder.HasOne(e => e.ToBuilding).WithMany()
            .HasForeignKey(e => e.ToBuildingId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_ToBuildingId");
        builder.HasOne(e => e.ToFloor).WithMany()
            .HasForeignKey(e => e.ToFloorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_ToFloorId");
        builder.HasOne(e => e.ToRoom).WithMany()
            .HasForeignKey(e => e.ToRoomId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMovement_ToRoomId");

        builder.HasIndex(e => new { e.MovementStatus, e.RequestedDate }, "IX_tblAssetMovement_MovementStatus_RequestedDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblAssetMovement_AssetId").HasFilter(null);
        builder.HasIndex(e => e.FromLocationId, "IX_tblAssetMovement_FromLocationId").HasFilter(null);
        builder.HasIndex(e => e.FromBuildingId, "IX_tblAssetMovement_FromBuildingId").HasFilter(null);
        builder.HasIndex(e => e.FromFloorId, "IX_tblAssetMovement_FromFloorId").HasFilter(null);
        builder.HasIndex(e => e.FromRoomId, "IX_tblAssetMovement_FromRoomId").HasFilter(null);
        builder.HasIndex(e => e.ToLocationId, "IX_tblAssetMovement_ToLocationId").HasFilter(null);
        builder.HasIndex(e => e.ToBuildingId, "IX_tblAssetMovement_ToBuildingId").HasFilter(null);
        builder.HasIndex(e => e.ToFloorId, "IX_tblAssetMovement_ToFloorId").HasFilter(null);
        builder.HasIndex(e => e.ToRoomId, "IX_tblAssetMovement_ToRoomId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetMovement_CompanyId").HasFilter(null);
    }
}
