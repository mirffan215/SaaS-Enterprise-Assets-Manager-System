namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblDisposalMethod</c>.
/// </summary>
public class DisposalMethod : AuditableEntity
{
    public int DisposalMethodId { get; set; }
    public string MethodCode { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public bool RequiresDataSanitization { get; set; } = true;
    public bool RequiresCertificate { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
