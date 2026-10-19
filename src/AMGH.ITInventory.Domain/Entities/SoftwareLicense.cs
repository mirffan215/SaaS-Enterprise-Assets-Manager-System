using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblSoftwareLicense</c>.
/// </summary>
public class SoftwareLicense : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int SoftwareLicenseId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string SoftwareName { get; set; } = string.Empty;
    public string? SoftwareVersion { get; set; }
    public int? PublisherManufacturerId { get; set; }
    public int? VendorId { get; set; }
    public LicenseType LicenseType { get; set; }
    /// <summary>Encrypted with AMGH_DataProtection_Key; use uspSoftwareLicense_SetKey/GetKey.</summary>
    public byte[]? LicenseKeyEncrypted { get; set; }
    /// <summary>NULL = unlimited.</summary>
    public int? TotalSeats { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? Cost { get; set; }
    public int? CurrencyId { get; set; }
    public int RenewalReminderDays { get; set; } = 60;
    public int? ContractOwnerEmployeeId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Manufacturer? Publisher { get; set; }
    public virtual Vendor? Vendor { get; set; }
    public virtual Currency? Currency { get; set; }
    public virtual Employee? ContractOwner { get; set; }

    public virtual ICollection<SoftwareLicenseAllocation> Allocations { get; set; } = new List<SoftwareLicenseAllocation>();
}
