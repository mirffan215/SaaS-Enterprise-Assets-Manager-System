using System.Security.Claims;
using AMGH.ITInventory.Application.Lifecycle;
using AMGH.ITInventory.Security.Authorization;
using AMGH.ITInventory.Security.Claims;
using AMGH.ITInventory.Shared.Security;

namespace AMGH.ITInventory.Web.Infrastructure;

/// <summary>A sidebar link. <see cref="Matches"/> are path prefixes that make it the current page.</summary>
public sealed record NavItem(string Text, string Url, string Icon, IReadOnlyList<string> Matches, int? Badge = null);

public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);

/// <summary>The menu for the signed-in user, plus where the current page sits in it (for breadcrumbs).</summary>
public sealed record NavModel(IReadOnlyList<NavSection> Sections, NavSection? CurrentSection, NavItem? CurrentItem, string HomeUrl);

public interface INavigation
{
    Task<NavModel> BuildAsync(HttpContext context);
}

/// <summary>
/// Builds the sidebar from the user's permissions (sections as in the brief: Overview, Asset Management, Asset Lifecycle,
/// Organization, Governance, Reporting, Administration, Platform). Items the user may not open are never shown.
/// Built once per request.
/// </summary>
internal sealed class Navigation : INavigation
{
    private readonly LinkGenerator _links;
    private readonly IApprovalService _approvals;
    private readonly Application.Abstractions.Persistence.IUnitOfWork _uow;
    private NavModel? _cached;

    public Navigation(LinkGenerator links, IApprovalService approvals, Application.Abstractions.Persistence.IUnitOfWork uow)
    {
        _links = links;
        _approvals = approvals;
        _uow = uow;
    }

