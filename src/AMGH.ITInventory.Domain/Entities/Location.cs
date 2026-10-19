using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblLocation</c>.
/// </summary>
public class Location : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "Saudi Arabia";
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Building> Buildings { get; set; } = new List<Building>();
    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
