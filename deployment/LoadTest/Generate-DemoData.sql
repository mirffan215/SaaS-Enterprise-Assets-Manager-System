/* =====================================================================
   Demo tenant data (sandbox), adapted from Generate-LoadData.sql.
   Fills the "demo" tenant database with a small, realistic enterprise estate instead of load-test volumes:
     * 3 sites, 8 departments, 40 employees (names are fictitious, e-mail addresses end in .invalid)
     * 120 hardware assets: 40 laptops, 14 desktops, 18 servers, 24 network devices (switches, firewalls, access points),
       16 printers, 8 storage systems; purchase dates over the last 5 years
     * lifecycle states: In Use (assigned), In Stock, Reserved, Under Maintenance, Pending Disposal (with a disposal request), Retired
     * warranties on every asset still in service: a fifth expired, a fifth ending in about 30 days, the rest valid for months
     * maintenance jobs (scheduled, in progress, completed) and a few audit verifications
   Run on the demo tenant database only (the script refuses any other name). Variables: DatabaseName, CompanyCode.
   Idempotent: when the demo rows already exist nothing is added. Everything created is marked CreatedBy = DEMOSEED.
   ===================================================================== */
:on error exit
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() NOT LIKE N'%demo%'
    THROW 53100, N'Refused: the demo data generator runs only on a database whose name contains "demo".', 1;
GO
DECLARE @CompanyId INT = (SELECT CompanyId FROM dbo.tblCompany WHERE CompanyCode = N'$(CompanyCode)');
IF @CompanyId IS NULL THROW 53101, N'Company code not found.', 1;
IF EXISTS (SELECT 1 FROM dbo.tblAsset WHERE CreatedBy = N'DEMOSEED')
BEGIN
    PRINT 'Demo data already present - nothing to do.';
    RETURN;
END
DECLARE @Prefix NVARCHAR(20) = (SELECT AssetNumberPrefix FROM dbo.tblCompany WHERE CompanyId = @CompanyId);
DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);

-- act inside the company (Row-Level Security and the CompanyId defaults use this)
EXEC sys.sp_set_session_context @key = N'CompanyId', @value = @CompanyId;
PRINT CONCAT('Generating demo data for company ', N'$(CompanyCode)', ' (', @CompanyId, ')');

IF OBJECT_ID('tempdb..#n') IS NOT NULL DROP TABLE #n;
SELECT TOP (200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i INTO #n FROM sys.all_objects;
CREATE UNIQUE CLUSTERED INDEX IX ON #n (i);

-- ---------------------------------------------------------------- organisation
-- the baseline seed data already holds location HQ and department IT: only what is missing is added, so a re-run or a seeded code never collides
INSERT dbo.tblLocation (LocationCode, LocationName, City, Country, IsActive, CreatedBy)
SELECT v.Code, v.Name, v.City, v.Country, 1, N'DEMOSEED' FROM (VALUES
    (N'HQ',  N'Headquarters',     N'London',    N'United Kingdom'),
    (N'BE',  N'Branch East',      N'Frankfurt', N'Germany'),
    (N'DC',  N'Primary Data Center', N'Amsterdam', N'Netherlands')) v (Code, Name, City, Country)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblLocation x WHERE x.LocationCode = v.Code);
UPDATE dbo.tblLocation SET City = N'London', Country = N'United Kingdom' WHERE LocationCode = N'HQ' AND City IS NULL;
INSERT dbo.tblBuilding (LocationId, BuildingCode, BuildingName, IsActive, CreatedBy)
SELECT l.LocationId, CONCAT(l.LocationCode, N'-B1'), CONCAT(l.LocationName, N' Main Building'), 1, N'DEMOSEED'
FROM dbo.tblLocation l WHERE l.LocationCode IN (N'HQ', N'BE', N'DC')
  AND NOT EXISTS (SELECT 1 FROM dbo.tblBuilding b WHERE b.LocationId = l.LocationId AND b.BuildingCode = CONCAT(l.LocationCode, N'-B1'));
