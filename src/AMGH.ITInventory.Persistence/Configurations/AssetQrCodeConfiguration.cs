using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetQrCode"/> to dbo.tblAssetQRCode. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetQrCodeConfiguration : IEntityTypeConfiguration<AssetQrCode>
{
    public void Configure(EntityTypeBuilder<AssetQrCode> builder)
    {
        builder.ToTable("tblAssetQRCode", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetQRCode_QrVersion", "[QrVersion] > 0");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetQRCodeId).HasName("PK_tblAssetQRCode");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetQRCodeId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.QrVersion).HasColumnType("int").IsRequired();
        builder.Property(e => e.Payload).HasColumnType("nvarchar(2000)").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.PayloadHash).HasColumnType("char(64)").HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.QrImagePng).HasColumnType("varbinary(max)").IsRequired();
        builder.Property(e => e.IsCurrent).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.Asset).WithMany(p => p.QrCodes)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetQRCode_AssetId");

        builder.HasIndex(e => new { e.AssetId, e.QrVersion }, "UX_tblAssetQRCode_AssetId_QrVersion").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.AssetId, "UX_tblAssetQRCode_AssetId").IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetQRCode_CompanyId").HasFilter(null);
    }
}
