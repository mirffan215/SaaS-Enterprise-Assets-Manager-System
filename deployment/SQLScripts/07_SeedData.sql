/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 07 - Seed data (idempotent: inserts only what is missing and
   never overwrites values that administrators have changed).
   Variables: AdminUsername, AdminEmail, AdminDisplayName, AdminPasswordHash
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
-- Release 2.0: company-owned lists are seeded for company 1 (AMGH, created by script 10). Row-Level
-- Security limits every check below to that company; new companies get their copy from the setup wizard.
EXEC sp_set_session_context @key = N'CompanyId', @value = 1;
GO

/* ---------------------------------------------------- NUMBER SEQUENCES */
INSERT dbo.tblAssetNumberSequence (SequenceCode, Prefix, LastNumber, PaddingLength, Description, SortOrder)
SELECT v.Code, N'AMGH-IT', 0, 5, v.Descr, v.SortOrder FROM (VALUES
    (N'PC',   N'Desktop computers',            10),
    (N'LAP',  N'Laptops',                       20),
    (N'SRV',  N'Servers',                       30),
    (N'NET',  N'Network equipment',             40),
    (N'PHN',  N'Desk / IP phones',              50),
    (N'MOB',  N'Mobile devices',                60),
    (N'CCTV', N'CCTV and surveillance',         70),
    (N'STO',  N'Storage systems',               80),
    (N'LIC',  N'Software licenses',             90)
) v (Code, Descr, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblAssetNumberSequence s WHERE s.SequenceCode = v.Code);
GO

/* ---------------------------------------------------- CATEGORIES */
INSERT dbo.tblAssetCategory (CategoryCode, CategoryName, AssetNumberSequenceId, DefaultUsefulLifeMonths, RequiresHostname, SortOrder)
SELECT v.Code, v.Name, s.AssetNumberSequenceId, v.Life, v.Host, v.SortOrder FROM (VALUES
    (N'PC',   N'Desktop Computer',  N'PC',   48, 1, 10),
    (N'LAP',  N'Laptop',            N'LAP',  36, 1, 20),
    (N'SRV',  N'Server',            N'SRV',  60, 1, 30),
    (N'NET',  N'Network Device',    N'NET',  60, 1, 40),
    (N'PHN',  N'Telephony',         N'PHN',  60, 0, 50),
    (N'MOB',  N'Mobile Device',     N'MOB',  24, 0, 60),
    (N'CCTV', N'CCTV / Surveillance', N'CCTV', 60, 0, 70),
    (N'STO',  N'Storage',           N'STO',  60, 1, 80)
) v (Code, Name, Seq, Life, Host, SortOrder)
JOIN dbo.tblAssetNumberSequence s ON s.SequenceCode = v.Seq
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblAssetCategory c WHERE c.CategoryCode = v.Code);
GO

INSERT dbo.tblAssetSubCategory (AssetCategoryId, SubCategoryCode, SubCategoryName, SortOrder)
SELECT c.AssetCategoryId, v.Code, v.Name, v.SortOrder FROM (VALUES
    (N'PC',  N'TOWER',  N'Tower Desktop', 10), (N'PC',  N'SFF', N'Small Form Factor', 20),
    (N'PC',  N'AIO',    N'All-in-One', 30),    (N'PC',  N'WKS', N'Workstation', 40),
    (N'LAP', N'STD',    N'Standard Laptop', 10), (N'LAP', N'ULTRA', N'Ultrabook', 20),
    (N'LAP', N'MWS',    N'Mobile Workstation', 30),
    (N'SRV', N'RACK',   N'Rack Server', 10),   (N'SRV', N'BLADE', N'Blade Server', 20),
    (N'SRV', N'TOWER',  N'Tower Server', 30),  (N'SRV', N'HCI', N'Hyper-converged Node', 40),
    (N'NET', N'SWITCH', N'Switch', 10),        (N'NET', N'ROUTER', N'Router', 20),
    (N'NET', N'FW',     N'Firewall', 30),      (N'NET', N'AP', N'Wireless Access Point', 40),
    (N'NET', N'WLC',    N'Wireless Controller', 50),
    (N'PHN', N'IPPHONE', N'IP Desk Phone', 10), (N'PHN', N'CONF', N'Conference Phone', 20),
    (N'MOB', N'SMART',  N'Smartphone', 10),    (N'MOB', N'TAB', N'Tablet', 20),
    (N'CCTV', N'CAM',   N'IP Camera', 10),     (N'CCTV', N'NVR', N'Network Video Recorder', 20),
    (N'STO', N'NAS',    N'NAS', 10),           (N'STO', N'SAN', N'SAN Array', 20),
    (N'STO', N'EXT',    N'External Drive', 30), (N'STO', N'TAPE', N'Tape Library', 40)
) v (Cat, Code, Name, SortOrder)
JOIN dbo.tblAssetCategory c ON c.CategoryCode = v.Cat
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblAssetSubCategory s WHERE s.AssetCategoryId = c.AssetCategoryId AND s.SubCategoryCode = v.Code);
GO

