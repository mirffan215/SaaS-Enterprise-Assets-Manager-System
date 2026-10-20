using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="UserRecoveryCode"/> to dbo.tblUserRecoveryCode (migration 0001_Security.sql).</summary>
internal sealed class UserRecoveryCodeConfiguration : IEntityTypeConfiguration<UserRecoveryCode>
{
    public void Configure(EntityTypeBuilder<UserRecoveryCode> builder)
    {
        builder.ToTable("tblUserRecoveryCode", "dbo");
        builder.HasKey(e => e.UserRecoveryCodeId).HasName("PK_tblUserRecoveryCode");
        builder.Property(e => e.UserRecoveryCodeId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.UserId).HasColumnType("int").IsRequired();
        builder.Property(e => e.CodeHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.UsedDate).HasColumnType("datetime2(3)");
        builder.HasOne(e => e.User).WithMany(p => p.RecoveryCodes)
            .HasForeignKey(e => e.UserId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblUserRecoveryCode_UserId");
        builder.HasIndex(e => e.UserId, "IX_tblUserRecoveryCode_UserId").HasFilter(null);
    }
}