INSERT dbo.tblFloor (BuildingId, FloorCode, FloorName, FloorNumber, IsActive, CreatedBy)
SELECT b.BuildingId, CONCAT(b.BuildingCode, N'-F', f.i), CONCAT(N'Floor ', f.i), f.i, 1, N'DEMOSEED'
FROM dbo.tblBuilding b CROSS JOIN (SELECT i FROM #n WHERE i <= 2) f WHERE b.CreatedBy = N'DEMOSEED';
INSERT dbo.tblRoom (FloorId, RoomCode, RoomName, IsActive, CreatedBy)
SELECT f.FloorId, CONCAT(f.FloorCode, N'-R', r.i), CONCAT(N'Room ', f.FloorNumber, FORMAT(r.i, '00')), 1, N'DEMOSEED'
FROM dbo.tblFloor f CROSS JOIN (SELECT i FROM #n WHERE i <= 3) r WHERE f.CreatedBy = N'DEMOSEED';
INSERT dbo.tblDepartment (DepartmentCode, DepartmentName, IsActive, CreatedBy)
SELECT v.Code, v.Name, 1, N'DEMOSEED' FROM (VALUES
    (N'IT', N'Information Technology'), (N'FIN', N'Finance'), (N'HR', N'Human Resources'), (N'OPS', N'Operations'),
    (N'SAL', N'Sales'), (N'MKT', N'Marketing'), (N'LEG', N'Legal and Compliance'), (N'PRC', N'Procurement')) v (Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblDepartment d WHERE d.DepartmentCode = v.Code);

IF OBJECT_ID('tempdb..#room') IS NOT NULL DROP TABLE #room;
SELECT ROW_NUMBER() OVER (ORDER BY r.RoomId) AS rn, r.RoomId, f.FloorId, b.BuildingId, b.LocationId
INTO #room FROM dbo.tblRoom r JOIN dbo.tblFloor f ON f.FloorId = r.FloorId JOIN dbo.tblBuilding b ON b.BuildingId = f.BuildingId
WHERE r.CreatedBy = N'DEMOSEED';
DECLARE @rooms INT = (SELECT COUNT(*) FROM #room);
IF OBJECT_ID('tempdb..#dept') IS NOT NULL DROP TABLE #dept;
SELECT ROW_NUMBER() OVER (ORDER BY DepartmentId) AS rn, DepartmentId INTO #dept FROM dbo.tblDepartment
WHERE DepartmentCode IN (N'IT', N'FIN', N'HR', N'OPS', N'SAL', N'MKT', N'LEG', N'PRC');
DECLARE @depts INT = (SELECT COUNT(*) FROM #dept);

INSERT dbo.tblEmployee (EmployeeNumber, FirstName, LastName, Email, JobTitle, DepartmentId, LocationId, HireDate, CreatedBy)
SELECT CONCAT(N'E', FORMAT(n.i, '0000')),
       CHOOSE(1 + n.i % 10, N'Emma', N'Liam', N'Sofia', N'Noah', N'Mia', N'Lucas', N'Olivia', N'Ethan', N'Aisha', N'Mateo'),
       CHOOSE(1 + (n.i / 10 + n.i) % 4, N'Smith', N'Khan', N'Garcia', N'Muller'),
       CONCAT(N'employee', n.i, N'@example.invalid'),
       CHOOSE(1 + n.i % 5, N'Analyst', N'Engineer', N'Manager', N'Coordinator', N'Specialist'),
       d.DepartmentId, r.LocationId, DATEADD(DAY, -(100 + (n.i * 37) % 1700), @Today), N'DEMOSEED'
FROM #n n JOIN #dept d ON d.rn = 1 + n.i % @depts JOIN #room r ON r.rn = 1 + n.i % @rooms
WHERE n.i <= 40;
IF OBJECT_ID('tempdb..#emp') IS NOT NULL DROP TABLE #emp;
SELECT ROW_NUMBER() OVER (ORDER BY EmployeeId) AS rn, EmployeeId, DepartmentId INTO #emp FROM dbo.tblEmployee WHERE CreatedBy = N'DEMOSEED';

-- ---------------------------------------------------------------- manufacturers (the seed data has most of them) and the printer category
IF OBJECT_ID('tempdb..#maker') IS NOT NULL DROP TABLE #maker;
SELECT v.Maker, v.NewCode INTO #maker FROM (VALUES
    (N'Dell', N'DELL'), (N'HP', N'HP'), (N'Lenovo', N'LEN'), (N'Hewlett Packard Enterprise', N'HPE'), (N'Cisco', N'CSCO'),
    (N'Juniper', N'JNPR'), (N'Canon', N'CANON'), (N'Xerox', N'XRX'), (N'NetApp', N'NTAP'), (N'Synology', N'SYN')) v (Maker, NewCode);
INSERT dbo.tblManufacturer (ManufacturerCode, ManufacturerName, IsActive, CreatedBy)
SELECT m.NewCode, m.Maker, 1, N'DEMOSEED' FROM #maker m
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblManufacturer x WHERE x.ManufacturerName LIKE m.Maker + N'%' OR x.ManufacturerCode = m.NewCode);
IF NOT EXISTS (SELECT 1 FROM dbo.tblAssetNumberSequence WHERE SequenceCode = N'PRN')
    INSERT dbo.tblAssetNumberSequence (SequenceCode, Prefix, LastNumber, PaddingLength, Description, SortOrder)
    VALUES (N'PRN', @Prefix, 0, 5, N'Printers and multifunction devices', 45);
IF NOT EXISTS (SELECT 1 FROM dbo.tblAssetCategory WHERE CategoryCode = N'PRN')
    INSERT dbo.tblAssetCategory (CategoryCode, CategoryName, AssetNumberSequenceId, DefaultUsefulLifeMonths, RequiresHostname, SortOrder)
    SELECT N'PRN', N'Printer', s.AssetNumberSequenceId, 60, 1, 45 FROM dbo.tblAssetNumberSequence s WHERE s.SequenceCode = N'PRN';

IF OBJECT_ID('tempdb..#man') IS NOT NULL DROP TABLE #man;
SELECT m.Maker, (SELECT TOP 1 x.ManufacturerId FROM dbo.tblManufacturer x WHERE x.ManufacturerName LIKE m.Maker + N'%' OR x.ManufacturerCode = m.NewCode ORDER BY x.ManufacturerId) AS ManufacturerId
INTO #man FROM #maker m;
DECLARE @Currency INT = (SELECT TOP 1 CurrencyId FROM dbo.tblCurrency WHERE IsBaseCurrency = 1);
DECLARE @stAssigned INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'ASSIGNED'),
        @stStock INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'IN_STOCK'),
        @stMaint INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'IN_MAINT'),
        @stReserved INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'RESERVED'),
        @stRetired INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'RETIRED'),
        @stPending INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'PEND_DISP');

