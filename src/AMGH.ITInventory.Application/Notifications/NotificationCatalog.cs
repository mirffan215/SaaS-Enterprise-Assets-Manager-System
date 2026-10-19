using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Application.Notifications;

/// <summary>A kind of notification with its built-in defaults (a company can change them under Notification settings).</summary>
public sealed record RuleDefinition(string Key, string Title, string Description, NotificationType Type, bool Scheduled,
    string? DefaultDays, string? DefaultRoles, string DaysHelp, IReadOnlyList<string> Placeholders);

/// <summary>The effective settings of a kind of notification for the current company.</summary>
public sealed record RuleSettings(string Key, bool IsEnabled, bool EmailEnabled, bool InAppEnabled, IReadOnlyList<int> DaysBefore,
    IReadOnlyList<string> RecipientRoles, DateOnly? LastRunDate);

public sealed record TemplateText(string Subject, string BodyHtml);

public static class NotificationCatalog
{
    public const string WarrantyExpiry = "WarrantyExpiry";
    public const string LicenseExpiry = "LicenseExpiry";
    public const string MaintenanceDue = "MaintenanceDue";
    public const string VerificationDue = "VerificationDue";
    public const string AssetAssigned = "AssetAssigned";
    public const string AssetReturned = "AssetReturned";
    public const string ApprovalRequired = "ApprovalRequired";
    public const string ApprovalDecided = "ApprovalDecided";
    /// <summary>Internal bookkeeping row (last daily run); not shown as a kind of notification.</summary>
    public const string DailyRun = "DailyRun";
    public const string TestTemplate = "Test";

    private static readonly string[] Asset = { "AssetNumber", "AssetName", "CompanyName", "Link" };

    public static readonly IReadOnlyList<RuleDefinition> Rules = new RuleDefinition[]
    {
        new(WarrantyExpiry, "Warranty expiry", "Before and on the day an asset's warranty ends.", NotificationType.WarrantyExpiry, true,
            "90,60,30,7,0", "ITAssetManager", "Days before the end date (0 = on the day).",
            Asset.Concat(new[] { "EndDate", "DaysLeft", "Provider", "Contract" }).ToArray()),
        new(LicenseExpiry, "Software licence expiry", "Before a licence expires; the licence owner is included.", NotificationType.LicenseExpiry, true,
            "60,30,7,0", "ITAssetManager", "Days before expiry (0 = on the day).",
            new[] { "Software", "LicenseNumber", "ExpiryDate", "DaysLeft", "SeatsUsed", "Seats", "CompanyName", "Link" }),
        new(MaintenanceDue, "Maintenance due", "Scheduled maintenance coming up; overdue jobs are escalated weekly.", NotificationType.MaintenanceDue, true,
            "1,0", "ITTechnician,ITAssetManager", "Days before the scheduled date.",
            Asset.Concat(new[] { "ScheduledDate", "MaintenanceType", "Problem", "Status" }).ToArray()),
        new(VerificationDue, "Physical verification due", "Weekly summary of assets due or overdue for verification.", NotificationType.AuditDue, true,
            "30", "ITAssetManager", "Include assets due within this many days.",
            new[] { "DueCount", "OverdueCount", "CompanyName", "Link" }),
        new(AssetAssigned, "Asset assigned", "To the person receiving an asset (by e-mail) and the IT asset managers.", NotificationType.AssignmentAcknowledgement, false,
            null, "ITAssetManager", string.Empty, Asset.Concat(new[] { "Custodian", "AssignedBy", "AssignmentType" }).ToArray()),
        new(AssetReturned, "Asset returned", "To the person returning an asset and the IT asset managers.", NotificationType.System, false,
            null, "ITAssetManager", string.Empty, Asset.Concat(new[] { "Custodian", "Condition" }).ToArray()),
        new(ApprovalRequired, "Approval required", "Movement or disposal request waiting: to everyone who may approve it.", NotificationType.ApprovalRequired, false,
            null, null, string.Empty, Asset.Concat(new[] { "RequestType", "DocumentNumber", "Summary", "RequestedBy", "Reason" }).ToArray()),
        new(ApprovalDecided, "Request decided", "To the requester when a movement or disposal is approved or rejected.", NotificationType.System, false,
            null, null, string.Empty, Asset.Concat(new[] { "RequestType", "DocumentNumber", "Decision", "DecidedBy", "Remarks" }).ToArray())
    };

    public static RuleDefinition? Find(string key) => Rules.FirstOrDefault(r => r.Key == key);

