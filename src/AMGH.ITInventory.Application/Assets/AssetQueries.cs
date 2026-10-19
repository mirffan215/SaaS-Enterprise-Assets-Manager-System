using AMGH.ITInventory.Domain.Enums;
using System.Linq.Expressions;
using AMGH.ITInventory.Domain.Constants;
using AMGH.ITInventory.Domain.Entities;

namespace AMGH.ITInventory.Application.Assets;

/// <summary>Search, sort and list projection for assets (translated to SQL by EF Core).</summary>
internal static class AssetQueries
{
    public static IQueryable<Asset> Filter(IQueryable<Asset> q, AssetSearchCriteria c)
    {
        if (!string.IsNullOrWhiteSpace(c.SearchText))
        {
            var t = c.SearchText.Trim();
            q = q.Where(a => a.AssetNumber.Contains(t) || a.AssetName.Contains(t)
                             || (a.SerialNumber != null && a.SerialNumber.Contains(t))
                             || (a.Hostname != null && a.Hostname.Contains(t))
                             || (a.ModelName != null && a.ModelName.Contains(t))
                             || (a.LegacyAssetTag != null && a.LegacyAssetTag.Contains(t))
                             || (a.IpAddress != null && a.IpAddress.Contains(t))
                             || (a.AssignedEmployee != null && (a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName).Contains(t))
                             || (a.AssignedEmployee != null && a.AssignedEmployee.EmployeeNumber.Contains(t)));
        }
        if (c.CategoryId is { } cat) q = q.Where(a => a.AssetCategoryId == cat);
        if (c.StatusId is { } st) q = q.Where(a => a.AssetStatusId == st);
        if (c.DepartmentId is { } dep) q = q.Where(a => a.DepartmentId == dep);
        if (c.LocationId is { } loc) q = q.Where(a => a.LocationId == loc);
        if (c.ManufacturerId is { } man) q = q.Where(a => a.ManufacturerId == man);
        if (c.ClassificationId is { } cls) q = q.Where(a => a.ClassificationId == cls);
        if (c.CriticalityId is { } cri) q = q.Where(a => a.CriticalityId == cri);
        if (c.OnlyUnassigned) q = q.Where(a => a.AssignedEmployeeId == null && a.AssetStatus.IsAssignable);
        if (c.BuildingId is { } bld) q = q.Where(a => a.BuildingId == bld);
        if (c.RiskLevelId is { } rl) q = q.Where(a => a.RiskLevelId == rl);
        if (c.Focus is { Length: > 0 } focus) q = ApplyFocus(q, focus, c.Today);
        if (!c.IncludeDisposed && c.StatusId is null) q = q.Where(a => a.AssetStatus.StatusCode != AssetStatusCodes.Disposed);
        return q;
    }

    /// <summary>The KPI definitions (dashboard cards and their drill-down lists use exactly these conditions).</summary>
    public static IQueryable<Asset> ApplyFocus(IQueryable<Asset> q, string focus, DateOnly today)
    {
        var soon = today.AddDays(AssetFocus.ExpiringDays);
        return focus switch
        {
            AssetFocus.Unassigned => q.Where(a => a.AssetStatus.StatusCode == AssetStatusCodes.InStock || a.AssetStatus.StatusCode == "RESERVED"),
            AssetFocus.Maintenance => q.Where(a => a.AssetStatus.StatusCode == AssetStatusCodes.UnderMaintenance),
            AssetFocus.Repair => q.Where(a => a.MaintenanceRecords.Any(m => !m.IsDeleted && m.MaintenanceType == MaintenanceType.Corrective
                && (m.MaintenanceStatus == MaintenanceStatus.Scheduled || m.MaintenanceStatus == MaintenanceStatus.InProgress))),
            AssetFocus.MissingQr => q.Where(a => !a.QrCodes.Any(qr => qr.IsCurrent)),
            AssetFocus.WarrantyExpired => q.Where(a => a.Warranties.Any(w => !w.IsDeleted)
                && a.Warranties.Where(w => !w.IsDeleted).Max(w => w.EndDate) < today),
            AssetFocus.WarrantyExpiring => q.Where(a => a.Warranties.Any(w => !w.IsDeleted)
                && a.Warranties.Where(w => !w.IsDeleted).Max(w => w.EndDate) >= today
                && a.Warranties.Where(w => !w.IsDeleted).Max(w => w.EndDate) <= soon),
            AssetFocus.HighRisk => q.Where(a => a.RiskLevel != null && (a.RiskLevel.RiskLevelCode == "HIGH" || a.RiskLevel.RiskLevelCode == "CRITICAL")),
            AssetFocus.Critical => q.Where(a => a.Criticality != null && a.Criticality.CriticalityCode == "CRITICAL"),
            AssetFocus.Lost => q.Where(a => a.AssetStatus.StatusCode == "MISSING" || a.AssetStatus.StatusCode == "STOLEN"),
            AssetFocus.VerificationOverdue => q.Where(a => a.NextAuditDueDate != null && a.NextAuditDueDate < today),
            _ => q
        };
    }

    public static IQueryable<Asset> Sort(IQueryable<Asset> q, string? sort, bool desc)
    {
        IOrderedQueryable<Asset> ordered = (sort ?? "number") switch
        {
            "name" => desc ? q.OrderByDescending(a => a.AssetName) : q.OrderBy(a => a.AssetName),
            "category" => desc ? q.OrderByDescending(a => a.AssetCategory.CategoryName) : q.OrderBy(a => a.AssetCategory.CategoryName),
            "status" => desc ? q.OrderByDescending(a => a.AssetStatus.SortOrder) : q.OrderBy(a => a.AssetStatus.SortOrder),
            "assignedTo" => desc
                ? q.OrderByDescending(a => a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName)
                : q.OrderBy(a => a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName),
            "location" => desc
                ? q.OrderByDescending(a => a.Location == null ? null : a.Location.LocationName)
                : q.OrderBy(a => a.Location == null ? null : a.Location.LocationName),
            "purchaseDate" => desc ? q.OrderByDescending(a => a.PurchaseDate) : q.OrderBy(a => a.PurchaseDate),
            _ => desc ? q.OrderByDescending(a => a.AssetNumber) : q.OrderBy(a => a.AssetNumber)
        };
        return ordered.ThenBy(a => a.AssetId);
    }

    public static readonly Expression<Func<Asset, AssetListItem>> ToListItem = a => new AssetListItem(
        a.AssetId,
        a.AssetNumber,
        a.AssetName,
        a.AssetCategory.CategoryName,
        a.AssetStatus.StatusCode,
        a.AssetStatus.StatusName,
        a.AssetStatus.ColorHex,
        a.Manufacturer == null ? null : a.Manufacturer.ManufacturerName,
        a.ModelName,
        a.SerialNumber,
        a.Hostname,
        a.AssignedEmployee == null ? null : a.AssignedEmployee.FirstName + " " + a.AssignedEmployee.LastName,
        a.Department == null ? null : a.Department.DepartmentName,
        a.Location == null ? null : a.Location.LocationName,
        a.Classification == null ? null : a.Classification.ClassificationName,
        a.PurchaseDate,
        a.RiskLevel == null ? null : a.RiskLevel.RiskLevelName,
        a.Criticality == null ? null : a.Criticality.CriticalityName,
        a.NextAuditDueDate,
        a.Warranties.Where(w => !w.IsDeleted).Max(w => (DateOnly?)w.EndDate));
}
