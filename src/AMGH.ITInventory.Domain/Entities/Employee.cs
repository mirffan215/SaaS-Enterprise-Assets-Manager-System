using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblEmployee</c>.
/// </summary>
public class Employee : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Computed column (read-only).</summary>
    public string? FullName { get; private set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public int DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public DateOnly? HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Active;

    public virtual Department Department { get; set; } = null!;
    public virtual Location? Location { get; set; }
    public virtual Employee? Manager { get; set; }

    public virtual ICollection<Employee> DirectReports { get; set; } = new List<Employee>();
    public virtual ICollection<Asset> AssignedAssets { get; set; } = new List<Asset>();
    public virtual ICollection<Asset> OwnedAssets { get; set; } = new List<Asset>();
    public virtual ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
}
