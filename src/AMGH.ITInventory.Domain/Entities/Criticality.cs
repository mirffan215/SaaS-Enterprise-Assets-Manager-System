using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblCriticality</c>.
/// </summary>
public class Criticality : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int CriticalityId { get; set; }
    public string CriticalityCode { get; set; } = string.Empty;
    public string CriticalityName { get; set; } = string.Empty;
    public byte CriticalityLevel { get; set; }
    /// <summary>Recovery Time Objective (Business Continuity Policy).</summary>
    public int? RtoHours { get; set; }
    /// <summary>Recovery Point Objective (Business Continuity Policy).</summary>
    public int? RpoHours { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
