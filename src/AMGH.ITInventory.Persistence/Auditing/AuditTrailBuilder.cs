using AMGH.ITInventory.Domain.Common;
using AMGH.ITInventory.Domain.Entities;
using AMGH.ITInventory.Domain.Enums;
using AMGH.ITInventory.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AMGH.ITInventory.Persistence.Auditing;

/// <summary>Captures entity changes for dbo.tblAuditLog (ISO 27001 A.8.15).</summary>
internal static class AuditTrailBuilder
{
    /// <summary>Log tables and high-volume operational tables are not themselves audited.</summary>
    private static readonly HashSet<Type> ExcludedTypes = new()
    {
        typeof(AuditLog), typeof(AssetHistory), typeof(LoginAudit), typeof(QrLabelPrintLog),
        typeof(UserPasswordHistory), typeof(Notification), typeof(NotificationQueueItem), typeof(ReportRun), typeof(ApprovalStep), typeof(NotificationPreference)
    };

    /// <summary>Recorded as changed, but the value is never written to the log.</summary>
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash), nameof(User.SecurityStamp),
        nameof(SoftwareLicense.LicenseKeyEncrypted), nameof(AssetQrCode.QrImagePng), nameof(CompanyMailSettings.SecretProtected)
    };

    /// <summary>Stamp columns are already on the row itself.</summary>
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        nameof(AuditableEntity.RowVersion), nameof(AuditableEntity.CreatedBy), nameof(AuditableEntity.CreatedDate),
        nameof(AuditableEntity.ModifiedBy), nameof(AuditableEntity.ModifiedDate),
        nameof(AuditableEntity.DeletedBy), nameof(AuditableEntity.DeletedDate),
        // Sign-in bookkeeping: recorded in dbo.tblLoginAudit instead.
        nameof(User.LastLoginDate), nameof(User.LastLoginIp), nameof(User.AccessFailedCount)
    };

    internal const string Redacted = "***";

    public static object? Mask(string propertyName, object? value) =>
        SensitiveProperties.Contains(propertyName) && value is not null ? Redacted : value;

    public static List<PendingAuditEntry> Capture(ChangeTracker changeTracker, ICurrentUserService user, DateTime utcNow)
    {
        var result = new List<PendingAuditEntry>();
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (ExcludedTypes.Contains(entry.Metadata.ClrType)) continue;

            var audit = entry.State switch
            {
                EntityState.Added => ForAdded(entry, user, utcNow),
                EntityState.Deleted => ForDeleted(entry, user, utcNow),
                _ => ForModified(entry, user, utcNow)
            };
            if (audit is not null) result.Add(audit);
        }
        return result;
    }

    private static IEnumerable<PropertyEntry> AuditedProperties(EntityEntry entry) =>
        entry.Properties.Where(p => !IgnoredProperties.Contains(p.Metadata.Name)
                                    && p.Metadata.GetComputedColumnSql() is null);

    private static PendingAuditEntry ForAdded(EntityEntry entry, ICurrentUserService user, DateTime now)
    {
        var names = AuditedProperties(entry).Select(p => p.Metadata.Name).ToList();
        return new PendingAuditEntry(entry, AuditAction.Create, null, null, Array.Empty<string>(), names,
            user.UserName, user.IpAddress, user.CorrelationId, now);
    }

    private static PendingAuditEntry ForDeleted(EntityEntry entry, ICurrentUserService user, DateTime now)
    {
        var old = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var p in AuditedProperties(entry))
            old[p.Metadata.Name] = Mask(p.Metadata.Name, p.OriginalValue);
        return new PendingAuditEntry(entry, AuditAction.Delete, old, null, Array.Empty<string>(), Array.Empty<string>(),
            user.UserName, user.IpAddress, user.CorrelationId, now);
    }

    private static PendingAuditEntry? ForModified(EntityEntry entry, ICurrentUserService user, DateTime now)
    {
        var old = new Dictionary<string, object?>(StringComparer.Ordinal);
        var @new = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changed = new List<string>();

        foreach (var p in AuditedProperties(entry))
        {
            if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
            var name = p.Metadata.Name;
            changed.Add(name);
            old[name] = Mask(name, p.OriginalValue);
            @new[name] = Mask(name, p.CurrentValue);
        }
        if (changed.Count == 0) return null;

        var action = AuditAction.Update;
        if (changed.Contains(nameof(ISoftDelete.IsDeleted)) && entry.Entity is ISoftDelete sd)
            action = sd.IsDeleted ? AuditAction.Delete : AuditAction.Restore;

        return new PendingAuditEntry(entry, action, old, @new, changed, Array.Empty<string>(),
            user.UserName, user.IpAddress, user.CorrelationId, now);
    }
}
