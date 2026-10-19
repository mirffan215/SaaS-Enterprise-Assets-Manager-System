/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 04 - Views (reporting, dashboard, compliance)
   All views exclude soft-deleted rows.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Flattened asset register - the main read model for grids, exports and reports. */
CREATE OR ALTER VIEW dbo.vw_AssetDetails
AS
SELECT
    a.AssetId, a.AssetNumber, a.AssetName,
    a.AssetCategoryId, c.CategoryCode, c.CategoryName,
    a.AssetSubCategoryId, sc.SubCategoryName,
    a.AssetStatusId, s.StatusCode, s.StatusName, s.IsAssignable, s.CountsAsInventory,
    a.ManufacturerId, m.ManufacturerName, a.ModelName, a.SerialNumber, a.LegacyAssetTag,
    a.Hostname, a.IpAddress, a.MacAddress, a.OperatingSystem, a.OsVersion,
    a.Processor, a.RamGb, a.StorageGb, a.FirmwareVersion, a.ImeiNumber, a.PhoneNumber,
    a.VendorId, v.VendorName, a.PurchaseOrderNumber, a.InvoiceNumber, a.PurchaseDate,
    a.PurchaseCost, a.CurrencyId, cur.CurrencyCode, a.UsefulLifeMonths, a.SalvageValue,
    dbo.fn_GetAssetAgeMonths(a.PurchaseDate, CAST(SYSUTCDATETIME() AS DATE)) AS AgeMonths,
    dbo.fn_GetAssetBookValue(a.PurchaseCost, a.SalvageValue, a.UsefulLifeMonths, a.PurchaseDate,
                             CAST(SYSUTCDATETIME() AS DATE)) AS BookValue,
    a.AssignedEmployeeId, e.EmployeeNumber AS AssignedEmployeeNumber, e.FullName AS AssignedEmployeeName,
    e.Email AS AssignedEmployeeEmail,
    a.DepartmentId, d.DepartmentCode, d.DepartmentName,
    a.LocationId, l.LocationName, a.BuildingId, b.BuildingName, a.FloorId, f.FloorName,
    a.RoomId, r.RoomName,
    a.AssetOwnerEmployeeId, o.FullName AS AssetOwnerName,
    a.ClassificationId, cl.ClassificationName, cl.ClassificationLevel,
    a.CriticalityId, cr.CriticalityName, cr.CriticalityLevel, cr.RtoHours, cr.RpoHours,
    a.RiskLevelId, rl.RiskLevelName,
    a.StoresSensitiveData, a.IsDiskEncrypted, a.EndpointProtectionInstalled, a.IsMdmManaged,
    a.BackupRequired, a.LastSecurityReviewDate,
    a.CommissionDate, a.EndOfSupportDate, a.EndOfLifeDate, a.NextAuditDueDate, a.DecommissionDate,
    w.WarrantyEndDate, w.WarrantyStatusName,
    CASE WHEN w.WarrantyEndDate IS NULL THEN NULL
         ELSE DATEDIFF(DAY, CAST(SYSUTCDATETIME() AS DATE), w.WarrantyEndDate) END AS WarrantyDaysRemaining,
    qr.QrVersion AS CurrentQrVersion,
    a.CreatedBy, a.CreatedDate, a.ModifiedBy, a.ModifiedDate
