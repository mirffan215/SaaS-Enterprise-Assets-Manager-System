using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="LoginAudit"/> to dbo.tblLoginAudit. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public void Configure(EntityTypeBuilder<LoginAudit> builder)
    {
        builder.ToTable("tblLoginAudit", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblLoginAudit_Immutable");
            tb.HasCheckConstraint("CK_tblLoginAudit_EventType_Domain", "[EventType] IN (N'LoginSuccess', N'LoginFailed', N'Logout', N'Lockout', N'PasswordChanged', N'PasswordReset', N'SessionExpired', N'AccountDisabled', N'MfaEnabled', N'MfaReset', N'RecoveryCodeUsed', N'SsoSignIn')");
        });

        builder.HasKey(e => e.LoginAuditId).HasName("PK_tblLoginAudit");

        builder.Property(e => e.CompanyId).HasColumnType("int");
        builder.Property(e => e.LoginAuditId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.UserId).HasColumnType("int");
        builder.Property(e => e.UsernameAttempted).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EventType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.IpAddress).HasColumnType("nvarchar(45)").HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.FailureReason).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.EventDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(e => e.User).WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblLoginAudit_UserId");

        builder.HasIndex(e => e.EventDate, "IX_tblLoginAudit_EventDate").HasFilter(null);
        builder.HasIndex(e => new { e.UsernameAttempted, e.EventDate }, "IX_tblLoginAudit_UsernameAttempted_EventDate").HasFilter(null);
        builder.HasIndex(e => e.UserId, "IX_tblLoginAudit_UserId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblLoginAudit_CompanyId").HasFilter(null);
    }
}
