using AMGH.ITInventory.Application.Notifications;
using AMGH.ITInventory.Domain.Enums;

namespace AMGH.ITInventory.Web.Models;

public sealed class NotificationCentreViewModel
{
    public IReadOnlyList<InboxItem> Items { get; init; } = Array.Empty<InboxItem>();
    public IReadOnlyDictionary<string, bool> Preferences { get; init; } = new Dictionary<string, bool>();
}

public sealed class NotificationSettingsViewModel
{
    public string Tab { get; init; } = "email";
    public required MailSettingsForm Mail { get; init; }
    public IReadOnlyList<RuleSettings> Rules { get; init; } = Array.Empty<RuleSettings>();
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<(RuleDefinition Rule, bool Custom)> Templates { get; init; } = Array.Empty<(RuleDefinition, bool)>();
    public IReadOnlyList<QueueItemView> Queue { get; init; } = Array.Empty<QueueItemView>();
    public IReadOnlyDictionary<QueueStatus, int> QueueCounts { get; init; } = new Dictionary<QueueStatus, int>();
    public QueueStatus? QueueFilter { get; init; }
}

public sealed class TemplateEditViewModel
{
    public required RuleDefinition Rule { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsCustom { get; init; }
}
