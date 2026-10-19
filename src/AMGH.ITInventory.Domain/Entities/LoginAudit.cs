using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblLoginAudit</c>.
/// </summary>
public class LoginAudit : ICompanyScoped
{
    /// <summary>Owning company; NULL for platform-level events.</summary>
    public int? CompanyId { get; set; }
    public long LoginAuditId { get; set; }
    public int? UserId { get; set; }
    public string UsernameAttempted { get; set; } = string.Empty;
    public LoginEventType EventType { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }
    public DateTime EventDate { get; set; }

    public virtual User? User { get; set; }
}
