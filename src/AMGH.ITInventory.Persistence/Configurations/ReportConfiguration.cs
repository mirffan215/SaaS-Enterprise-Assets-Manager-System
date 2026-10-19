using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="ReportRun"/> to dbo.tblReportRun (script 12).</summary>
internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        builder.ToTable("tblReportRun", "dbo");
        builder.HasKey(e => e.ReportRunId).HasName("PK_tblReportRun");
        builder.Property(e => e.ReportRunId).UseIdentityColumn();
        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ReportCode).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Format).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.RowCount).HasColumnType("int").IsRequired();
        builder.Property(e => e.Filters).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.RunBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RunDate).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(e => new { e.CompanyId, e.RunDate }, "IX_tblReportRun_Company_RunDate");
    }
}

/// <summary>Maps <see cref="SavedReport"/> to dbo.tblSavedReport (script 12).</summary>
internal sealed class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> builder)
    {
        builder.ToTable("tblSavedReport", "dbo");
        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.SavedReportId).HasName("PK_tblSavedReport");
        builder.Property(e => e.SavedReportId).UseIdentityColumn();
        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ReportCode).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.QueryString).HasColumnType("nvarchar(2000)").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.IsShared).HasColumnType("bit").IsRequired();
        builder.Property(e => e.OwnerUserId).HasColumnType("int").IsRequired();
        builder.HasIndex(e => new { e.CompanyId, e.ReportCode }, "IX_tblSavedReport_Company_Report");
    }
}

/// <summary>Maps <see cref="ApprovalRule"/> to dbo.tblApprovalRule (script 13).</summary>
internal sealed class ApprovalRuleConfiguration : IEntityTypeConfiguration<ApprovalRule>
{
    public void Configure(EntityTypeBuilder<ApprovalRule> builder)
    {
        builder.ToTable("tblApprovalRule", "dbo");
        builder.HasKey(e => e.ApprovalRuleId).HasName("PK_tblApprovalRule");
        builder.Property(e => e.ApprovalRuleId).UseIdentityColumn();
        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RequestType).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.StageNo).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.ApproverRole).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(e => new { e.CompanyId, e.RequestType, e.StageNo }, "UX_tblApprovalRule_Company_Type_Stage").IsUnique();
    }
}

/// <summary>Maps <see cref="ApprovalStep"/> to dbo.tblApprovalStep (script 13).</summary>
internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("tblApprovalStep", "dbo");
        builder.HasKey(e => e.ApprovalStepId).HasName("PK_tblApprovalStep");
        builder.Property(e => e.ApprovalStepId).UseIdentityColumn();
        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RequestType).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RequestId).HasColumnType("int").IsRequired();
        builder.Property(e => e.StageNo).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.ApproverRole).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.Decision).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.DecidedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DecidedDate).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.Remarks).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.HasIndex(e => new { e.CompanyId, e.RequestType, e.RequestId }, "IX_tblApprovalStep_Request");
    }
}
