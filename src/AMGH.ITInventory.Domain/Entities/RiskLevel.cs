using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblRiskLevel</c>.
/// </summary>
public class RiskLevel : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int RiskLevelId { get; set; }
    public string RiskLevelCode { get; set; } = string.Empty;
    public string RiskLevelName { get; set; } = string.Empty;
    public byte MinScore { get; set; }
    public byte MaxScore { get; set; }
    public short? ReviewFrequencyMonths { get; set; }
    public string? ColorHex { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
