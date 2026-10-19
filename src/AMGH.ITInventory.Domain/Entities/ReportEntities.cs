using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>Report generation history and export audit trail (dbo.tblReportRun, script 12). Append-only.</summary>
public class ReportRun : ICompanyOwned
{
    public int CompanyId { get; set; }
    public long ReportRunId { get; set; }
    public string ReportCode { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string? Filters { get; set; }
    public string RunBy { get; set; } = string.Empty;
    public DateTime RunDate { get; set; }
}

/// <summary>A saved report configuration (filters, columns, sort, grouping) (dbo.tblSavedReport, script 12).</summary>
public class SavedReport : AuditableEntity, ICompanyOwned
{
    public int CompanyId { get; set; }
    public int SavedReportId { get; set; }
    public string ReportCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    public int OwnerUserId { get; set; }
}

/// <summary>Approval stage n of a request type must be decided by a member of <see cref="ApproverRole"/> (dbo.tblApprovalRule, script 13).</summary>
public class ApprovalRule : ICompanyOwned
{
    public int CompanyId { get; set; }
    public int ApprovalRuleId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public byte StageNo { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedDate { get; set; }
}

/// <summary>A decision on one approval stage (dbo.tblApprovalStep, script 13). Append-only.</summary>
public class ApprovalStep : ICompanyOwned
{
    public int CompanyId { get; set; }
    public long ApprovalStepId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public int RequestId { get; set; }
    public byte StageNo { get; set; }
    public string? ApproverRole { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string DecidedBy { get; set; } = string.Empty;
    public DateTime DecidedDate { get; set; }
    public string? Remarks { get; set; }
}
