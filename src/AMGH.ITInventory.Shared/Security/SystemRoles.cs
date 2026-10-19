namespace AMGH.ITInventory.Shared.Security;

/// <summary>System role names. Must match dbo.tblRole (seeded by 07_SeedData.sql).</summary>
public static class SystemRoles
{
    /// <summary>Full system access including users, roles and settings.</summary>
    public const string SystemAdministrator = "SystemAdministrator";
    /// <summary>Owns the asset register: full lifecycle management and approvals.</summary>
    public const string ITAssetManager = "ITAssetManager";
    /// <summary>Day-to-day asset operations without approvals or deletion.</summary>
    public const string ITTechnician = "ITTechnician";
    /// <summary>Oversight of classification, risk, disposal approvals and audit logs.</summary>
    public const string InformationSecurityOfficer = "InformationSecurityOfficer";
    /// <summary>Read-only access to the register, reports and audit logs for internal/external audit.</summary>
    public const string Auditor = "Auditor";
    /// <summary>Views assets of the department and requests movements.</summary>
    public const string DepartmentManager = "DepartmentManager";
    /// <summary>Read-only access to the asset register.</summary>
    public const string ReadOnly = "ReadOnly";
}