-- ---------------------------------------------------------------- the estate: category by position
IF OBJECT_ID('tempdb..#spec') IS NOT NULL DROP TABLE #spec;
SELECT n.i,
       CASE WHEN n.i <= 40 THEN N'LAP' WHEN n.i <= 54 THEN N'PC' WHEN n.i <= 72 THEN N'SRV' WHEN n.i <= 96 THEN N'NET'
            WHEN n.i <= 112 THEN N'PRN' ELSE N'STO' END AS Cat,
       CASE WHEN n.i <= 40 THEN n.i WHEN n.i <= 54 THEN n.i - 40 WHEN n.i <= 72 THEN n.i - 54 WHEN n.i <= 96 THEN n.i - 72
            WHEN n.i <= 112 THEN n.i - 96 ELSE n.i - 112 END AS j,
       (n.i * 7) % 20 AS Slot
INTO #spec FROM #n n WHERE n.i <= 120;

INSERT dbo.tblAsset (AssetNumber, AssetName, AssetCategoryId, AssetStatusId, ManufacturerId, ModelName, SerialNumber, Hostname,
    PurchaseDate, PurchaseCost, CurrencyId, AssignedEmployeeId, DepartmentId, LocationId, BuildingId, FloorId, RoomId,
    StoresSensitiveData, IsDiskEncrypted, EndpointProtectionInstalled, NextAuditDueDate, DecommissionDate, CreatedBy, CreatedDate)
