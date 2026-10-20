using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetMaintenance"/> to dbo.tblAssetMaintenance. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetMaintenanceConfiguration : IEntityTypeConfiguration<AssetMaintenance>
{
    public void Configure(EntityTypeBuilder<AssetMaintenance> builder)
    {
        builder.ToTable("tblAssetMaintenance", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetMaintenance_MaintenanceType_Domain", "[MaintenanceType] IN (N'Preventive', N'Corrective', N'Upgrade', N'Inspection', N'Calibration')");
            tb.HasCheckConstraint("CK_tblAssetMaintenance_MaintenanceStatus_Domain", "[MaintenanceStatus] IN (N'Scheduled', N'InProgress', N'Completed', N'Cancelled')");
            tb.HasCheckConstraint("CK_tblAssetMaintenance_Cost", "[Cost] IS NULL OR [Cost] >= 0");
            tb.HasCheckConstraint("CK_tblAssetMaintenance_DowntimeHours", "[DowntimeHours] IS NULL OR [DowntimeHours] >= 0");
            tb.HasCheckConstraint("CK_tblAssetMaintenance_Rule01", "[MaintenanceStatus] <> N'Completed' OR ([CompletedDate] IS NOT NULL AND [ResolutionDetails] IS NOT NULL)");
            tb.HasCheckConstraint("CK_tblAssetMaintenance_Rule02", "[CompletedDate] IS NULL OR [StartDate] IS NULL OR [CompletedDate] >= [StartDate]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetMaintenanceId).HasName("PK_tblAssetMaintenance");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetMaintenanceId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.MaintenanceType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.MaintenanceStatus).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.ScheduledDate).HasColumnType("date").IsRequired();
        builder.Property(e => e.StartDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.CompletedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.VendorId).HasColumnType("int");
        builder.Property(e => e.TicketNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.ProblemDescription).HasColumnType("nvarchar(1000)").HasMaxLength(1000).IsRequired();
        builder.Property(e => e.ResolutionDetails).HasColumnType("nvarchar(2000)").HasMaxLength(2000);
        builder.Property(e => e.PerformedBy).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.AssignedToUserId).HasColumnType("int");
        builder.Property(e => e.Cost).HasColumnType("decimal(18,2)");
        builder.Property(e => e.InvoiceNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.CurrencyId).HasColumnType("int");
        builder.Property(e => e.DowntimeHours).HasColumnType("decimal(9,2)");
        builder.Property(e => e.NextMaintenanceDate).HasColumnType("date");

        builder.HasOne(e => e.Asset).WithMany(p => p.MaintenanceRecords)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMaintenance_AssetId");
        builder.HasOne(e => e.Vendor).WithMany()
            .HasForeignKey(e => e.VendorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMaintenance_VendorId");
        builder.HasOne(e => e.AssignedToUser).WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMaintenance_AssignedToUserId");
        builder.HasOne(e => e.Currency).WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetMaintenance_CurrencyId");

        builder.HasIndex(e => new { e.CompanyId, e.AssignedToUserId, e.MaintenanceStatus }, "IX_tblAssetMaintenance_AssignedToUserId").HasFilter("[AssignedToUserId] IS NOT NULL");
        builder.HasIndex(e => new { e.MaintenanceStatus, e.ScheduledDate }, "IX_tblAssetMaintenance_MaintenanceStatus_ScheduledDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblAssetMaintenance_AssetId").HasFilter(null);
        builder.HasIndex(e => e.VendorId, "IX_tblAssetMaintenance_VendorId").HasFilter(null);
        builder.HasIndex(e => e.CurrencyId, "IX_tblAssetMaintenance_CurrencyId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetMaintenance_CompanyId").HasFilter(null);
    }
}
