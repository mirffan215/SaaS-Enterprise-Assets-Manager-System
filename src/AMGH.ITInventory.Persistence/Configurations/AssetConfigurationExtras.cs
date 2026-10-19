using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="RoleCategory"/> to dbo.tblRoleCategory (script 16).</summary>
internal sealed class RoleCategoryConfiguration : IEntityTypeConfiguration<RoleCategory>
{
    public void Configure(EntityTypeBuilder<RoleCategory> b)
    {
        b.ToTable("tblRoleCategory", "dbo");
        b.HasKey(e => new { e.RoleId, e.AssetCategoryId }).HasName("PK_tblRoleCategory");
        b.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        b.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").IsRequired();
    }
}
