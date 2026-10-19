using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="RolePermission"/> to dbo.tblRolePermission. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("tblRolePermission", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => new { e.RoleId, e.PermissionId }).HasName("PK_tblRolePermission");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RoleId).HasColumnType("int").IsRequired();
        builder.Property(e => e.PermissionId).HasColumnType("int").IsRequired();

        builder.HasOne(e => e.Role).WithMany(p => p.RolePermissions)
            .HasForeignKey(e => e.RoleId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblRolePermission_RoleId");
        builder.HasOne(e => e.Permission).WithMany(p => p.RolePermissions)
            .HasForeignKey(e => e.PermissionId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblRolePermission_PermissionId");

        builder.HasIndex(e => e.PermissionId, "IX_tblRolePermission_PermissionId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblRolePermission_CompanyId").HasFilter(null);
    }
}