/* ---------------------------------------------------- STATUSES */
INSERT dbo.tblAssetStatus (StatusCode, StatusName, IsAssignable, CountsAsInventory, IsTerminal, ColorHex, SortOrder)
SELECT v.* FROM (VALUES
    (N'IN_STOCK',  N'In Stock',          1, 1, 0, '#0D6EFD', 10),
    (N'RESERVED',  N'Reserved',          1, 1, 0, '#6610F2', 20),
    (N'ASSIGNED',  N'Assigned',          0, 1, 0, '#198754', 30),
    (N'IN_MAINT',  N'Under Maintenance', 0, 1, 0, '#FD7E14', 40),
    (N'MISSING',   N'Missing',           0, 1, 0, '#DC3545', 50),
    (N'STOLEN',    N'Stolen',            0, 1, 0, '#842029', 60),
    (N'RETIRED',   N'Retired',           0, 1, 0, '#6C757D', 70),
    (N'PEND_DISP', N'Pending Disposal',  0, 1, 0, '#FFC107', 80),
    (N'DISPOSED',  N'Disposed',          0, 0, 1, '#343A40', 90)
) v (Code, Name, Assignable, CountsInv, Terminal, Color, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblAssetStatus s WHERE s.StatusCode = v.Code);
GO

/* ---------------------------------------------------- CLASSIFICATION (align with the AMGH Information Classification scheme) */
INSERT dbo.tblClassification (ClassificationCode, ClassificationName, ClassificationLevel, HandlingRequirements, ColorHex, SortOrder)
SELECT v.* FROM (VALUES
    (N'PUBLIC',       N'Public',       1, N'No restrictions on disclosure.', '#198754', 10),
    (N'INTERNAL',     N'Internal',     2, N'For AMGH staff and authorised third parties only.', '#0D6EFD', 20),
    (N'CONFIDENTIAL', N'Confidential', 3, N'Need-to-know access; encryption required on portable devices and in transit.', '#FD7E14', 30),
    (N'RESTRICTED',   N'Restricted',   4, N'Named individuals only; encryption mandatory; secure disposal with certificate.', '#DC3545', 40)
) v (Code, Name, Lvl, Handling, Color, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblClassification c WHERE c.ClassificationCode = v.Code);
GO

/* ---------------------------------------------------- RISK LEVELS (5x5 likelihood x impact) */
INSERT dbo.tblRiskLevel (RiskLevelCode, RiskLevelName, MinScore, MaxScore, ReviewFrequencyMonths, ColorHex, SortOrder)
SELECT v.* FROM (VALUES
    (N'LOW',      N'Low',       1,  4, 12, '#198754', 10),
    (N'MEDIUM',   N'Medium',    5,  9,  6, '#FFC107', 20),
    (N'HIGH',     N'High',     10, 16,  3, '#FD7E14', 30),
    (N'CRITICAL', N'Critical', 17, 25,  1, '#DC3545', 40)
) v (Code, Name, MinS, MaxS, Review, Color, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblRiskLevel r WHERE r.RiskLevelCode = v.Code);
GO

