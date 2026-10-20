using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblAssetMaintenance</c>.
/// </summary>
public class AssetMaintenance : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    /// <summary>Document number, e.g. AMGH-MOV-000123 (Release 2.13; assigned when the document is created).</summary>
    public string? DocumentNumber { get; set; }
    /// <summary>The matching document in the ERP system (e.g. an SAP posting number), if any.</summary>
    public string? ErpReference { get; set; }
    public int AssetMaintenanceId { get; set; }
    public int AssetId { get; set; }
    public MaintenanceType MaintenanceType { get; set; }
    public MaintenanceStatus MaintenanceStatus { get; set; } = MaintenanceStatus.Scheduled;
    public DateOnly ScheduledDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int? VendorId { get; set; }
    public string? TicketNumber { get; set; }
    public string ProblemDescription { get; set; } = string.Empty;
    public string? ResolutionDetails { get; set; }
    public string? PerformedBy { get; set; }
    /// <summary>The technician (user account) the job is assigned to; Release 2.10, script 17. PerformedBy stays for external technicians.</summary>
    public int? AssignedToUserId { get; set; }
    public decimal? Cost { get; set; }
    /// <summary>The vendor invoice (or internal reference) behind <see cref="Cost"/>; migration 0002.</summary>
    public string? InvoiceNumber { get; set; }
    public int? CurrencyId { get; set; }
    public decimal? DowntimeHours { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }

    public virtual Asset Asset { get; set; } = null!;
    public virtual Vendor? Vendor { get; set; }
    public virtual User? AssignedToUser { get; set; }
    public virtual Currency? Currency { get; set; }
}