    public async Task<NavModel> BuildAsync(HttpContext context)
    {
        if (_cached is not null) return _cached;
        var user = context.User;
        bool P(string permission) => user.HasPermission(permission);
        string A(string action, string controller, object? values = null) => _links.GetPathByAction(context, action, controller, values) ?? "/";
        string Md(string type) => A("List", "MasterData", new { type });

        int? waiting = null;
        if (P(Permissions.Movements.Approve) || P(Permissions.Disposals.Approve) || P(Permissions.CompanyAdmin.ExportDataApprove))
        {
            var actor = new ApprovalActor(user.Identity?.Name ?? string.Empty,
                user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal),
                user.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal));
            var n = await _approvals.CountActionableAsync(actor, context.RequestAborted);
            waiting = n > 0 ? n : null;
        }

        var auditEnabled = true;
        if (user.GetCompanyId() is { } companyId)
            auditEnabled = (await _uow.Repository<Domain.Entities.Company>().QueryAsync(q => q.Where(c => c.CompanyId == companyId)
                .Select(c => c.AssetAuditEnabled), context.RequestAborted)).FirstOrDefault(true);
        var sections = new List<NavSection>();
        void Section(string title, params (bool Show, NavItem Item)[] items)
        {
            var visible = items.Where(i => i.Show).Select(i => i.Item).ToList();
            if (visible.Count > 0) sections.Add(new NavSection(title, visible));
        }
        static string[] M(params string[] p) => p;

        var dashboard = P(Permissions.Dashboard.View);
        Section("Overview",
            (dashboard, new NavItem("Executive dashboard", A("Index", "Dashboard"), "bi-speedometer2", M("/dashboard"))),
            (!dashboard, new NavItem("Overview", A("Index", "Home", new { overview = true }), "bi-house", M("/Home"))),
            (P(Permissions.Movements.View) || P(Permissions.Disposals.View) || P(Permissions.CompanyAdmin.ExportDataApprove), new NavItem("Approval Center", A("Index", "Approvals"), "bi-inbox", M("/approvals"), waiting)),
            (true, new NavItem("Notifications", A("Index", "Notifications"), "bi-bell", M("/notifications"))));
        Section("Asset management",
            (P(Permissions.Assets.View), new NavItem("Asset register", A("Index", "Assets"), "bi-box-seam", M("/Assets", "/labels", "/a/"))),
            (P(Permissions.Assets.Create), new NavItem("Add new asset", A("Create", "Assets"), "bi-plus-square", M("/Assets/Create"))),
            (P(Permissions.Assets.Import), new NavItem("Import from Excel", A("Import", "Assets"), "bi-file-earmark-arrow-up", M("/Assets/Import"))),
            (P(Permissions.MasterData.View), new NavItem("Asset categories", A("Categories", "AssetConfiguration"), "bi-tags", M("/admin/asset-configuration/categories", "/master-data/categories", "/master-data/subcategories"))),
            (P(Permissions.MasterData.View), new NavItem("Asset numbering", A("Numbering", "AssetConfiguration"), "bi-123", M("/admin/asset-configuration/numbering"))));
        Section("Asset lifecycle",
            (P(Permissions.Movements.View), new NavItem("Movements", A("Index", "Movements"), "bi-arrow-left-right", M("/movements", "/assets/{id}/move"))),
            (P(Permissions.Maintenance.View), new NavItem("Maintenance and repairs", A("Index", "Maintenance"), "bi-tools", M("/maintenance"))),
            (P(Permissions.Audits.View) && auditEnabled, new NavItem("Physical verification", A("Index", "Verification"), "bi-qr-code-scan", M("/verification"))),
            (P(Permissions.Warranties.View), new NavItem("Warranties", A("Index", "Warranties"), "bi-shield-check", M("/warranties"))),
            (P(Permissions.Licenses.View), new NavItem("Software licences", A("Index", "Licenses"), "bi-key", M("/licenses"))),
            (P(Permissions.Disposals.View), new NavItem("Disposals", A("Index", "Disposals"), "bi-recycle", M("/disposals"))));
        var md = P(Permissions.MasterData.View);
        Section("Organization",
            (md, new NavItem("Employees", Md("employees"), "bi-people", M("/master-data/employees"))),
            (md, new NavItem("Departments", Md("departments"), "bi-diagram-3", M("/master-data/departments"))),
            (md, new NavItem("Locations and buildings", Md("locations"), "bi-building", M("/master-data/locations", "/master-data/buildings", "/master-data/floors", "/master-data/rooms"))),
            (md, new NavItem("Vendors and manufacturers", Md("vendors"), "bi-truck", M("/master-data/vendors", "/master-data/manufacturers"))),
            (md, new NavItem("All master data", A("Index", "MasterData"), "bi-collection", M("/master-data"))));
        Section("Governance",
            (P(Permissions.Administration.AuditLogsView) && P(Permissions.Reports.View), new NavItem("Audit report", A("Run", "Reports", new { code = "audit" }), "bi-clock-history", M("/reports/audit"))),
            (P(Permissions.Reports.View), new NavItem("Compliance report", A("Run", "Reports", new { code = "compliance" }), "bi-clipboard-check", M("/reports/compliance"))),
            (P(Permissions.Reports.View), new NavItem("Risk assessment", A("Run", "Reports", new { code = "risk" }), "bi-exclamation-triangle", M("/reports/risk"))),
            (P(Permissions.Administration.AuditLogsView), new NavItem("Sign-in history", A("Index", "SecurityLog"), "bi-door-open", M("/SecurityLog"))));
        Section("Reporting and analytics",
            (P(Permissions.Reports.View), new NavItem("Reports centre", A("Index", "Reports"), "bi-file-earmark-bar-graph", M("/reports"))),
            (P(Permissions.Reports.View), new NavItem("Report history", A("History", "Reports"), "bi-journal-check", M("/reports/history"))));
        Section("Administration",
            (P(Permissions.Administration.UsersView), new NavItem("Users and roles", A("Index", "Users"), "bi-person-gear", M("/Users"))),
            (P(Permissions.Administration.RolesManage), new NavItem("Roles and permissions", A("Index", "Roles"), "bi-shield-lock", M("/admin/roles"))),
            (P(Permissions.Administration.RolesManage), new NavItem("Approval stages", A("Rules", "Approvals"), "bi-signpost-split", M("/approvals/rules"))),
            (user.IsPlatformAdmin(), new NavItem("Backup and restore", A("Index", "CompanyBackup"), "bi-hdd", M("/admin/backup"))),
            (P(Permissions.Administration.SystemSettings), new NavItem("Recycle bin", A("Index", "RecycleBin"), "bi-trash3", M("/recycle-bin"))),
            (P(Permissions.Administration.SystemSettings), new NavItem("Configuration transfer", A("Index", "ConfigurationTransfer"), "bi-arrow-left-right", M("/admin/configuration"))),
            (P(Permissions.CompanyAdmin.ExportDataRequest) || P(Permissions.CompanyAdmin.ExportDataApprove) || P(Permissions.CompanyAdmin.ExportDataDownload),
                new NavItem("Company data export", A("Index", "CompanyDataExport"), "bi-box-arrow-down", M("/company/data-export"))),
            (P(Permissions.Administration.NotificationsManage), new NavItem("E-mail and notifications", A("Index", "NotificationSettings"), "bi-envelope-at", M("/settings/notifications"))));
        Section("Platform",
            (user.IsPlatformAdmin(), new NavItem("Company directory", A("Index", "Platform"), "bi-buildings", M("/platform/companies"))),
            (user.IsPlatformAdmin(), new NavItem("Create company", A("Create", "Platform"), "bi-building-add", M("/platform/companies/new"))),
            (user.IsPlatformAdmin(), new NavItem("Users and company access", A("Index", "PlatformUsers"), "bi-people-fill", M("/platform/users"))),
            (user.IsPlatformAdmin(), new NavItem("Access review", A("Review", "PlatformUsers"), "bi-clipboard-check", M("/platform/access-review"))));

        // current page = the item with the longest matching prefix (so "Add new asset" beats "Asset register")
        var path = context.Request.Path.Value ?? "/";
        NavSection? currentSection = null;
        NavItem? currentItem = null;
        var best = 0;
        foreach (var s in sections)
            foreach (var i in s.Items)
                foreach (var m in i.Matches.Append(i.Url.Split('?')[0]))
                {
                    if (m.Length <= best || m.Contains('{', StringComparison.Ordinal) || !Matches(path, m)) continue;
                    best = m.Length;
                    currentSection = s;
                    currentItem = i;
                }
        var home = dashboard ? A("Index", "Dashboard") : A("Index", "Home", new { overview = true });
        return _cached = new NavModel(sections, currentSection, currentItem, home);
    }

    private static bool Matches(string path, string prefix) =>
        prefix.Length > 1 && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (path.Length == prefix.Length || path[prefix.Length] is '/' or '?');
}
