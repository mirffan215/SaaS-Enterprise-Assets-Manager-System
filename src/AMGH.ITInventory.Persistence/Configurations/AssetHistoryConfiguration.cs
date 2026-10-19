using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetHistory"/> to dbo.tblAssetHistory. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetHistoryConfiguration : IEntityTypeConfiguration<AssetHistory>
{
    public void Configure(EntityTypeBuilder<AssetHistory> builder)
    {
        builder.ToTable("tblAssetHistory", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblAssetHistory_Immutable");
            tb.HasCheckConstraint("CK_tblAssetHistory_EventType_Domain", "[EventType] IN (N'Created', N'Updated', N'StatusChanged', N'Assigned', N'Returned', N'Moved', N'MaintenanceLogged', N'Audited', N'QrGenerated', N'LabelPrinted', N'DocumentAttached', N'DisposalRequested', N'Disposed', N'Deleted', N'Restored', N'LicenseAllocated', N'LicenseRevoked')");
        });

        builder.HasKey(e => e.AssetHistoryId).HasName("PK_tblAssetHistory");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetHistoryId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.EventType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.FieldName).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.OldValue).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.NewValue).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.Remarks).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.PerformedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EventDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(e => e.Asset).WithMany(p => p.History)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetHistory_AssetId");

        builder.HasIndex(e => new { e.AssetId, e.EventDate }, "IX_tblAssetHistory_AssetId_EventDate").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetHistory_CompanyId").HasFilter(null);
    }
}
