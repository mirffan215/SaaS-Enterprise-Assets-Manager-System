using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetCategory"/> to dbo.tblAssetCategory. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetCategoryConfiguration : IEntityTypeConfiguration<AssetCategory>
{
    public void Configure(EntityTypeBuilder<AssetCategory> builder)
    {
        builder.ToTable("tblAssetCategory", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblAssetCategory_DefaultUsefulLifeMonths", "[DefaultUsefulLifeMonths] IS NULL OR [DefaultUsefulLifeMonths] > 0");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetCategoryId).HasName("PK_tblAssetCategory");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetCategoryId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.CategoryCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CategoryName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.AssetNumberSequenceId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DefaultUsefulLifeMonths).HasColumnType("int");
        builder.Property(e => e.RequiresHostname).HasColumnType("bit").IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.AssetNumberSequence).WithMany(p => p.AssetCategories)
            .HasForeignKey(e => e.AssetNumberSequenceId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetCategory_AssetNumberSequenceId");

        builder.HasIndex(e => new { e.CompanyId, e.CategoryCode }, "UX_tblAssetCategory_CategoryCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.CategoryName }, "UX_tblAssetCategory_CategoryName").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.AssetNumberSequenceId, "IX_tblAssetCategory_AssetNumberSequenceId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetCategory_CompanyId").HasFilter(null);
    }
}