    /// <summary>Built-in wording. Values are inserted with {{Name}} and are HTML-encoded automatically.</summary>
    public static TemplateText DefaultTemplate(string key) => key switch
    {
        WarrantyExpiry => new("Warranty {{DaysLeft}}: {{AssetNumber}} {{AssetName}}",
            "<p>The warranty for <strong>{{AssetNumber}}</strong> ({{AssetName}}) ends on <strong>{{EndDate}}</strong> ({{DaysLeft}}).</p>" +
            "<p>Provider: {{Provider}} · contract {{Contract}}</p><p>Renew the support contract or plan the replacement in time.</p>"),
        LicenseExpiry => new("Licence {{DaysLeft}}: {{Software}}",
            "<p>The licence <strong>{{Software}}</strong> ({{LicenseNumber}}) expires on <strong>{{ExpiryDate}}</strong> ({{DaysLeft}}).</p>" +
            "<p>Seats in use: {{SeatsUsed}} of {{Seats}}.</p><p>Start the renewal so users are not interrupted.</p>"),
        MaintenanceDue => new("Maintenance {{Status}}: {{AssetNumber}} on {{ScheduledDate}}",
            "<p>{{MaintenanceType}} maintenance for <strong>{{AssetNumber}}</strong> ({{AssetName}}) is <strong>{{Status}}</strong>, scheduled for {{ScheduledDate}}.</p>" +
            "<p>Work: {{Problem}}</p>"),
        VerificationDue => new("Physical verification: {{OverdueCount}} overdue, {{DueCount}} due soon",
            "<p><strong>{{OverdueCount}}</strong> asset(s) are overdue for physical verification and <strong>{{DueCount}}</strong> are due soon.</p>" +
            "<p>Open Physical verification and scan the assets to record the results.</p>"),
        AssetAssigned => new("{{AssetNumber}} has been assigned to {{Custodian}}",
            "<p><strong>{{AssetNumber}}</strong> ({{AssetName}}) has been assigned to <strong>{{Custodian}}</strong> ({{AssignmentType}}) by {{AssignedBy}}.</p>" +
            "<p>Please keep it secure, use it under the Acceptable Use Policy, and sign the hand-over form.</p>"),
        AssetReturned => new("{{AssetNumber}} has been returned",
            "<p><strong>{{AssetNumber}}</strong> ({{AssetName}}) was returned by {{Custodian}}. Condition: {{Condition}}.</p>"),
        ApprovalRequired => new("Approval needed: {{RequestType}} {{DocumentNumber}} ({{AssetNumber}})",
            "<p>{{RequestedBy}} asks for approval: <strong>{{Summary}}</strong> for <strong>{{AssetNumber}}</strong> ({{AssetName}}).</p>" +
            "<p>Reason: {{Reason}}</p><p>Decide it in the Approval Center.</p>"),
        ApprovalDecided => new("{{RequestType}} {{DocumentNumber}} ({{AssetNumber}}): {{Decision}}",
            "<p>Your {{RequestType}} request for <strong>{{AssetNumber}}</strong> ({{AssetName}}) was <strong>{{Decision}}</strong> by {{DecidedBy}}.</p>" +
            "<p>{{Remarks}}</p>"),
        _ => new("Test message from {{CompanyName}} Enterprise Asset Management System",
            "<p>This is a test message. E-mail delivery from <strong>{{CompanyName}}</strong> Enterprise Asset Management System (EAMS) works.</p>")
    };

    /// <summary>Sample values for previews and test messages.</summary>
    public static IReadOnlyDictionary<string, string> SampleValues(string companyName) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AssetNumber"] = "AMGH-IT-LAP-00001", ["AssetName"] = "Dell Latitude 5440", ["CompanyName"] = companyName, ["Link"] = "#",
        ["EndDate"] = "30 Nov 2026", ["DaysLeft"] = "in 30 days", ["Provider"] = "Dell KSA", ["Contract"] = "PS-7781",
        ["Software"] = "Adobe Acrobat Pro", ["LicenseNumber"] = "AMGH-IT-LIC-00001", ["ExpiryDate"] = "31 Dec 2026", ["SeatsUsed"] = "18", ["Seats"] = "20",
        ["ScheduledDate"] = "20 Oct 2026", ["MaintenanceType"] = "Preventive", ["Problem"] = "Quarterly service", ["Status"] = "due tomorrow",
        ["DueCount"] = "12", ["OverdueCount"] = "3", ["Custodian"] = "Sara Al-Harbi", ["AssignedBy"] = "it.admin", ["AssignmentType"] = "Permanent",
        ["Condition"] = "Good", ["RequestType"] = "Movement", ["Summary"] = "Relocation to Riyadh Office", ["RequestedBy"] = "tech", ["Reason"] = "Office move",
        ["Decision"] = "approved", ["DecidedBy"] = "it.manager", ["Remarks"] = "", ["DocumentNumber"] = "AMGH-MOV-000123"
    };
}