SELECT CONCAT(@Prefix, N'-', s.Cat, N'-', FORMAT(s.j, '00000')),
       CONCAT(mn.Maker, N' ', mn.Model),
       c.AssetCategoryId,
       st.StatusId, m.ManufacturerId, mn.Model,
       CONCAT(s.Cat, N'-', FORMAT(1000000 + (s.i * 7919) % 9000000, '0000000')),
       CASE WHEN s.Cat = N'LAP' THEN CONCAT(N'LT-', FORMAT(s.j, '000'))
            WHEN s.Cat = N'PC' THEN CONCAT(N'WS-', FORMAT(s.j, '000'))
            WHEN s.Cat = N'SRV' THEN CONCAT(N'SRV-', FORMAT(s.j, '000'))
            WHEN s.Cat = N'NET' THEN CONCAT(N'NET-', FORMAT(s.j, '000'))
            WHEN s.Cat = N'PRN' THEN CONCAT(N'PRN-', FORMAT(s.j, '000')) END,
       pd.PurchaseDate,
       CAST(CASE s.Cat WHEN N'LAP' THEN 900 + ABS(CHECKSUM(s.i * 104729)) % 1500 WHEN N'PC' THEN 700 + ABS(CHECKSUM(s.i * 104729)) % 800
                       WHEN N'SRV' THEN 6000 + ABS(CHECKSUM(s.i * 104729)) % 18000 WHEN N'NET' THEN 800 + ABS(CHECKSUM(s.i * 104729)) % 8200
                       WHEN N'PRN' THEN 300 + ABS(CHECKSUM(s.i * 104729)) % 2700 ELSE 5000 + ABS(CHECKSUM(s.i * 104729)) % 25000 END AS DECIMAL(18, 2)),
       @Currency,
       CASE WHEN st.Code = N'ASSIGNED' AND s.Cat IN (N'LAP', N'PC') THEN e.EmployeeId END,
       CASE WHEN st.Code = N'ASSIGNED' AND s.Cat IN (N'LAP', N'PC') THEN e.DepartmentId ELSE d.DepartmentId END,
       r.LocationId, r.BuildingId, r.FloorId, r.RoomId,
       CASE WHEN s.Cat IN (N'LAP', N'SRV', N'STO') THEN 1 ELSE 0 END, CASE WHEN s.Cat IN (N'LAP', N'PC') AND s.i % 9 <> 0 THEN 1 ELSE 0 END,
       CASE WHEN s.Cat IN (N'LAP', N'PC', N'SRV') AND s.i % 13 <> 0 THEN 1 ELSE 0 END,
       DATEADD(DAY, (ABS(CHECKSUM(s.i * 31)) % 300) - 60, @Today),
       CASE WHEN st.Code = N'RETIRED' THEN DATEADD(DAY, -(s.i % 200), @Today) END,
       N'DEMOSEED', DATEADD(DAY, -(s.i % 400), SYSUTCDATETIME())
