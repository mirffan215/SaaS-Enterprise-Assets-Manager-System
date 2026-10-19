using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblWarranty</c>.
/// </summary>
public class Warranty : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int WarrantyId { get; set; }
    public int AssetId { get; set; }
    public int WarrantyStatusId { get; set; }
    public WarrantyType WarrantyType { get; set; } = WarrantyType.Manufacturer;
    public int? ProviderVendorId { get; set; }
    public string? ContractNumber { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? SupportLevel { get; set; }
    public decimal? Cost { get; set; }
    public int? CurrencyId { get; set; }
    public string? Terms { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual WarrantyStatus WarrantyStatus { get; set; } = null!;
    public virtual Vendor? ProviderVendor { get; set; }
    public virtual Currency? Currency { get; set; }
}