/* ---------------------------------------------------- CRITICALITY (RTO / RPO in hours) */
INSERT dbo.tblCriticality (CriticalityCode, CriticalityName, CriticalityLevel, RtoHours, RpoHours, Description, SortOrder)
SELECT v.* FROM (VALUES
    (N'LOW',      N'Low',      1, 72, 48, N'Loss tolerated for several days.', 10),
    (N'MEDIUM',   N'Medium',   2, 24, 24, N'Restore within one business day.', 20),
    (N'HIGH',     N'High',     3,  8,  4, N'Supports key business processes.', 30),
    (N'CRITICAL', N'Critical', 4,  4,  1, N'Business-critical; covered by the Business Continuity Plan.', 40)
) v (Code, Name, Lvl, Rto, Rpo, Descr, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCriticality c WHERE c.CriticalityCode = v.Code);
GO

/* ---------------------------------------------------- CURRENCIES */
INSERT dbo.tblCurrency (CurrencyCode, CurrencyName, Symbol, DecimalPlaces, IsBaseCurrency)
SELECT v.* FROM (VALUES
    ('SAR', N'Saudi Riyal',       N'SAR', 2, 1),
    ('USD', N'US Dollar',         N'$',   2, 0),
    ('EUR', N'Euro',              N'€',   2, 0),
    ('GBP', N'Pound Sterling',    N'£',   2, 0),
    ('AED', N'UAE Dirham',        N'AED', 2, 0)
) v (Code, Name, Symbol, Dec, IsBase)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCurrency c WHERE c.CurrencyCode = v.Code);
GO

/* ---------------------------------------------------- DISPOSAL METHODS */
INSERT dbo.tblDisposalMethod (MethodCode, MethodName, RequiresDataSanitization, RequiresCertificate, Description, SortOrder)
SELECT v.* FROM (VALUES
    (N'DESTROY',  N'Physical Destruction',      1, 1, N'Shredding / degaussing by an approved provider.', 10),
    (N'RECYCLE',  N'Certified E-Waste Recycling', 1, 1, N'Approved recycler with certificate of destruction.', 20),
    (N'RESALE',   N'Resale',                     1, 0, N'Sold after verified data sanitisation.', 30),
    (N'DONATE',   N'Donation',                   1, 0, N'Donated after verified data sanitisation.', 40),
    (N'TRADEIN',  N'Vendor Trade-in',            1, 0, N'Returned to vendor against new purchase.', 50),
    (N'LEASERET', N'Lease Return',               1, 0, N'Returned to lessor at end of lease.', 60),
    (N'WRITEOFF', N'Write-off (Lost/Stolen)',    0, 0, N'Accounting write-off; no physical asset available.', 70)
) v (Code, Name, San, Cert, Descr, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblDisposalMethod d WHERE d.MethodCode = v.Code);
GO

/* ---------------------------------------------------- WARRANTY STATUSES (codes used by fn_GetWarrantyStateCode) */
INSERT dbo.tblWarrantyStatus (StatusCode, StatusName, SortOrder)
SELECT v.* FROM (VALUES
    (N'ACTIVE',   N'Active',        10),
    (N'EXPIRING', N'Expiring Soon', 20),
    (N'EXPIRED',  N'Expired',       30),
    (N'VOID',     N'Void / Cancelled', 40)
) v (Code, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblWarrantyStatus w WHERE w.StatusCode = v.Code);
GO

