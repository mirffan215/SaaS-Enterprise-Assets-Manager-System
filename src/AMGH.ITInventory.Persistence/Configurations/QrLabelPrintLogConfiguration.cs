using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="QrLabelPrintLog"/> to dbo.tblQrLabelPrintLog. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class QrLabelPrintLogConfiguration : IEntityTypeConfiguration<QrLabelPrintLog>
{
    public void Configure(EntityTypeBuilder<QrLabelPrintLog> builder)
    {
        builder.ToTable("tblQrLabelPrintLog", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblQrLabelPrintLog_Immutable");
            tb.HasCheckConstraint("CK_tblQrLabelPrintLog_LabelSize_Domain", "[LabelSize] IN (N'Size50x30', N'Size70x40', N'Size100x50')");
            tb.HasCheckConstraint("CK_tblQrLabelPrintLog_LabelDesign_Domain", "[LabelDesign] IN (N'QrLabel', N'AssetTag', N'RfidLabel')");
            tb.HasCheckConstraint("CK_tblQrLabelPrintLog_Copies", "[Copies] BETWEEN 1 AND 100");
            tb.HasCheckConstraint("CK_tblQrLabelPrintLog_Rule01", "[IsReprint] = 0 OR [ReprintReason] IS NOT NULL");
        });

        builder.HasKey(e => e.QrLabelPrintLogId).HasName("PK_tblQrLabelPrintLog");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.QrLabelPrintLogId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetQRCodeId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.LabelSize).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.LabelDesign).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Copies).HasColumnType("smallint").IsRequired();
        builder.Property(e => e.PrintBatchId).HasColumnType("uniqueidentifier").IsRequired();
        builder.Property(e => e.IsReprint).HasColumnType("bit").IsRequired();
        builder.Property(e => e.ReprintReason).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.PrintedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PrintedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(e => e.AssetQrCode).WithMany(p => p.PrintLogs)
            .HasForeignKey(e => e.AssetQRCodeId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblQrLabelPrintLog_AssetQRCodeId");
        builder.HasOne(e => e.Asset).WithMany()
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblQrLabelPrintLog_AssetId");

        builder.HasIndex(e => e.PrintBatchId, "IX_tblQrLabelPrintLog_PrintBatchId").HasFilter(null);
        builder.HasIndex(e => e.PrintedDate, "IX_tblQrLabelPrintLog_PrintedDate").HasFilter(null);
        builder.HasIndex(e => e.AssetQRCodeId, "IX_tblQrLabelPrintLog_AssetQRCodeId").HasFilter(null);
        builder.HasIndex(e => e.AssetId, "IX_tblQrLabelPrintLog_AssetId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblQrLabelPrintLog_CompanyId").HasFilter(null);
    }
}
