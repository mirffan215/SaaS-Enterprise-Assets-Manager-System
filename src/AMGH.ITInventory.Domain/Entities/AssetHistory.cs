using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Immutable lifecycle history per asset. UPDATE/DELETE blocked by trigger.
/// </summary>
public class AssetHistory : ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public long AssetHistoryId { get; set; }
    public int AssetId { get; set; }
    public AssetEventType EventType { get; set; }
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Remarks { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }

    public virtual Asset Asset { get; set; } = null!;
}
