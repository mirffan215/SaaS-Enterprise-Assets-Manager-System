using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetDisposal"/> to dbo.tblAssetDisposal. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetDisposalConfiguration : IEntityTypeConfiguration<AssetDisposal>
{
    public void Configure(EntityTypeBuilder<AssetDisposal> builder)
    {
        builder.ToTable("tblAssetDisposal", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblAssetDisposal_Evidence");
            tb.HasCheckConstraint("CK_tblAssetDisposal_DisposalStatus_Domain", "[DisposalStatus] IN (N'Requested', N'Approved', N'Rejected', N'Completed', N'Cancelled')");
            tb.HasCheckConstraint("CK_tblAssetDisposal_SaleValue", "[SaleValue] IS NULL OR [SaleValue] >= 0");
            tb.HasCheckConstraint("CK_tblAssetDisposal_Rule01", "[DisposalStatus] NOT IN (N'Approved', N'Completed') OR ([ApprovedBy] IS NOT NULL AND [ApprovedDate] IS NOT NULL)");
            tb.HasCheckConstraint("CK_tblAssetDisposal_Rule02", "[DisposalStatus] <> N'Completed' OR [DisposalDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetDisposal_Rule03", "[DisposalStatus] <> N'Rejected' OR [RejectionReason] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblAssetDisposal_Rule04", "[ApprovedBy] IS NULL OR [ApprovedBy] <> [RequestedBy]");
            tb.HasCheckConstraint("CK_tblAssetDisposal_Rule05", "[DataSanitizationVerifiedBy] IS NULL OR [DataSanitizationDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetDisposalId).HasName("PK_tblAssetDisposal");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DocumentNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.ErpReference).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.AssetDisposalId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DisposalMethodId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DisposalStatus).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Reason).HasColumnType("nvarchar(1000)").HasMaxLength(1000).IsRequired();
        builder.Property(e => e.RequestedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RequestedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ApprovedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ApprovedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.RejectionReason).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.DataSanitizationMethod).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.DataSanitizationDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.DataSanitizationVerifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.CertificateNumber).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DisposalDate).HasColumnType("date");
        builder.Property(e => e.RecipientName).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.SaleValue).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurrencyId).HasColumnType("int");
        builder.Property(e => e.Remarks).HasColumnType("nvarchar(1000)").HasMaxLength(1000);

        builder.HasOne(e => e.Asset).WithMany(p => p.Disposals)
            .HasForeignKey(e => e.AssetId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetDisposal_AssetId");
        builder.HasOne(e => e.DisposalMethod).WithMany()
            .HasForeignKey(e => e.DisposalMethodId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetDisposal_DisposalMethodId");
        builder.HasOne(e => e.Currency).WithMany()
            .HasForeignKey(e => e.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetDisposal_CurrencyId");

        builder.HasIndex(e => e.AssetId, "UX_tblAssetDisposal_AssetId").IsUnique().HasFilter("[DisposalStatus] IN (N'Requested', N'Approved', N'Completed') AND [IsDeleted] = 0");
        builder.HasIndex(e => e.AssetId, "IX_tblAssetDisposal_AssetId").HasFilter(null);
        builder.HasIndex(e => e.DisposalMethodId, "IX_tblAssetDisposal_DisposalMethodId").HasFilter(null);
        builder.HasIndex(e => e.CurrencyId, "IX_tblAssetDisposal_CurrencyId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetDisposal_CompanyId").HasFilter(null);
    }
}
