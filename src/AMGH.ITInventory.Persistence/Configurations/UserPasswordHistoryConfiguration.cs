using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="UserPasswordHistory"/> to dbo.tblUserPasswordHistory. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class UserPasswordHistoryConfiguration : IEntityTypeConfiguration<UserPasswordHistory>
{
    public void Configure(EntityTypeBuilder<UserPasswordHistory> builder)
    {
        builder.ToTable("tblUserPasswordHistory", "dbo", tb =>
        {
        });

        builder.HasKey(e => e.UserPasswordHistoryId).HasName("PK_tblUserPasswordHistory");

        builder.Property(e => e.UserPasswordHistoryId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.UserId).HasColumnType("int").IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(e => e.User).WithMany(p => p.PasswordHistory)
            .HasForeignKey(e => e.UserId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblUserPasswordHistory_UserId");

        builder.HasIndex(e => new { e.UserId, e.CreatedDate }, "IX_tblUserPasswordHistory_UserId_CreatedDate").HasFilter(null);
    }
}
