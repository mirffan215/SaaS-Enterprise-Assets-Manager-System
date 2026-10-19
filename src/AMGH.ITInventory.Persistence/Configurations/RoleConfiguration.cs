using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Role"/> to dbo.tblRole. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("tblRole", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblRole_ProtectSystemRoles");
            tb.HasTrigger("trg_tblRole_NoHardDelete");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.RoleId).HasName("PK_tblRole");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RoleId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.RoleName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.IsSystemRole).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.RoleName }, "UX_tblRole_RoleName").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblRole_CompanyId").HasFilter(null);
    }
}
