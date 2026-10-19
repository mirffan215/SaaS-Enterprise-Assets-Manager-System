using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AMGH.ITInventory.Persistence.Auditing;

/// <summary>
/// A change captured before SaveChanges. Converted to a <see cref="AuditLog"/> row after the
/// first save, when database-generated keys (IDENTITY) are known.
/// </summary>
internal sealed class PendingAuditEntry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly EntityEntry _entry;
    private readonly IReadOnlyList<string> _addedPropertyNames;

    public PendingAuditEntry(
        EntityEntry entry,
        AuditAction action,
        Dictionary<string, object?>? oldValues,
        Dictionary<string, object?>? newValues,
        IReadOnlyList<string> changedColumns,
        IReadOnlyList<string> addedPropertyNames,
        string performedBy,
        string? ipAddress,
        string? correlationId,
        DateTime eventDateUtc)
    {
        _entry = entry;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        ChangedColumns = changedColumns;
        _addedPropertyNames = addedPropertyNames;
        PerformedBy = performedBy;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
        EventDateUtc = eventDateUtc;
    }

    public AuditAction Action { get; }
    public Dictionary<string, object?>? OldValues { get; }
    public Dictionary<string, object?>? NewValues { get; }
    public IReadOnlyList<string> ChangedColumns { get; }
    public string PerformedBy { get; }
    public string? IpAddress { get; }
    public string? CorrelationId { get; }
    public DateTime EventDateUtc { get; }

    public AuditLog ToAuditLog()
    {
        var newValues = NewValues;
        if (Action == AuditAction.Create)
        {
            // Read after save so IDENTITY values are the real ones.
            newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var name in _addedPropertyNames)
                newValues[name] = AuditTrailBuilder.Mask(name, _entry.Property(name).CurrentValue);
        }

        return new AuditLog
        {
            EntityName = _entry.Metadata.ClrType.Name,
            EntityKey = BuildKey(),
            Action = Action,
            OldValues = OldValues is null ? null : JsonSerializer.Serialize(OldValues, JsonOptions),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOptions),
            ChangedColumns = ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(ChangedColumns, JsonOptions),
            PerformedBy = PerformedBy,
            IpAddress = IpAddress,
            CorrelationId = CorrelationId,
            EventDate = EventDateUtc
        };
    }

    private string BuildKey()
    {
        var key = _entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;
        return string.Join(",", key.Properties.Select(p =>
            Convert.ToString(_entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));
    }
}
