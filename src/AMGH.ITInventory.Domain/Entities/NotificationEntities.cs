using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>How a company sends e-mail (dbo.tblCompanyMailSettings, script 14). The secret is stored encrypted.</summary>
public class CompanyMailSettings : ICompanyOwned
{
    public int CompanyId { get; set; }
    public bool IsEnabled { get; set; }
    public string Mode { get; set; } = "ConnectorRelay";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public string Security { get; set; } = "StartTls";
    /// <summary>SHA-256 thumbprint of a private relay certificate to accept (pinning); null = normal validation.</summary>
    public string? CertificateThumbprint { get; set; }
    public string? Username { get; set; }
    public string? SecretProtected { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }
    public string? AppBaseUrl { get; set; }
    public DateTime? LastTestDate { get; set; }
    public string? LastTestResult { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedDate { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>A company's own wording of a notification (dbo.tblNotificationTemplate, script 14).</summary>
public class NotificationTemplate : ICompanyOwned
{
    public int CompanyId { get; set; }
    public int NotificationTemplateId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedDate { get; set; }
}

/// <summary>A company's settings for one kind of notification (dbo.tblNotificationRule, script 14).</summary>
public class NotificationRule : ICompanyOwned
{
    public int CompanyId { get; set; }
    public int NotificationRuleId { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool InAppEnabled { get; set; }
    public string? DaysBefore { get; set; }
    public string? RecipientRoles { get; set; }
    public DateOnly? LastRunDate { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedDate { get; set; }
}

/// <summary>A user's e-mail choice for one kind of notification (dbo.tblNotificationPreference, script 14).</summary>
public class NotificationPreference : ICompanyOwned
{
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
    public DateTime ModifiedDate { get; set; }
}
