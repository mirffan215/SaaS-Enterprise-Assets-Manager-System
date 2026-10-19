using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetSubCategory"/> to dbo.tblAssetSubCategory. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetSubCategoryConfiguration : IEntityTypeConfiguration<AssetSubCategory>
{
    public void Configure(EntityTypeBuilder<AssetSubCategory> builder)
    {
        builder.ToTable("tblAssetSubCategory", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetSubCategoryId).HasName("PK_tblAssetSubCategory");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetSubCategoryId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.AssetCategoryId).HasColumnType("int").IsRequired();
        builder.Property(e => e.SubCategoryCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.SubCategoryName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.AssetCategory).WithMany(p => p.SubCategories)
            .HasForeignKey(e => e.AssetCategoryId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblAssetSubCategory_AssetCategoryId");

        builder.HasIndex(e => new { e.CompanyId, e.AssetCategoryId, e.SubCategoryCode }, "UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.AssetCategoryId, "IX_tblAssetSubCategory_AssetCategoryId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetSubCategory_CompanyId").HasFilter(null);
    }
}
