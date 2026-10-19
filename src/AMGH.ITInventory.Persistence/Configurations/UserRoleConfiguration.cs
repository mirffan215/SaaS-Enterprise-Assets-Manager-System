using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="UserRole"/> to dbo.tblUserRole. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("tblUserRole", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblUserRole_KeepAdmin");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => new { e.UserId, e.RoleId }).HasName("PK_tblUserRole");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.UserId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RoleId).HasColumnType("int").IsRequired();

        builder.HasOne(e => e.User).WithMany(p => p.UserRoles)
            .HasForeignKey(e => e.UserId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblUserRole_UserId");
        builder.HasOne(e => e.Role).WithMany(p => p.UserRoles)
            .HasForeignKey(e => e.RoleId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblUserRole_RoleId");

        builder.HasIndex(e => e.RoleId, "IX_tblUserRole_RoleId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblUserRole_CompanyId").HasFilter(null);
    }
}
