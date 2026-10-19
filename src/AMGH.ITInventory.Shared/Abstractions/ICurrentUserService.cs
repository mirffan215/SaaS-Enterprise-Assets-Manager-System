namespace AMGH.ITInventory.Shared.Abstractions;

/// <summary>The signed-in user for the current request (or "SYSTEM" for background jobs).</summary>
public interface ICurrentUserService
{
    int? UserId { get; }

    /// <summary>Username written to CreatedBy / ModifiedBy / PerformedBy columns.</summary>
    string UserName { get; }

    string? IpAddress { get; }

    /// <summary>Request correlation id, written to the audit log.</summary>
    string? CorrelationId { get; }

    bool IsAuthenticated { get; }
}
