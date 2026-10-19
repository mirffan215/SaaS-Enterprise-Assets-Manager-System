using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetDocument"/> to dbo.tblAssetDocument. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetDocumentConfiguration : IEntityTypeConfiguration<AssetDocument>
{
    public void Configure(EntityTypeBuilder<AssetDocument> builder)
    {
        builder.ToTable("tblAssetDocument", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetDocument_DocumentType_Domain", "[DocumentType] IN (N'Invoice', N'PurchaseOrder', N'DeliveryNote', N'WarrantyCertificate', N'HandoverForm', N'MaintenanceReport', N'DisposalCertificate', N'DataSanitizationCertificate', N'Manual', N'Photo', N'Other')");
            tb.HasCheckConstraint("CK_tblAssetDocument_FileSizeBytes", "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 26214400");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetDocumentId).HasName("PK_tblAssetDocument");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetDocumentId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.DocumentTitle).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.OriginalFileName).HasColumnType("nvarchar(260)").HasMaxLength(260).IsRequired();
        builder.Property(e => e.StoredFileName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ContentType).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.FileSizeBytes).HasColumnType("bigint").IsRequired();
        builder.Property(e => e.Sha256Hash).HasColumnType("char(64)").HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.ClassificationId).HasColumnType("int");
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);

        builder.HasOne(e => e.Asset).WithMany(p => p.Documents)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetDocument_AssetId");
        builder.HasOne(e => e.Classification).WithMany()
            .HasForeignKey(e => e.ClassificationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetDocument_ClassificationId");

        builder.HasIndex(e => e.StoredFileName, "UX_tblAssetDocument_StoredFileName").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.AssetId, "IX_tblAssetDocument_AssetId").HasFilter(null);
        builder.HasIndex(e => e.ClassificationId, "IX_tblAssetDocument_ClassificationId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetDocument_CompanyId").HasFilter(null);
    }
}