FROM dbo.tblAsset a
JOIN dbo.tblAssetCategory c       ON c.AssetCategoryId = a.AssetCategoryId
JOIN dbo.tblAssetStatus s         ON s.AssetStatusId = a.AssetStatusId
LEFT JOIN dbo.tblAssetSubCategory sc ON sc.AssetSubCategoryId = a.AssetSubCategoryId
LEFT JOIN dbo.tblManufacturer m   ON m.ManufacturerId = a.ManufacturerId
LEFT JOIN dbo.tblVendor v         ON v.VendorId = a.VendorId
LEFT JOIN dbo.tblCurrency cur     ON cur.CurrencyId = a.CurrencyId
LEFT JOIN dbo.tblEmployee e       ON e.EmployeeId = a.AssignedEmployeeId
LEFT JOIN dbo.tblEmployee o       ON o.EmployeeId = a.AssetOwnerEmployeeId
LEFT JOIN dbo.tblDepartment d     ON d.DepartmentId = a.DepartmentId
LEFT JOIN dbo.tblLocation l       ON l.LocationId = a.LocationId
LEFT JOIN dbo.tblBuilding b       ON b.BuildingId = a.BuildingId
LEFT JOIN dbo.tblFloor f          ON f.FloorId = a.FloorId
LEFT JOIN dbo.tblRoom r           ON r.RoomId = a.RoomId
LEFT JOIN dbo.tblClassification cl ON cl.ClassificationId = a.ClassificationId
LEFT JOIN dbo.tblCriticality cr   ON cr.CriticalityId = a.CriticalityId
LEFT JOIN dbo.tblRiskLevel rl     ON rl.RiskLevelId = a.RiskLevelId
OUTER APPLY (
    SELECT TOP (1) wa.EndDate AS WarrantyEndDate, ws.StatusName AS WarrantyStatusName
    FROM dbo.tblWarranty wa
    JOIN dbo.tblWarrantyStatus ws ON ws.WarrantyStatusId = wa.WarrantyStatusId
    WHERE wa.AssetId = a.AssetId AND wa.IsDeleted = 0
    ORDER BY wa.EndDate DESC
) w
OUTER APPLY (
    SELECT TOP (1) q.QrVersion FROM dbo.tblAssetQRCode q
    WHERE q.AssetId = a.AssetId AND q.IsCurrent = 1 AND q.IsDeleted = 0
) qr
WHERE a.IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_CurrentAssignments
AS
SELECT aa.AssetAssignmentId, aa.AssetId, a.AssetNumber, a.AssetName, c.CategoryName,
       aa.EmployeeId, e.EmployeeNumber, e.FullName AS EmployeeName, e.Email AS EmployeeEmail,
       aa.DepartmentId, d.DepartmentName, aa.AssignmentType, aa.AssignedDate, aa.ExpectedReturnDate,
       CASE WHEN aa.ExpectedReturnDate < CAST(SYSUTCDATETIME() AS DATE) THEN 1 ELSE 0 END AS IsOverdue,
       aa.AcknowledgementReceived, aa.AcknowledgementDate, aa.AssignedBy
FROM dbo.tblAssetAssignment aa
JOIN dbo.tblAsset a ON a.AssetId = aa.AssetId AND a.IsDeleted = 0
JOIN dbo.tblAssetCategory c ON c.AssetCategoryId = a.AssetCategoryId
LEFT JOIN dbo.tblEmployee e ON e.EmployeeId = aa.EmployeeId
LEFT JOIN dbo.tblDepartment d ON d.DepartmentId = aa.DepartmentId
WHERE aa.IsCurrent = 1 AND aa.IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_AssetCountByCategory
AS
SELECT c.AssetCategoryId, c.CategoryCode, c.CategoryName,
       COUNT(a.AssetId) AS TotalAssets,
       SUM(CASE WHEN a.AssignedEmployeeId IS NOT NULL THEN 1 ELSE 0 END) AS AssignedAssets,
       SUM(ISNULL(a.PurchaseCost, 0)) AS TotalPurchaseCost
FROM dbo.tblAssetCategory c
LEFT JOIN (dbo.tblAsset a JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId AND s.CountsAsInventory = 1)
       ON a.AssetCategoryId = c.AssetCategoryId AND a.IsDeleted = 0
WHERE c.IsDeleted = 0
GROUP BY c.AssetCategoryId, c.CategoryCode, c.CategoryName;
GO

CREATE OR ALTER VIEW dbo.vw_AssetCountByStatus
AS
SELECT s.AssetStatusId, s.StatusCode, s.StatusName, s.ColorHex, s.SortOrder, COUNT(a.AssetId) AS TotalAssets
FROM dbo.tblAssetStatus s
LEFT JOIN dbo.tblAsset a ON a.AssetStatusId = s.AssetStatusId AND a.IsDeleted = 0
WHERE s.IsDeleted = 0
GROUP BY s.AssetStatusId, s.StatusCode, s.StatusName, s.ColorHex, s.SortOrder;
GO

CREATE OR ALTER VIEW dbo.vw_AssetCountByDepartment
AS
SELECT d.DepartmentId, d.DepartmentCode, d.DepartmentName, COUNT(a.AssetId) AS TotalAssets
FROM dbo.tblDepartment d
LEFT JOIN (dbo.tblAsset a JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId AND s.CountsAsInventory = 1)
       ON a.DepartmentId = d.DepartmentId AND a.IsDeleted = 0