FROM #spec s
JOIN dbo.tblAssetCategory c ON c.CategoryCode = s.Cat
-- lifecycle state: laptops and desktops are mostly assigned, infrastructure is mostly in use (assigned to a department)
CROSS APPLY (SELECT CASE
        WHEN s.Cat IN (N'LAP', N'PC') AND s.Slot <= 11 THEN N'ASSIGNED'
        WHEN s.Cat NOT IN (N'LAP', N'PC') AND s.Slot <= 14 THEN N'ASSIGNED'
        WHEN s.Slot <= 15 THEN N'IN_STOCK'
        WHEN s.Slot = 16 THEN N'IN_MAINT'
        WHEN s.Slot = 17 THEN N'RESERVED'
        WHEN s.Slot = 18 THEN N'PEND_DISP'
        ELSE N'RETIRED' END AS Code) sc
CROSS APPLY (SELECT sc.Code AS Code, CASE sc.Code WHEN N'ASSIGNED' THEN @stAssigned WHEN N'IN_STOCK' THEN @stStock WHEN N'IN_MAINT' THEN @stMaint
        WHEN N'RESERVED' THEN @stReserved WHEN N'PEND_DISP' THEN @stPending ELSE @stRetired END AS StatusId) st
CROSS APPLY (SELECT
        CASE s.Cat WHEN N'LAP' THEN CHOOSE(1 + s.j % 3, N'Dell', N'HP', N'Lenovo') WHEN N'PC' THEN CHOOSE(1 + s.j % 3, N'Dell', N'HP', N'Lenovo')
                   WHEN N'SRV' THEN CHOOSE(1 + s.j % 3, N'Dell', N'Hewlett Packard Enterprise', N'Lenovo') WHEN N'NET' THEN CHOOSE(1 + s.j % 3, N'Cisco', N'Juniper', N'Cisco')
                   WHEN N'PRN' THEN CHOOSE(1 + s.j % 4, N'HP', N'Canon', N'Xerox', N'HP') ELSE CHOOSE(1 + s.j % 3, N'NetApp', N'Dell', N'Synology') END AS Maker,
        CASE s.Cat WHEN N'LAP' THEN CHOOSE(1 + s.j % 3, N'Latitude 5450', N'EliteBook 840 G10', N'ThinkPad T14 Gen 4')
                   WHEN N'PC' THEN CHOOSE(1 + s.j % 3, N'OptiPlex 7010', N'ProDesk 600 G6', N'ThinkCentre M90q')
                   WHEN N'SRV' THEN CHOOSE(1 + s.j % 3, N'PowerEdge R650', N'ProLiant DL380 Gen11', N'ThinkSystem SR650 V3')
                   WHEN N'NET' THEN CHOOSE(1 + s.j % 3, N'Catalyst 9300 Switch', N'EX4300 Switch', N'Catalyst 9200 Switch')
                   WHEN N'PRN' THEN CHOOSE(1 + s.j % 4, N'LaserJet Enterprise M611', N'imageRUNNER ADVANCE', N'VersaLink C405', N'Color LaserJet M555')
                   ELSE CHOOSE(1 + s.j % 3, N'FAS2750 Storage', N'PowerVault ME5024', N'RackStation RS2423+') END AS Model) mn
JOIN #man m ON m.Maker = mn.Maker
CROSS APPLY (SELECT DATEADD(DAY, -(60 + ABS(CHECKSUM(s.i * 7919)) % 1700), @Today) AS PurchaseDate) pd
JOIN #emp e ON e.rn = 1 + s.i % 40
JOIN #dept d ON d.rn = 1 + s.i % @depts
JOIN #room r ON r.rn = 1 + (s.i * 5) % @rooms;
PRINT CONCAT('Assets: ', @@ROWCOUNT);

-- keep the number sequences ahead of the numbers just issued
UPDATE sq SET LastNumber = x.Cnt
FROM dbo.tblAssetNumberSequence sq
JOIN (SELECT c.AssetNumberSequenceId, COUNT(*) AS Cnt FROM dbo.tblAsset a JOIN dbo.tblAssetCategory c ON c.AssetCategoryId = a.AssetCategoryId
      WHERE a.CreatedBy = N'DEMOSEED' GROUP BY c.AssetNumberSequenceId) x ON x.AssetNumberSequenceId = sq.AssetNumberSequenceId
