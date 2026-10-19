using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblVendor</c>.
/// </summary>
public class Vendor : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int VendorId { get; set; }
    public string VendorCode { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? CommercialRegistrationNo { get; set; }
    public string? VatNumber { get; set; }
    public bool IsApproved { get; set; }
    public DateOnly? ApprovalDate { get; set; }
    /// <summary>Last supplier security assessment (ISO 27001 A.5.19-A.5.22).</summary>
    public DateOnly? SecurityAssessmentDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