/* ---------------------------------------------------- MANUFACTURERS */
INSERT dbo.tblManufacturer (ManufacturerCode, ManufacturerName, Website)
SELECT v.* FROM (VALUES
    (N'DELL', N'Dell Technologies', N'https://www.dell.com'),
    (N'HP',   N'HP Inc.', N'https://www.hp.com'),
    (N'HPE',  N'Hewlett Packard Enterprise', N'https://www.hpe.com'),
    (N'LEN',  N'Lenovo', N'https://www.lenovo.com'),
    (N'APL',  N'Apple', N'https://www.apple.com'),
    (N'SAM',  N'Samsung', N'https://www.samsung.com'),
    (N'CSCO', N'Cisco', N'https://www.cisco.com'),
    (N'FTNT', N'Fortinet', N'https://www.fortinet.com'),
    (N'HUA',  N'Huawei', N'https://www.huawei.com'),
    (N'ARUBA', N'HPE Aruba Networking', N'https://www.arubanetworks.com'),
    (N'YEA',  N'Yealink', N'https://www.yealink.com'),
    (N'HIK',  N'Hikvision', N'https://www.hikvision.com'),
    (N'AXIS', N'Axis Communications', N'https://www.axis.com'),
    (N'SYN',  N'Synology', N'https://www.synology.com'),
    (N'NTAP', N'NetApp', N'https://www.netapp.com'),
    (N'MSFT', N'Microsoft', N'https://www.microsoft.com'),
    (N'SOPH', N'Sophos', N'https://www.sophos.com'),
    (N'VMW',  N'VMware by Broadcom', N'https://www.vmware.com')
) v (Code, Name, Web)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblManufacturer m WHERE m.ManufacturerCode = v.Code);
GO

/* ---------------------------------------------------- DEFAULT ORGANISATION / LOCATION (edit after deployment) */
IF NOT EXISTS (SELECT 1 FROM dbo.tblDepartment WHERE DepartmentCode = N'IT')
    INSERT dbo.tblDepartment (DepartmentCode, DepartmentName) VALUES (N'IT', N'Information Technology');
IF NOT EXISTS (SELECT 1 FROM dbo.tblLocation WHERE LocationCode = N'HQ')
    INSERT dbo.tblLocation (LocationCode, LocationName) VALUES (N'HQ', N'Head Office');
IF NOT EXISTS (SELECT 1 FROM dbo.tblBuilding b JOIN dbo.tblLocation l ON l.LocationId = b.LocationId
               WHERE l.LocationCode = N'HQ' AND b.BuildingCode = N'MAIN')
    INSERT dbo.tblBuilding (LocationId, BuildingCode, BuildingName)
    SELECT LocationId, N'MAIN', N'Main Building' FROM dbo.tblLocation WHERE LocationCode = N'HQ';
IF NOT EXISTS (SELECT 1 FROM dbo.tblFloor f JOIN dbo.tblBuilding b ON b.BuildingId = f.BuildingId
               JOIN dbo.tblLocation l ON l.LocationId = b.LocationId
               WHERE l.LocationCode = N'HQ' AND b.BuildingCode = N'MAIN' AND f.FloorCode = N'GF')
    INSERT dbo.tblFloor (BuildingId, FloorCode, FloorName, FloorNumber)
    SELECT b.BuildingId, N'GF', N'Ground Floor', 0 FROM dbo.tblBuilding b
    JOIN dbo.tblLocation l ON l.LocationId = b.LocationId WHERE l.LocationCode = N'HQ' AND b.BuildingCode = N'MAIN';
INSERT dbo.tblRoom (FloorId, RoomCode, RoomName, RoomType, IsSecureArea)
SELECT f.FloorId, v.Code, v.Name, v.RType, v.Secure
FROM (VALUES (N'SRV', N'Server Room', N'ServerRoom', 1), (N'ITSTORE', N'IT Store', N'Store', 1)) v (Code, Name, RType, Secure)
CROSS JOIN dbo.tblFloor f
JOIN dbo.tblBuilding b ON b.BuildingId = f.BuildingId AND b.BuildingCode = N'MAIN'
JOIN dbo.tblLocation l ON l.LocationId = b.LocationId AND l.LocationCode = N'HQ'
WHERE f.FloorCode = N'GF'
  AND NOT EXISTS (SELECT 1 FROM dbo.tblRoom r WHERE r.FloorId = f.FloorId AND r.RoomCode = v.Code);
GO

