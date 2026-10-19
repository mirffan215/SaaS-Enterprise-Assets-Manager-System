using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>Maps to <c>dbo.tblDataExportRequest</c> (script 19): a request for a full export of the company's data.</summary>
public class DataExportRequest : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int DataExportRequestId { get; set; }
    public string Status { get; set; } = DataExportStatuses.Requested;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>One-time download token, encrypted (never stored in clear text).</summary>
    public string? TokenProtected { get; set; }
    /// <summary>Password of the encrypted archive, encrypted (never stored in clear text).</summary>
    public string? PasswordProtected { get; set; }
    public DateTime? TokenExpiresDate { get; set; }
    public string? DownloadedBy { get; set; }
    public DateTime? DownloadedDate { get; set; }
}

public static class DataExportStatuses
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Downloaded = "Downloaded";
    public const string Expired = "Expired";
}
