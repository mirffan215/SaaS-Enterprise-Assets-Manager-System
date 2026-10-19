using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblNotificationQueue</c>.
/// </summary>
public class NotificationQueueItem : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public long NotificationQueueId { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;
    public string RecipientAddress { get; set; } = string.Empty;
    public int? RecipientUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; } = true;
    public QueueStatus QueueStatus { get; set; } = QueueStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime NextAttemptDate { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentDate { get; set; }
    public string? DeduplicationKey { get; set; }

    public virtual User? RecipientUser { get; set; }
}