/* ---------------------------------------------------- PERMISSIONS (generated) */
INSERT dbo.tblPermission (PermissionCode, ModuleName, PermissionName, SortOrder)
SELECT v.Code, v.Module, v.Name, v.SortOrder FROM (VALUES
    (N'Dashboard.View', N'Dashboard', N'View dashboard', 10),
    (N'Assets.View', N'Assets', N'View assets', 20),
    (N'Assets.Create', N'Assets', N'Create assets', 30),
    (N'Assets.Edit', N'Assets', N'Edit assets', 40),
    (N'Assets.Delete', N'Assets', N'Delete (soft) assets', 50),
    (N'Assets.Import', N'Assets', N'Bulk import assets', 60),
    (N'Assets.Export', N'Assets', N'Bulk export assets', 70),
    (N'Assets.ViewSecurityDetails', N'Assets', N'View information security details', 80),
    (N'Assets.ViewFinancials', N'Assets', N'View procurement and financial details', 90),
    (N'Assignments.View', N'Assignments', N'View assignments', 100),
    (N'Assignments.Assign', N'Assignments', N'Assign assets', 110),
    (N'Assignments.Return', N'Assignments', N'Return assets', 120),
    (N'Movements.View', N'Movements', N'View movements', 130),
    (N'Movements.Request', N'Movements', N'Request movements', 140),
    (N'Movements.Approve', N'Movements', N'Approve or reject movements', 150),
    (N'Movements.Complete', N'Movements', N'Complete approved movements', 160),
    (N'Maintenance.View', N'Maintenance', N'View maintenance', 170),
    (N'Maintenance.Manage', N'Maintenance', N'Create and update maintenance', 180),
    (N'Audits.View', N'Audits', N'View physical audits', 190),
    (N'Audits.Perform', N'Audits', N'Perform physical audits', 200),
    (N'Disposals.View', N'Disposals', N'View disposals', 210),
    (N'Disposals.Request', N'Disposals', N'Request disposal', 220),
    (N'Disposals.Approve', N'Disposals', N'Approve or reject disposal', 230),
    (N'Disposals.Complete', N'Disposals', N'Complete disposal with evidence', 240),
    (N'Warranties.View', N'Warranties', N'View warranties', 250),
    (N'Warranties.Manage', N'Warranties', N'Manage warranties', 260),
    (N'Licenses.View', N'Licenses', N'View software licenses', 270),
    (N'Licenses.Manage', N'Licenses', N'Manage software licenses', 280),
    (N'Licenses.ViewKeys', N'Licenses', N'Reveal license keys', 290),
    (N'QrCodes.Generate', N'QrCodes', N'Generate QR codes', 300),
    (N'QrCodes.Print', N'QrCodes', N'Print QR labels', 310),
    (N'QrCodes.Reprint', N'QrCodes', N'Reprint QR labels', 320),
    (N'Documents.View', N'Documents', N'View attachments', 330),
    (N'Documents.Upload', N'Documents', N'Upload attachments', 340),
    (N'Documents.Delete', N'Documents', N'Delete attachments', 350),
    (N'Reports.View', N'Reports', N'View reports', 360),
    (N'Reports.Export', N'Reports', N'Export reports', 370),
    (N'MasterData.View', N'MasterData', N'View master data', 380),
    (N'MasterData.Manage', N'MasterData', N'Manage master data', 390),
    (N'Users.View', N'Administration', N'View users', 400),
    (N'Users.Manage', N'Administration', N'Manage users', 410),
    (N'Roles.Manage', N'Administration', N'Manage roles and permissions', 420),
    (N'AuditLogs.View', N'Administration', N'View audit and login logs', 430),
    (N'Notifications.Manage', N'Administration', N'Manage notification queue', 440),
    (N'System.Settings', N'Administration', N'Manage system settings', 450)
) v (Code, Module, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPermission p WHERE p.PermissionCode = v.Code);

