using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblSoftwareLicenseAllocation</c>.
/// </summary>
public class SoftwareLicenseAllocation : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int SoftwareLicenseAllocationId { get; set; }
    public int SoftwareLicenseId { get; set; }
    public int? AssetId { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime AllocatedDate { get; set; }
    public DateTime? RevokedDate { get; set; }
    public bool IsCurrent { get; set; } = true;
    public string? Notes { get; set; }

    public virtual SoftwareLicense SoftwareLicense { get; set; } = null!;
    public virtual Asset? Asset { get; set; }
    public virtual Employee? Employee { get; set; }
}
