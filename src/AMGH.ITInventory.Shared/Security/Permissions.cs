namespace AMGH.ITInventory.Shared.Security;

/// <summary>
/// Permission codes. Must match dbo.tblPermission (seeded by 07_SeedData.sql).
/// Used as ASP.NET Core authorization policy names.
/// </summary>
public static class Permissions
{
    public static class Dashboard
    {
        /// <summary>View dashboard.</summary>
        public const string View = "Dashboard.View";
    }

    public static class Assets
    {
        /// <summary>View assets.</summary>
        public const string View = "Assets.View";
        /// <summary>Create assets.</summary>
        public const string Create = "Assets.Create";
        /// <summary>Edit assets.</summary>
        public const string Edit = "Assets.Edit";
        /// <summary>Delete (soft) assets.</summary>
        public const string Delete = "Assets.Delete";
        /// <summary>Bulk import assets.</summary>
        public const string Import = "Assets.Import";
        /// <summary>Bulk export assets.</summary>
        public const string Export = "Assets.Export";
        /// <summary>View information security details.</summary>
        public const string ViewSecurityDetails = "Assets.ViewSecurityDetails";
        /// <summary>View procurement and financial details.</summary>
        public const string ViewFinancials = "Assets.ViewFinancials";
    }

    public static class Assignments
    {
        /// <summary>View assignments.</summary>
        public const string View = "Assignments.View";
        /// <summary>Assign assets.</summary>
        public const string Assign = "Assignments.Assign";
        /// <summary>Return assets.</summary>
        public const string Return = "Assignments.Return";
    }

    public static class Movements
    {
        /// <summary>View movements.</summary>
        public const string View = "Movements.View";
        /// <summary>Request movements.</summary>
        public const string Request = "Movements.Request";
        /// <summary>Approve or reject movements.</summary>
        public const string Approve = "Movements.Approve";
        /// <summary>Complete approved movements.</summary>
        public const string Complete = "Movements.Complete";
    }

    public static class Maintenance
    {
        /// <summary>View maintenance.</summary>
        public const string View = "Maintenance.View";
        /// <summary>Create and update maintenance.</summary>
        public const string Manage = "Maintenance.Manage";
    }

    public static class Audits
    {
        /// <summary>View physical audits.</summary>
        public const string View = "Audits.View";
        /// <summary>Perform physical audits.</summary>
        public const string Perform = "Audits.Perform";
    }

    public static class Disposals
    {
        /// <summary>View disposals.</summary>
        public const string View = "Disposals.View";
        /// <summary>Request disposal.</summary>
        public const string Request = "Disposals.Request";
        /// <summary>Approve or reject disposal.</summary>
        public const string Approve = "Disposals.Approve";
        /// <summary>Complete disposal with evidence.</summary>
        public const string Complete = "Disposals.Complete";
    }

    public static class Warranties
    {
        /// <summary>View warranties.</summary>
        public const string View = "Warranties.View";
        /// <summary>Manage warranties.</summary>
        public const string Manage = "Warranties.Manage";
    }

    public static class Licenses
    {
        /// <summary>View software licenses.</summary>
        public const string View = "Licenses.View";
        /// <summary>Manage software licenses.</summary>
        public const string Manage = "Licenses.Manage";
        /// <summary>Reveal license keys.</summary>
        public const string ViewKeys = "Licenses.ViewKeys";
    }

    public static class QrCodes
    {
        /// <summary>Generate QR codes.</summary>
        public const string Generate = "QrCodes.Generate";
        /// <summary>Print QR labels.</summary>
        public const string Print = "QrCodes.Print";
        /// <summary>Reprint QR labels.</summary>
        public const string Reprint = "QrCodes.Reprint";
    }

    public static class Documents
    {
        /// <summary>View attachments.</summary>
        public const string View = "Documents.View";
        /// <summary>Upload attachments.</summary>
        public const string Upload = "Documents.Upload";
        /// <summary>Delete attachments.</summary>
        public const string Delete = "Documents.Delete";
    }

    public static class Reports
    {
        /// <summary>View reports.</summary>
        public const string View = "Reports.View";
        /// <summary>Export reports.</summary>
        public const string Export = "Reports.Export";
    }

    public static class MasterData
    {
        /// <summary>View master data.</summary>
        public const string View = "MasterData.View";
        /// <summary>Manage master data.</summary>
        public const string Manage = "MasterData.Manage";
    }

    public static class Administration
    {
        /// <summary>View users.</summary>
        public const string UsersView = "Users.View";
        /// <summary>Manage users.</summary>
        public const string UsersManage = "Users.Manage";
        /// <summary>Manage roles and permissions.</summary>
        public const string RolesManage = "Roles.Manage";
        /// <summary>View audit and login logs.</summary>
        public const string AuditLogsView = "AuditLogs.View";
        /// <summary>Manage notification queue.</summary>
        public const string NotificationsManage = "Notifications.Manage";
        /// <summary>Manage system settings.</summary>
        public const string SystemSettings = "System.Settings";
    }

    /// <summary>Every permission code, for policy registration.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Dashboard.View",
        "Assets.View",
        "Assets.Create",
        "Assets.Edit",
        "Assets.Delete",
        "Assets.Import",
        "Assets.Export",
        "Assets.ViewSecurityDetails",
        "Assets.ViewFinancials",
        "Assignments.View",
        "Assignments.Assign",
        "Assignments.Return",
        "Movements.View",
        "Movements.Request",
        "Movements.Approve",
        "Movements.Complete",
        "Maintenance.View",
        "Maintenance.Manage",
        "Audits.View",
        "Audits.Perform",
        "Disposals.View",
        "Disposals.Request",
        "Disposals.Approve",
        "Disposals.Complete",
        "Warranties.View",
        "Warranties.Manage",
        "Licenses.View",
        "Licenses.Manage",
        "Licenses.ViewKeys",
        "QrCodes.Generate",
        "QrCodes.Print",
        "QrCodes.Reprint",
        "Documents.View",
        "Documents.Upload",
        "Documents.Delete",
        "Reports.View",
        "Reports.Export",
        "MasterData.View",
        "MasterData.Manage",
        "Users.View",
        "Users.Manage",
        "Roles.Manage",
        "AuditLogs.View",
        "Notifications.Manage",
        "System.Settings"
    };
}