/* ---------------------------------------------------- ROLES (generated) */
INSERT dbo.tblRole (RoleName, Description, IsSystemRole)
SELECT v.Name, v.Descr, 1 FROM (VALUES
    (N'SystemAdministrator', N'Full system access including users, roles and settings.'),
    (N'ITAssetManager', N'Owns the asset register: full lifecycle management and approvals.'),
    (N'ITTechnician', N'Day-to-day asset operations without approvals or deletion.'),
    (N'InformationSecurityOfficer', N'Oversight of classification, risk, disposal approvals and audit logs.'),
    (N'Auditor', N'Read-only access to the register, reports and audit logs for internal/external audit.'),
    (N'DepartmentManager', N'Views assets of the department and requests movements.'),
    (N'ReadOnly', N'Read-only access to the asset register.')
) v (Name, Descr)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblRole r WHERE r.RoleName = v.Name);

/* ---------------------------------------------------- ROLE PERMISSIONS (generated) */
INSERT dbo.tblRolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM (VALUES
    (N'SystemAdministrator', N'Dashboard.View'),
    (N'SystemAdministrator', N'Assets.View'),
    (N'SystemAdministrator', N'Assets.Create'),
    (N'SystemAdministrator', N'Assets.Edit'),
    (N'SystemAdministrator', N'Assets.Delete'),
    (N'SystemAdministrator', N'Assets.Import'),
    (N'SystemAdministrator', N'Assets.Export'),
    (N'SystemAdministrator', N'Assets.ViewSecurityDetails'),
    (N'SystemAdministrator', N'Assets.ViewFinancials'),
    (N'SystemAdministrator', N'Assignments.View'),
    (N'SystemAdministrator', N'Assignments.Assign'),
    (N'SystemAdministrator', N'Assignments.Return'),
    (N'SystemAdministrator', N'Movements.View'),
    (N'SystemAdministrator', N'Movements.Request'),
    (N'SystemAdministrator', N'Movements.Approve'),
    (N'SystemAdministrator', N'Movements.Complete'),
    (N'SystemAdministrator', N'Maintenance.View'),
    (N'SystemAdministrator', N'Maintenance.Manage'),
    (N'SystemAdministrator', N'Audits.View'),
    (N'SystemAdministrator', N'Audits.Perform'),
    (N'SystemAdministrator', N'Disposals.View'),
    (N'SystemAdministrator', N'Disposals.Request'),
    (N'SystemAdministrator', N'Disposals.Approve'),
    (N'SystemAdministrator', N'Disposals.Complete'),
    (N'SystemAdministrator', N'Warranties.View'),
    (N'SystemAdministrator', N'Warranties.Manage'),
    (N'SystemAdministrator', N'Licenses.View'),
    (N'SystemAdministrator', N'Licenses.Manage'),
    (N'SystemAdministrator', N'Licenses.ViewKeys'),
    (N'SystemAdministrator', N'QrCodes.Generate'),
    (N'SystemAdministrator', N'QrCodes.Print'),
    (N'SystemAdministrator', N'QrCodes.Reprint'),
    (N'SystemAdministrator', N'Documents.View'),
    (N'SystemAdministrator', N'Documents.Upload'),
    (N'SystemAdministrator', N'Documents.Delete'),
    (N'SystemAdministrator', N'Reports.View'),
    (N'SystemAdministrator', N'Reports.Export'),
    (N'SystemAdministrator', N'MasterData.View'),
    (N'SystemAdministrator', N'MasterData.Manage'),
    (N'SystemAdministrator', N'Users.View'),
    (N'SystemAdministrator', N'Users.Manage'),
    (N'SystemAdministrator', N'Roles.Manage'),
    (N'SystemAdministrator', N'AuditLogs.View'),
    (N'SystemAdministrator', N'Notifications.Manage'),
    (N'SystemAdministrator', N'System.Settings'),
    (N'ITAssetManager', N'Dashboard.View'),
    (N'ITAssetManager', N'Assets.View'),
    (N'ITAssetManager', N'Assets.Create'),
    (N'ITAssetManager', N'Assets.Edit'),
    (N'ITAssetManager', N'Assets.Delete'),
    (N'ITAssetManager', N'Assets.Import'),
    (N'ITAssetManager', N'Assets.Export'),
    (N'ITAssetManager', N'Assets.ViewSecurityDetails'),
    (N'ITAssetManager', N'Assets.ViewFinancials'),
    (N'ITAssetManager', N'Assignments.View'),
    (N'ITAssetManager', N'Assignments.Assign'),
    (N'ITAssetManager', N'Assignments.Return'),
    (N'ITAssetManager', N'Movements.View'),
    (N'ITAssetManager', N'Movements.Request'),
    (N'ITAssetManager', N'Movements.Approve'),
    (N'ITAssetManager', N'Movements.Complete'),
    (N'ITAssetManager', N'Maintenance.View'),
    (N'ITAssetManager', N'Maintenance.Manage'),
    (N'ITAssetManager', N'Audits.View'),
    (N'ITAssetManager', N'Audits.Perform'),
    (N'ITAssetManager', N'Disposals.View'),
    (N'ITAssetManager', N'Disposals.Request'),
    (N'ITAssetManager', N'Disposals.Approve'),
    (N'ITAssetManager', N'Disposals.Complete'),
    (N'ITAssetManager', N'Warranties.View'),
    (N'ITAssetManager', N'Warranties.Manage'),
    (N'ITAssetManager', N'Licenses.View'),
    (N'ITAssetManager', N'Licenses.Manage'),
    (N'ITAssetManager', N'Licenses.ViewKeys'),
    (N'ITAssetManager', N'QrCodes.Generate'),
    (N'ITAssetManager', N'QrCodes.Print'),
    (N'ITAssetManager', N'QrCodes.Reprint'),
    (N'ITAssetManager', N'Documents.View'),
    (N'ITAssetManager', N'Documents.Upload'),
    (N'ITAssetManager', N'Documents.Delete'),
    (N'ITAssetManager', N'Reports.View'),
    (N'ITAssetManager', N'Reports.Export'),
    (N'ITAssetManager', N'MasterData.View'),
    (N'ITAssetManager', N'MasterData.Manage'),
    (N'ITTechnician', N'Dashboard.View'),
    (N'ITTechnician', N'Assets.View'),
    (N'ITTechnician', N'Assets.Create'),
    (N'ITTechnician', N'Assets.Edit'),
    (N'ITTechnician', N'Assets.ViewSecurityDetails'),
    (N'ITTechnician', N'Assignments.View'),
    (N'ITTechnician', N'Assignments.Assign'),
    (N'ITTechnician', N'Assignments.Return'),
    (N'ITTechnician', N'Movements.View'),
    (N'ITTechnician', N'Movements.Request'),
    (N'ITTechnician', N'Movements.Complete'),
    (N'ITTechnician', N'Maintenance.View'),
    (N'ITTechnician', N'Maintenance.Manage'),
    (N'ITTechnician', N'Audits.View'),
    (N'ITTechnician', N'Audits.Perform'),
    (N'ITTechnician', N'Disposals.View'),
    (N'ITTechnician', N'Disposals.Request'),
    (N'ITTechnician', N'Warranties.View'),
    (N'ITTechnician', N'Licenses.View'),
    (N'ITTechnician', N'QrCodes.Generate'),
    (N'ITTechnician', N'QrCodes.Print'),
    (N'ITTechnician', N'Documents.View'),
    (N'ITTechnician', N'Documents.Upload'),
    (N'ITTechnician', N'MasterData.View'),
    (N'InformationSecurityOfficer', N'Assets.Export'),
    (N'InformationSecurityOfficer', N'Assets.View'),
    (N'InformationSecurityOfficer', N'Assets.ViewFinancials'),
    (N'InformationSecurityOfficer', N'Assets.ViewSecurityDetails'),
    (N'InformationSecurityOfficer', N'Assignments.View'),
    (N'InformationSecurityOfficer', N'AuditLogs.View'),
    (N'InformationSecurityOfficer', N'Audits.View'),
    (N'InformationSecurityOfficer', N'Dashboard.View'),
    (N'InformationSecurityOfficer', N'Disposals.Approve'),
    (N'InformationSecurityOfficer', N'Disposals.View'),
    (N'InformationSecurityOfficer', N'Documents.View'),
    (N'InformationSecurityOfficer', N'Licenses.View'),
    (N'InformationSecurityOfficer', N'Maintenance.View'),
    (N'InformationSecurityOfficer', N'MasterData.View'),
    (N'InformationSecurityOfficer', N'Movements.View'),
    (N'InformationSecurityOfficer', N'Reports.Export'),
    (N'InformationSecurityOfficer', N'Reports.View'),
    (N'InformationSecurityOfficer', N'Warranties.View'),
    (N'Auditor', N'Assets.Export'),
    (N'Auditor', N'Assets.View'),
    (N'Auditor', N'Assets.ViewFinancials'),
    (N'Auditor', N'Assets.ViewSecurityDetails'),
    (N'Auditor', N'Assignments.View'),
    (N'Auditor', N'AuditLogs.View'),
    (N'Auditor', N'Audits.View'),
    (N'Auditor', N'Dashboard.View'),
    (N'Auditor', N'Disposals.View'),
    (N'Auditor', N'Documents.View'),
    (N'Auditor', N'Licenses.View'),
    (N'Auditor', N'Maintenance.View'),
    (N'Auditor', N'MasterData.View'),
    (N'Auditor', N'Movements.View'),
    (N'Auditor', N'Reports.Export'),
    (N'Auditor', N'Reports.View'),
    (N'Auditor', N'Warranties.View'),
    (N'DepartmentManager', N'Dashboard.View'),
    (N'DepartmentManager', N'Assets.View'),
    (N'DepartmentManager', N'Assignments.View'),
    (N'DepartmentManager', N'Movements.View'),
    (N'DepartmentManager', N'Movements.Request'),
    (N'DepartmentManager', N'Reports.View'),
    (N'ReadOnly', N'Dashboard.View'),
    (N'ReadOnly', N'Assets.View')
) v (RoleName, PermissionCode)
JOIN dbo.tblRole r ON r.RoleName = v.RoleName
JOIN dbo.tblPermission p ON p.PermissionCode = v.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblRolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
/* ---------------------------------------------------- INITIAL ADMINISTRATOR
   Password hash is produced by deployment/PowerShell/Deploy-Database.ps1
   (ASP.NET Core Identity v3 format, PBKDF2-HMAC-SHA512, 100,000 iterations).
   MustChangePassword = 1 forces a new password at first sign-in. */
