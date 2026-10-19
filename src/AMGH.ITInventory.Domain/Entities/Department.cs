using AMGH.ITInventory.Domain.Common;

namespace AMGH.ITInventory.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.tblDepartment</c>.
/// </summary>
public class Department : AuditableEntity, ICompanyOwned
{
    /// <summary>Owning company. Set by the data layer from the signed-in workspace; never changes.</summary>
    public int CompanyId { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int? ParentDepartmentId { get; set; }
    public int? HeadEmployeeId { get; set; }
    public string? CostCenter { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual Department? ParentDepartment { get; set; }
    public virtual Employee? HeadEmployee { get; set; }

    public virtual ICollection<Department> SubDepartments { get; set; } = new List<Department>();
    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
