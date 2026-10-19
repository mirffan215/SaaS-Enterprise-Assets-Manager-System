using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblManufacturer</c>.
/// </summary>
public class Manufacturer : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int ManufacturerId { get; set; }
    public string ManufacturerCode { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