WHERE d.IsDeleted = 0
GROUP BY d.DepartmentId, d.DepartmentCode, d.DepartmentName;
GO

/* Warranties ending within the next 90 days or already expired on in-inventory assets. */
CREATE OR ALTER VIEW dbo.vw_WarrantyExpiring
AS
SELECT w.WarrantyId, w.AssetId, a.AssetNumber, a.AssetName, w.WarrantyType, w.ContractNumber,
       v.VendorName AS ProviderName, w.StartDate, w.EndDate,
       DATEDIFF(DAY, CAST(SYSUTCDATETIME() AS DATE), w.EndDate) AS DaysRemaining,
       cr.CriticalityName
FROM dbo.tblWarranty w
JOIN dbo.tblAsset a ON a.AssetId = w.AssetId AND a.IsDeleted = 0
JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId AND s.CountsAsInventory = 1
LEFT JOIN dbo.tblVendor v ON v.VendorId = w.ProviderVendorId
LEFT JOIN dbo.tblCriticality cr ON cr.CriticalityId = a.CriticalityId
WHERE w.IsDeleted = 0
  AND w.EndDate <= DATEADD(DAY, 90, CAST(SYSUTCDATETIME() AS DATE))
  AND NOT EXISTS (SELECT 1 FROM dbo.tblWarranty w2
                  WHERE w2.AssetId = w.AssetId AND w2.IsDeleted = 0 AND w2.EndDate > w.EndDate);
GO

/* Seat usage vs entitlement; IsOverAllocated should always be 0 (trigger-enforced). */
CREATE OR ALTER VIEW dbo.vw_LicenseCompliance
AS
SELECT l.SoftwareLicenseId, l.LicenseNumber, l.SoftwareName, l.SoftwareVersion, l.LicenseType,
       m.ManufacturerName AS PublisherName, l.TotalSeats,
       ISNULL(u.UsedSeats, 0) AS UsedSeats,
       CASE WHEN l.TotalSeats IS NULL THEN NULL ELSE l.TotalSeats - ISNULL(u.UsedSeats, 0) END AS AvailableSeats,
       CASE WHEN l.TotalSeats IS NOT NULL AND ISNULL(u.UsedSeats, 0) > l.TotalSeats THEN 1 ELSE 0 END AS IsOverAllocated,
       l.ExpiryDate,
       CASE WHEN l.ExpiryDate IS NULL THEN NULL
            ELSE DATEDIFF(DAY, CAST(SYSUTCDATETIME() AS DATE), l.ExpiryDate) END AS DaysToExpiry,
       CASE WHEN l.ExpiryDate IS NULL THEN N'NoExpiry'
            WHEN l.ExpiryDate < CAST(SYSUTCDATETIME() AS DATE) THEN N'Expired'
            WHEN l.ExpiryDate <= DATEADD(DAY, l.RenewalReminderDays, CAST(SYSUTCDATETIME() AS DATE)) THEN N'RenewalDue'
            ELSE N'Active' END AS ComplianceState,
       l.IsActive
FROM dbo.tblSoftwareLicense l
LEFT JOIN dbo.tblManufacturer m ON m.ManufacturerId = l.PublisherManufacturerId
OUTER APPLY (SELECT COUNT(*) AS UsedSeats FROM dbo.tblSoftwareLicenseAllocation sa
             WHERE sa.SoftwareLicenseId = l.SoftwareLicenseId AND sa.IsCurrent = 1 AND sa.IsDeleted = 0) u
WHERE l.IsDeleted = 0;
GO