IF NOT EXISTS (SELECT 1 FROM dbo.tblUser WHERE Username = N'$(AdminUsername)')
BEGIN
    INSERT dbo.tblUser (Username, Email, DisplayName, PasswordHash, IsActive, MustChangePassword, PasswordChangedDate)
    VALUES (N'$(AdminUsername)', N'$(AdminEmail)', N'$(AdminDisplayName)', N'$(AdminPasswordHash)', 1, 1, SYSUTCDATETIME());

    INSERT dbo.tblUserPasswordHistory (UserId, PasswordHash)
    SELECT UserId, PasswordHash FROM dbo.tblUser WHERE Username = N'$(AdminUsername)';
END

INSERT dbo.tblCompanyUser (CompanyId, UserId, IsDefault)
SELECT 1, u.UserId, 1 FROM dbo.tblUser u
WHERE u.Username = N'$(AdminUsername)'
  AND NOT EXISTS (SELECT 1 FROM dbo.tblCompanyUser cu WHERE cu.CompanyId = 1 AND cu.UserId = u.UserId);
UPDATE dbo.tblUser SET IsPlatformAdmin = 1 WHERE Username = N'$(AdminUsername)' AND IsPlatformAdmin = 0;

INSERT dbo.tblUserRole (UserId, RoleId)
SELECT u.UserId, r.RoleId
FROM dbo.tblUser u CROSS JOIN dbo.tblRole r
WHERE u.Username = N'$(AdminUsername)' AND r.RoleName = N'SystemAdministrator'
  AND NOT EXISTS (SELECT 1 FROM dbo.tblUserRole ur WHERE ur.UserId = u.UserId AND ur.RoleId = r.RoleId);
GO
PRINT 'Script 07 completed.';
GO
