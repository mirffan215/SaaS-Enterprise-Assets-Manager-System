using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AuditLog"/> to dbo.tblAuditLog. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("tblAuditLog", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblAuditLog_Immutable");
            tb.HasCheckConstraint("CK_tblAuditLog_Action_Domain", "[Action] IN (N'Create', N'Update', N'Delete', N'Restore', N'View', N'Export', N'Import', N'Print', N'Approve', N'Reject')");
        });

        builder.HasKey(e => e.AuditLogId).HasName("PK_tblAuditLog");

        builder.Property(e => e.CompanyId).HasColumnType("int");
        builder.Property(e => e.AuditLogId).HasColumnType("bigint").IsRequired().UseIdentityColumn();
        builder.Property(e => e.EntityName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EntityKey).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Action).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(e => e.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ChangedColumns).HasColumnType("nvarchar(max)");
        builder.Property(e => e.PerformedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IpAddress).HasColumnType("nvarchar(45)").HasMaxLength(45);
        builder.Property(e => e.CorrelationId).HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.EventDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => new { e.EntityName, e.EntityKey, e.EventDate }, "IX_tblAuditLog_EntityName_EntityKey_EventDate").HasFilter(null);
        builder.HasIndex(e => e.EventDate, "IX_tblAuditLog_EventDate").HasFilter(null);
        builder.HasIndex(e => new { e.PerformedBy, e.EventDate }, "IX_tblAuditLog_PerformedBy_EventDate").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAuditLog_CompanyId").HasFilter(null);
    }
}
