using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Immutable application audit trail (ISO 27001 A.8.15). UPDATE/DELETE blocked by trigger.
/// </summary>
public class AuditLog : ICompanyScoped
{
    /// <summary>Owning company; NULL for platform-level events.</summary>
    public int? CompanyId { get; set; }
    public long AuditLogId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedColumns { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime EventDate { get; set; }
}
