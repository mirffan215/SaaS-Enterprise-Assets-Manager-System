using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="NotificationQueueItem"/> to dbo.tblNotificationQueue. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class NotificationQueueItemConfiguration : IEntityTypeConfiguration<NotificationQueueItem>
{
    public void Configure(EntityTypeBuilder<NotificationQueueItem> builder)
    {
        builder.ToTable("tblNotificationQueue", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblNotificationQueue_Channel_Domain", "[Channel] IN (N'Email', N'InApp')");
            tb.HasCheckConstraint("CK_tblNotificationQueue_QueueStatus_Domain", "[QueueStatus] IN (N'Pending', N'Processing', N'Sent', N'Failed', N'Cancelled')");
            tb.HasCheckConstraint("CK_tblNotificationQueue_AttemptCount", "[AttemptCount] >= 0");
            tb.HasCheckConstraint("CK_tblNotificationQueue_MaxAttempts", "[MaxAttempts] BETWEEN 1 AND 20");
            tb.HasCheckConstraint("CK_tblNotificationQueue_Rule01", "[QueueStatus] <> N'Sent' OR [SentDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.NotificationQueueId).HasName("PK_tblNotificationQueue");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.NotificationQueueId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.Channel).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RecipientAddress).HasColumnType("nvarchar(256)").HasMaxLength(256).IsRequired();
        builder.Property(e => e.RecipientUserId).HasColumnType("int");
        builder.Property(e => e.Subject).HasColumnType("nvarchar(300)").HasMaxLength(300).IsRequired();
        builder.Property(e => e.Body).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.IsHtml).HasColumnType("bit").IsRequired();
        builder.Property(e => e.QueueStatus).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.AttemptCount).HasColumnType("int").IsRequired();
        builder.Property(e => e.MaxAttempts).HasColumnType("int").IsRequired();
        builder.Property(e => e.NextAttemptDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.LastError).HasColumnType("nvarchar(2000)").HasMaxLength(2000);
        builder.Property(e => e.SentDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.DeduplicationKey).HasColumnType("nvarchar(200)").HasMaxLength(200);

        builder.HasOne(e => e.RecipientUser).WithMany()
            .HasForeignKey(e => e.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblNotificationQueue_RecipientUserId");

        builder.HasIndex(e => new { e.CompanyId, e.DeduplicationKey }, "UX_tblNotificationQueue_DeduplicationKey").IsUnique().HasFilter("[DeduplicationKey] IS NOT NULL");
        builder.HasIndex(e => new { e.QueueStatus, e.NextAttemptDate }, "IX_tblNotificationQueue_QueueStatus_NextAttemptDate").HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.RecipientUserId, "IX_tblNotificationQueue_RecipientUserId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblNotificationQueue_CompanyId").HasFilter(null);
    }
}
