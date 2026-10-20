using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to dbo.tblUser. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("tblUser", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblUser_KeepAdmin");
            tb.HasCheckConstraint("CK_tblUser_Email", "[Email] LIKE N'%_@_%._%'");
            tb.HasCheckConstraint("CK_tblUser_AccessFailedCount", "[AccessFailedCount] >= 0");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.UserId).HasName("PK_tblUser");

        builder.Property(e => e.UserId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.Username).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Email).HasColumnType("nvarchar(256)").HasMaxLength(256).IsRequired();
        builder.Property(e => e.DisplayName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnType("int");
        builder.Property(e => e.PasswordHash).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(e => e.SecurityStamp).HasColumnType("uniqueidentifier").IsRequired();
        builder.Property(e => e.IsPlatformAdmin).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();
        builder.Property(e => e.AccessFailedCount).HasColumnType("int").IsRequired();
        builder.Property(e => e.LockoutEndUtc).HasColumnType("datetime2(3)");
        builder.Property(e => e.MustChangePassword).HasColumnType("bit").IsRequired();
        builder.Property(e => e.PasswordChangedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.LastLoginDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.LastLoginIp).HasColumnType("nvarchar(45)").HasMaxLength(45);
        builder.Property(e => e.TotpSecret).HasColumnType("nvarchar(400)").HasMaxLength(400);
        builder.Property(e => e.TotpEnabledDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.TotpLastStep).HasColumnType("bigint");
        builder.Ignore(e => e.HasTwoStep);

        builder.HasOne(e => e.Employee).WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblUser_EmployeeId");

        builder.HasIndex(e => e.Username, "UX_tblUser_Username").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.Email, "UX_tblUser_Email").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.EmployeeId, "UX_tblUser_EmployeeId").IsUnique().HasFilter("[EmployeeId] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(e => e.EmployeeId, "IX_tblUser_EmployeeId").HasFilter(null);
    }
}