WHERE sq.LastNumber < x.Cnt;

IF OBJECT_ID('tempdb..#a') IS NOT NULL DROP TABLE #a;
SELECT a.AssetId, ROW_NUMBER() OVER (ORDER BY a.AssetId) AS i, a.AssignedEmployeeId, a.DepartmentId, a.AssetStatusId
INTO #a FROM dbo.tblAsset a WHERE a.CreatedBy = N'DEMOSEED';

-- ---------------------------------------------------------------- assignments (current), and an earlier returned one for a third
-- a signed acknowledgement always carries its date (CK_tblAssetAssignment_Rule05)
INSERT dbo.tblAssetAssignment (AssetId, EmployeeId, DepartmentId, AssignmentType, AssignedDate, IsCurrent, AcknowledgementReceived, AcknowledgementDate, AssignedBy, CreatedBy)
SELECT a.AssetId, a.AssignedEmployeeId, a.DepartmentId, N'Permanent', DATEADD(DAY, -(20 + a.i * 11 % 600), SYSUTCDATETIME()), 1, CASE WHEN a.i % 5 = 0 THEN 0 ELSE 1 END,
       CASE WHEN a.i % 5 = 0 THEN NULL ELSE DATEADD(DAY, -(19 + a.i * 11 % 600), SYSUTCDATETIME()) END, N'admin', N'DEMOSEED'
FROM #a a WHERE a.AssignedEmployeeId IS NOT NULL;
INSERT dbo.tblAssetAssignment (AssetId, EmployeeId, DepartmentId, AssignmentType, AssignedDate, ReturnedDate, IsCurrent, AcknowledgementReceived, AcknowledgementDate, AssignedBy, ReturnedTo, CreatedBy)
SELECT a.AssetId, e.EmployeeId, e.DepartmentId, N'Permanent', DATEADD(DAY, -(800 + a.i % 500), SYSUTCDATETIME()), DATEADD(DAY, -(710 + a.i % 80), SYSUTCDATETIME()),
       0, 1, DATEADD(DAY, -(799 + a.i % 500), SYSUTCDATETIME()), N'admin', N'admin', N'DEMOSEED'
FROM #a a JOIN #emp e ON e.rn = 1 + (a.i * 13) % 40 WHERE a.i % 3 = 0 AND a.AssignedEmployeeId IS NOT NULL;

-- ---------------------------------------------------------------- warranties: a fifth expired, a fifth ending in about 30 days, the rest valid
DECLARE @wActive INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'ACTIVE'),
        @wExpiring INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'EXPIRING'),
        @wExpired INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'EXPIRED');
INSERT dbo.tblWarranty (AssetId, WarrantyStatusId, WarrantyType, ContractNumber, StartDate, EndDate, SupportLevel, CreatedBy)
SELECT a.AssetId,
       CASE WHEN x.EndDate < @Today THEN @wExpired WHEN x.EndDate <= DATEADD(DAY, 90, @Today) THEN @wExpiring ELSE @wActive END,
       CHOOSE(1 + a.i % 3, N'Manufacturer', N'Extended', N'SupportContract'), CONCAT(N'DW-', FORMAT(a.i, '0000')), DATEADD(YEAR, -3, x.EndDate), x.EndDate,
       CHOOSE(1 + a.i % 3, N'Next business day on-site', N'4-hour response', N'Return to depot'), N'DEMOSEED'
FROM #a a
CROSS APPLY (SELECT CASE a.i % 5
                 WHEN 0 THEN DATEADD(DAY, -(20 + a.i * 3 % 380), @Today)            -- expired
                 WHEN 1 THEN DATEADD(DAY, 30 - (a.i % 3), @Today)                    -- about 30 days remaining
                 ELSE DATEADD(DAY, 120 + a.i * 17 % 800, @Today) END AS EndDate) x   -- valid
