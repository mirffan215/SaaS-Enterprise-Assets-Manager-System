namespace AMGH.ITInventory.Domain.Common;

/// <summary>
/// A row that belongs to exactly one company. The data layer sets <see cref="CompanyId"/> from the
/// signed-in workspace on insert, filters every query by it, and refuses any change to it. SQL Server
/// Row-Level Security enforces the same rule in the database.
/// </summary>
public interface ICompanyOwned
{
    int CompanyId { get; set; }
}

/// <summary>A log row that belongs to a company, or to the platform when <see cref="CompanyId"/> is NULL.</summary>
public interface ICompanyScoped
{
    int? CompanyId { get; set; }
}
