using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="DataExportRequest"/> to dbo.tblDataExportRequest (script 19).</summary>
internal sealed class DataExportRequestConfiguration : IEntityTypeConfiguration<DataExportRequest>
{
    public void Configure(EntityTypeBuilder<DataExportRequest> builder)
    {
        builder.ToTable("tblDataExportRequest", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblDataExportRequest_Status", "[Status] IN (N'Requested', N'Approved', N'Rejected', N'Downloaded', N'Expired')");
            tb.HasCheckConstraint("CK_tblDataExportRequest_Approver", "[ApprovedBy] IS NULL OR [ApprovedBy] <> [RequestedBy]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.DataExportRequestId).HasName("PK_tblDataExportRequest");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DataExportRequestId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.Status).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(e => e.RequestedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RequestedDate).HasColumnType("datetime2(3)").IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ApprovedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ApprovedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.RejectionReason).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.TokenProtected).HasColumnType("nvarchar(600)").HasMaxLength(600);
        builder.Property(e => e.PasswordProtected).HasColumnType("nvarchar(600)").HasMaxLength(600);
        builder.Property(e => e.TokenExpiresDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.DownloadedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DownloadedDate).HasColumnType("datetime2(3)");

        builder.HasIndex(e => new { e.CompanyId, e.Status }, "IX_tblDataExportRequest_Company");
    }
}