/* Single-row KPI source for the executive dashboard. */
CREATE OR ALTER VIEW dbo.vw_DashboardKpi
AS
SELECT
    ISNULL(k.TotalAssets, 0)                AS TotalAssets,
    ISNULL(k.AssignedAssets, 0)             AS AssignedAssets,
    ISNULL(k.UnassignedAssets, 0)           AS UnassignedAssets,
    ISNULL(k.MaintenanceAssets, 0)          AS MaintenanceAssets,
    ISNULL(k.PendingDisposalAssets, 0)      AS PendingDisposalAssets,
    ISNULL(k.DisposedAssets, 0)             AS DisposedAssets,
    ISNULL(k.MissingAssets, 0)              AS MissingAssets,
    ISNULL(k.OverdueAuditAssets, 0)         AS OverdueAuditAssets,
    ISNULL(k.HighRiskAssets, 0)             AS HighRiskAssets,
    ISNULL(k.UnencryptedSensitiveAssets, 0) AS UnencryptedSensitiveAssets,
    ISNULL(k.TotalPurchaseCost, 0)          AS TotalPurchaseCost,
    (SELECT COUNT(*) FROM dbo.vw_WarrantyExpiring WHERE DaysRemaining BETWEEN 0 AND 30) AS WarrantiesExpiring30Days,
    (SELECT COUNT(*) FROM dbo.vw_WarrantyExpiring WHERE DaysRemaining < 0)              AS WarrantiesExpired,
    (SELECT COUNT(*) FROM dbo.vw_LicenseCompliance
      WHERE IsActive = 1 AND ComplianceState IN (N'RenewalDue', N'Expired'))           AS LicensesNeedingAttention,
    (SELECT COUNT(*) FROM dbo.tblAssetMovement
      WHERE IsDeleted = 0 AND MovementStatus = N'Requested')                           AS PendingMovementApprovals,
    (SELECT COUNT(*) FROM dbo.tblAssetDisposal
      WHERE IsDeleted = 0 AND DisposalStatus = N'Requested')                           AS PendingDisposalApprovals
FROM
(
    SELECT
        SUM(CASE WHEN s.CountsAsInventory = 1 THEN 1 ELSE 0 END) AS TotalAssets,
        SUM(CASE WHEN s.CountsAsInventory = 1 AND a.AssignedEmployeeId IS NOT NULL THEN 1 ELSE 0 END) AS AssignedAssets,
        SUM(CASE WHEN s.IsAssignable = 1 AND a.AssignedEmployeeId IS NULL THEN 1 ELSE 0 END) AS UnassignedAssets,
        SUM(CASE WHEN s.StatusCode = N'IN_MAINT' THEN 1 ELSE 0 END) AS MaintenanceAssets,
        SUM(CASE WHEN s.StatusCode = N'PEND_DISP' THEN 1 ELSE 0 END) AS PendingDisposalAssets,
        SUM(CASE WHEN s.StatusCode = N'DISPOSED' THEN 1 ELSE 0 END) AS DisposedAssets,
        SUM(CASE WHEN s.StatusCode IN (N'MISSING', N'STOLEN') THEN 1 ELSE 0 END) AS MissingAssets,
        SUM(CASE WHEN s.CountsAsInventory = 1 AND a.NextAuditDueDate < CAST(SYSUTCDATETIME() AS DATE) THEN 1 ELSE 0 END) AS OverdueAuditAssets,
        SUM(CASE WHEN s.CountsAsInventory = 1 AND rl.RiskLevelCode IN (N'HIGH', N'CRITICAL') THEN 1 ELSE 0 END) AS HighRiskAssets,
        SUM(CASE WHEN s.CountsAsInventory = 1 AND a.StoresSensitiveData = 1 AND a.IsDiskEncrypted = 0 THEN 1 ELSE 0 END) AS UnencryptedSensitiveAssets,
        SUM(CASE WHEN s.CountsAsInventory = 1 THEN ISNULL(a.PurchaseCost, 0) ELSE 0 END) AS TotalPurchaseCost
    FROM dbo.tblAsset a
    JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId
    LEFT JOIN dbo.tblRiskLevel rl ON rl.RiskLevelId = a.RiskLevelId
    WHERE a.IsDeleted = 0
) k;
GO

CREATE OR ALTER VIEW dbo.vw_UserEffectivePermissions
AS
SELECT u.UserId, u.Username, r.RoleName, p.PermissionCode, p.ModuleName
FROM dbo.tblUser u
JOIN dbo.tblUserRole ur       ON ur.UserId = u.UserId AND ur.IsDeleted = 0
JOIN dbo.tblRole r            ON r.RoleId = ur.RoleId AND r.IsDeleted = 0 AND r.IsActive = 1
JOIN dbo.tblRolePermission rp ON rp.RoleId = r.RoleId AND rp.IsDeleted = 0
JOIN dbo.tblPermission p      ON p.PermissionId = rp.PermissionId AND p.IsDeleted = 0
WHERE u.IsDeleted = 0 AND u.IsActive = 1;
GO
PRINT 'Script 04 completed.';
GO
