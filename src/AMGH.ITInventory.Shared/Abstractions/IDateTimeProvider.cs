namespace AMGH.ITInventory.Shared.Abstractions;

/// <summary>Testable clock. All persisted timestamps are UTC.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly UtcToday { get; }
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);
}
