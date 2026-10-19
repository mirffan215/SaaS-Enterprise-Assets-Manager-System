using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Notification"/> (in-app notifications) to dbo.tblNotification. GENERATED - matches deployment/SQLScripts/02_Tables.sql
/// (CompanyId and its index added by Release 2.0, script 10). The e-mail settings, template, rule and preference mappings
/// are in NotificationSettingsConfiguration.cs.</summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("tblNotification", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblNotification_NotificationType_Domain", "[NotificationType] IN (N'WarrantyExpiry', N'LicenseExpiry', N'MaintenanceDue', N'AuditDue', N'ApprovalRequired', N'AssignmentAcknowledgement', N'System')");
            tb.HasCheckConstraint("CK_tblNotification_Severity_Domain", "[Severity] IN (N'Info', N'Warning', N'Critical')");
            tb.HasCheckConstraint("CK_tblNotification_Rule01", "[IsRead] = 0 OR [ReadDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.NotificationId).HasName("PK_tblNotification");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.NotificationId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.UserId).HasColumnType("int").IsRequired();
        builder.Property(e => e.Title).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Message).HasColumnType("nvarchar(2000)").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.NotificationType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Severity).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RelatedEntityName).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.RelatedEntityId).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.ActionUrl).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.IsRead).HasColumnType("bit").IsRequired();
        builder.Property(e => e.ReadDate).HasColumnType("datetime2(3)");

        builder.HasOne(e => e.User).WithMany(p => p.Notifications)
            .HasForeignKey(e => e.UserId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblNotification_UserId");

        builder.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedDate }, "IX_tblNotification_UserId_IsRead_CreatedDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.UserId, "IX_tblNotification_UserId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblNotification_CompanyId").HasFilter(null);
    }
}
