using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Permission"/> to dbo.tblPermission. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("tblPermission", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.PermissionId).HasName("PK_tblPermission");

        builder.Property(e => e.PermissionId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.PermissionCode).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModuleName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.PermissionName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();

        builder.HasIndex(e => e.PermissionCode, "UX_tblPermission_PermissionCode").IsUnique().HasFilter(null);
    }
}