WHERE a.AssetStatusId <> @stRetired;
PRINT CONCAT('Warranties: ', @@ROWCOUNT);

-- ---------------------------------------------------------------- maintenance: assets under maintenance, plus history and plans
INSERT dbo.tblAssetMaintenance (AssetId, MaintenanceType, MaintenanceStatus, ScheduledDate, CompletedDate, ProblemDescription, ResolutionDetails, PerformedBy, DowntimeHours, CreatedBy)
SELECT a.AssetId,
       CHOOSE(1 + a.i % 4, N'Preventive', N'Corrective', N'Upgrade', N'Inspection'),
       CASE WHEN a.AssetStatusId = @stMaint THEN N'InProgress' WHEN a.i % 3 = 0 THEN N'Scheduled' ELSE N'Completed' END,
       CASE WHEN a.i % 3 = 0 THEN DATEADD(DAY, 7 + a.i % 60, @Today) ELSE DATEADD(DAY, -(10 + a.i * 7 % 300), @Today) END,
       CASE WHEN a.AssetStatusId <> @stMaint AND a.i % 3 <> 0 THEN DATEADD(DAY, -(5 + a.i * 7 % 290), SYSUTCDATETIME()) END,
       CHOOSE(1 + a.i % 4, N'Quarterly service', N'Intermittent fault reported', N'Memory upgrade', N'Annual safety inspection'),
       CASE WHEN a.AssetStatusId <> @stMaint AND a.i % 3 <> 0 THEN N'Completed as planned' END,
       CONCAT(N'Technician ', 1 + a.i % 6), CAST(a.i % 8 AS DECIMAL(9, 2)), N'DEMOSEED'
FROM #a a WHERE a.AssetStatusId = @stMaint OR a.i % 4 = 0;
PRINT CONCAT('Maintenance jobs: ', @@ROWCOUNT);

-- ---------------------------------------------------------------- assets scheduled for disposal carry a pending request
DECLARE @method INT = (SELECT TOP 1 DisposalMethodId FROM dbo.tblDisposalMethod ORDER BY SortOrder);
IF @method IS NOT NULL
    INSERT dbo.tblAssetDisposal (AssetId, DisposalMethodId, DisposalStatus, Reason, RequestedBy, RequestedDate, CreatedBy)
    SELECT a.AssetId, @method, N'Requested', CHOOSE(1 + a.i % 3, N'End of life', N'Beyond economic repair', N'Replaced by newer model'),
           N'admin', DATEADD(DAY, -(a.i % 20), SYSUTCDATETIME()), N'DEMOSEED'
    FROM #a a WHERE a.AssetStatusId = @stPending;

-- ---------------------------------------------------------------- verification of part of the estate in the current cycle
DECLARE @Cycle NVARCHAR(20) = CONCAT(YEAR(SYSUTCDATETIME()), N'-Q', (MONTH(SYSUTCDATETIME()) - 1) / 3 + 1);
INSERT dbo.tblAssetAudit (AssetId, AuditCycle, AuditDate, AuditedBy, AuditResult, ScanMethod, Remarks, IsResolved, CreatedBy)
SELECT a.AssetId, @Cycle, DATEADD(DAY, -(a.i % 40), SYSUTCDATETIME()), N'admin',
       CASE WHEN a.i % 17 = 0 THEN N'LocationMismatch' WHEN a.i % 23 = 0 THEN N'Damaged' ELSE N'Verified' END,
       N'QrScan', CASE WHEN a.i % 17 = 0 THEN N'Found in another room' WHEN a.i % 23 = 0 THEN N'Cracked casing' END, 0, N'DEMOSEED'
FROM #a a WHERE a.i % 3 = 0 AND a.AssetStatusId <> @stRetired;
PRINT CONCAT('Verifications (cycle ', @Cycle, '): ', @@ROWCOUNT);
PRINT 'Demo data completed.';
GO
