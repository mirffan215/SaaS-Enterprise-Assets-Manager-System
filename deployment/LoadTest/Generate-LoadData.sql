/* =====================================================================
   AMGH IT Inventory - Release 2.6: load-test data (100,000 assets)

   RUN ONLY ON A COPY. The script refuses to run unless the database name contains "LoadTest".
   Variables (sqlcmd -v): DatabaseName, CompanyCode (default AMGH), AssetCount (default 100000).
   Everything it creates is marked "LT" (codes, numbers, e-mails) and lives in the chosen company.
   Volumes: 10 locations, 20 buildings, 100 floors, 1,000 rooms, 40 departments, 3,000 employees,
   100,000 assets (~60% assigned) with assignment history, 70% with a warranty, 20,000 maintenance jobs,
   10,000 movements, 30,000 verifications, 300,000 audit-log rows, 20,000 notifications.
   Typical duration: 2-6 minutes.
   ===================================================================== */
:on error exit
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() NOT LIKE N'%LoadTest%'
    THROW 53000, N'Refused: run the load-test data generator only on a copy whose database name contains "LoadTest".', 1;
GO
DECLARE @t0 DATETIME2 = SYSUTCDATETIME();
DECLARE @N INT = $(AssetCount);
DECLARE @CompanyId INT = (SELECT CompanyId FROM dbo.tblCompany WHERE CompanyCode = N'$(CompanyCode)');
IF @CompanyId IS NULL THROW 53001, N'Company code not found.', 1;
IF EXISTS (SELECT 1 FROM dbo.tblAsset WHERE AssetNumber LIKE N'%-LT-%') THROW 53002, N'Load-test data already exists in this copy (asset numbers with -LT-).', 1;
DECLARE @Prefix NVARCHAR(10) = (SELECT AssetNumberPrefix FROM dbo.tblCompany WHERE CompanyId = @CompanyId);

-- act inside the company (Row-Level Security and the CompanyId defaults use this), no per-row asset history
EXEC sys.sp_set_session_context @key = N'CompanyId', @value = @CompanyId;
EXEC sys.sp_set_session_context @key = N'AMGH.SuppressAssetHistory', @value = 1;
PRINT CONCAT('Generating ', @N, ' assets in company ', N'$(CompanyCode)', ' (', @CompanyId, ')');

-- numbers 1..N
IF OBJECT_ID('tempdb..#n') IS NOT NULL DROP TABLE #n;
SELECT TOP (CASE WHEN @N > 300000 THEN @N ELSE 300000 END) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
INTO #n FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c;
CREATE UNIQUE CLUSTERED INDEX IX ON #n (i);

-- ---------------------------------------------------------------- organisation
INSERT dbo.tblLocation (LocationCode, LocationName, City, Country, IsActive, CreatedBy)
SELECT CONCAT(N'LT-L', FORMAT(i, '00')), CONCAT(N'LT Site ', i), CHOOSE(1 + i % 3, N'Jeddah', N'Riyadh', N'Dammam'), N'Saudi Arabia', 1, N'LOADTEST'
FROM #n WHERE i <= 10;
INSERT dbo.tblBuilding (LocationId, BuildingCode, BuildingName, IsActive, CreatedBy)
SELECT l.LocationId, CONCAT(N'LT-B', b.i), CONCAT(l.LocationName, N' Building ', b.i), 1, N'LOADTEST'
FROM dbo.tblLocation l CROSS JOIN (SELECT i FROM #n WHERE i <= 2) b WHERE l.LocationCode LIKE N'LT-L%';
INSERT dbo.tblFloor (BuildingId, FloorCode, FloorName, FloorNumber, IsActive, CreatedBy)
SELECT b.BuildingId, CONCAT(N'LT-F', f.i), CONCAT(N'Floor ', f.i), f.i, 1, N'LOADTEST'
FROM dbo.tblBuilding b CROSS JOIN (SELECT i FROM #n WHERE i <= 5) f WHERE b.BuildingCode LIKE N'LT-B%';
INSERT dbo.tblRoom (FloorId, RoomCode, RoomName, IsActive, CreatedBy)
SELECT f.FloorId, CONCAT(N'LT-R', r.i), CONCAT(N'Room ', f.FloorNumber, FORMAT(r.i, '00')), 1, N'LOADTEST'
FROM dbo.tblFloor f CROSS JOIN (SELECT i FROM #n WHERE i <= 10) r WHERE f.FloorCode LIKE N'LT-F%';
INSERT dbo.tblDepartment (DepartmentCode, DepartmentName, IsActive, CreatedBy)
SELECT CONCAT(N'LT-D', FORMAT(i, '00')), CONCAT(N'LT Department ', i), 1, N'LOADTEST' FROM #n WHERE i <= 40;

IF OBJECT_ID('tempdb..#room') IS NOT NULL DROP TABLE #room;
SELECT ROW_NUMBER() OVER (ORDER BY r.RoomId) AS rn, r.RoomId, f.FloorId, b.BuildingId, b.LocationId
INTO #room FROM dbo.tblRoom r JOIN dbo.tblFloor f ON f.FloorId = r.FloorId JOIN dbo.tblBuilding b ON b.BuildingId = f.BuildingId
WHERE r.RoomCode LIKE N'LT-R%';
IF OBJECT_ID('tempdb..#dept') IS NOT NULL DROP TABLE #dept;
SELECT ROW_NUMBER() OVER (ORDER BY DepartmentId) AS rn, DepartmentId INTO #dept FROM dbo.tblDepartment WHERE DepartmentCode LIKE N'LT-D%';

INSERT dbo.tblEmployee (EmployeeNumber, FirstName, LastName, Email, JobTitle, DepartmentId, LocationId, HireDate, CreatedBy)
SELECT CONCAT(N'LT-E', FORMAT(n.i, '00000')),
       CHOOSE(1 + n.i % 12, N'Ahmed', N'Sara', N'Omar', N'Fatimah', N'Khalid', N'Noura', N'Yousef', N'Reem', N'Faisal', N'Huda', N'Majed', N'Lama'),
       CHOOSE(1 + (n.i / 12) % 10, N'Al-Harbi', N'Al-Qahtani', N'Al-Ghamdi', N'Al-Zahrani', N'Al-Otaibi', N'Al-Shehri', N'Al-Dossary', N'Al-Mutairi', N'Al-Anazi', N'Saleh'),
       CONCAT(N'lt.employee', n.i, N'@example.invalid'),            -- .invalid (RFC 2606): never deliverable
       CHOOSE(1 + n.i % 5, N'Accountant', N'Engineer', N'Officer', N'Analyst', N'Coordinator'),
       d.DepartmentId, r.LocationId, DATEADD(DAY, -(n.i % 3000), CAST(SYSUTCDATETIME() AS DATE)), N'LOADTEST'
FROM #n n JOIN #dept d ON d.rn = 1 + n.i % 40 JOIN #room r ON r.rn = 1 + n.i % 1000
WHERE n.i <= 3000;
IF OBJECT_ID('tempdb..#emp') IS NOT NULL DROP TABLE #emp;
SELECT ROW_NUMBER() OVER (ORDER BY EmployeeId) AS rn, EmployeeId, DepartmentId INTO #emp FROM dbo.tblEmployee WHERE EmployeeNumber LIKE N'LT-E%';
PRINT CONCAT('Organisation: ', (SELECT COUNT(*) FROM #room), ' rooms, ', (SELECT COUNT(*) FROM #emp), ' employees');

-- ---------------------------------------------------------------- the company's own lists
IF OBJECT_ID('tempdb..#cat') IS NOT NULL DROP TABLE #cat;
SELECT ROW_NUMBER() OVER (ORDER BY AssetCategoryId) AS rn, AssetCategoryId INTO #cat FROM dbo.tblAssetCategory WHERE IsDeleted = 0 AND IsActive = 1;
IF OBJECT_ID('tempdb..#man') IS NOT NULL DROP TABLE #man;
SELECT ROW_NUMBER() OVER (ORDER BY ManufacturerId) AS rn, ManufacturerId, ManufacturerName INTO #man FROM dbo.tblManufacturer WHERE IsDeleted = 0;
DECLARE @cats INT = (SELECT COUNT(*) FROM #cat), @mans INT = (SELECT COUNT(*) FROM #man);
IF @cats = 0 THROW 53003, N'The company has no asset categories.', 1;
IF OBJECT_ID('tempdb..#cls') IS NOT NULL DROP TABLE #cls;
SELECT ROW_NUMBER() OVER (ORDER BY ClassificationId) AS rn, ClassificationId INTO #cls FROM dbo.tblClassification WHERE IsDeleted = 0;
IF OBJECT_ID('tempdb..#crit') IS NOT NULL DROP TABLE #crit;
SELECT ROW_NUMBER() OVER (ORDER BY CriticalityId) AS rn, CriticalityId INTO #crit FROM dbo.tblCriticality WHERE IsDeleted = 0;
IF OBJECT_ID('tempdb..#risk') IS NOT NULL DROP TABLE #risk;
SELECT ROW_NUMBER() OVER (ORDER BY RiskLevelId) AS rn, RiskLevelId INTO #risk FROM dbo.tblRiskLevel WHERE IsDeleted = 0;
DECLARE @cls INT = (SELECT COUNT(*) FROM #cls), @crit INT = (SELECT COUNT(*) FROM #crit), @risk INT = (SELECT COUNT(*) FROM #risk);
DECLARE @Currency INT = (SELECT TOP 1 CurrencyId FROM dbo.tblCurrency WHERE IsBaseCurrency = 1);
DECLARE @stAssigned INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'ASSIGNED'),
        @stStock INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'IN_STOCK'),
        @stMaint INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'IN_MAINT'),
        @stReserved INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'RESERVED'),
        @stRetired INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'RETIRED'),
        @stDisposed INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'DISPOSED'),
        @stMissing INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'MISSING');

-- ---------------------------------------------------------------- assets
-- status mix: 60% assigned, 22% in stock, 5% maintenance, 4% reserved, 5% retired, 3% disposed, 1% missing
INSERT dbo.tblAsset (AssetNumber, AssetName, AssetCategoryId, AssetStatusId, ManufacturerId, ModelName, SerialNumber, Hostname,
    PurchaseDate, PurchaseCost, CurrencyId, AssignedEmployeeId, DepartmentId, LocationId, BuildingId, FloorId, RoomId,
    ClassificationId, CriticalityId, RiskLevelId, StoresSensitiveData, IsDiskEncrypted, EndpointProtectionInstalled,
    NextAuditDueDate, DecommissionDate, CreatedBy, CreatedDate)
SELECT CONCAT(@Prefix, N'-LT-', FORMAT(n.i, '000000')),
       CONCAT(m.ManufacturerName, N' ', CHOOSE(1 + n.i % 6, N'Latitude 5440', N'EliteBook 840', N'ThinkPad T14', N'OptiPlex 7010', N'ProDesk 600', N'PowerEdge R650')),
       c.AssetCategoryId,
       CASE WHEN n.i % 100 < 60 THEN @stAssigned WHEN n.i % 100 < 82 THEN @stStock WHEN n.i % 100 < 87 THEN @stMaint WHEN n.i % 100 < 91 THEN @stReserved
            WHEN n.i % 100 < 96 THEN @stRetired WHEN n.i % 100 < 99 THEN @stDisposed ELSE @stMissing END,
       m.ManufacturerId, CHOOSE(1 + n.i % 6, N'5440', N'840 G10', N'T14 Gen 4', N'7010', N'600 G6', N'R650'),
       CONCAT(N'LT', FORMAT(n.i, '0000000')),
       CASE WHEN n.i % 3 = 0 THEN CONCAT(N'lt-ws-', n.i) END,
       DATEADD(DAY, -(ABS(CHECKSUM(n.i * 7919)) % 2555), CAST(SYSUTCDATETIME() AS DATE)),
       CAST(1500 + ABS(CHECKSUM(n.i * 104729)) % 18000 AS DECIMAL(18, 2)), @Currency,
       CASE WHEN n.i % 100 < 60 THEN e.EmployeeId END,
       CASE WHEN n.i % 100 < 60 THEN e.DepartmentId ELSE d.DepartmentId END,
       r.LocationId, r.BuildingId, r.FloorId, r.RoomId,
       cl.ClassificationId, cr.CriticalityId, rk.RiskLevelId,
       CASE WHEN n.i % 4 = 0 THEN 1 ELSE 0 END, CASE WHEN n.i % 9 = 0 THEN 0 ELSE 1 END, CASE WHEN n.i % 13 = 0 THEN 0 ELSE 1 END,
       DATEADD(DAY, (ABS(CHECKSUM(n.i * 31)) % 730) - 365, CAST(SYSUTCDATETIME() AS DATE)),
       CASE WHEN n.i % 100 BETWEEN 96 AND 98 THEN DATEADD(DAY, -(n.i % 400), CAST(SYSUTCDATETIME() AS DATE)) END,
       N'LOADTEST', DATEADD(MINUTE, -(n.i % 1000000), SYSUTCDATETIME())
FROM #n n
JOIN #cat c ON c.rn = 1 + n.i % @cats
JOIN #man m ON m.rn = 1 + (n.i / 3) % NULLIF(@mans, 0)
JOIN #emp e ON e.rn = 1 + n.i % 3000
JOIN #dept d ON d.rn = 1 + n.i % 40
JOIN #room r ON r.rn = 1 + (n.i * 7) % 1000
LEFT JOIN #cls cl ON cl.rn = 1 + n.i % NULLIF(@cls, 0)
LEFT JOIN #crit cr ON cr.rn = 1 + (n.i / 7) % NULLIF(@crit, 0)
LEFT JOIN #risk rk ON rk.rn = 1 + (n.i / 11) % NULLIF(@risk, 0)
WHERE n.i <= @N;
PRINT CONCAT('Assets: ', @@ROWCOUNT);

IF OBJECT_ID('tempdb..#a') IS NOT NULL DROP TABLE #a;
SELECT a.AssetId, CAST(SUBSTRING(a.AssetNumber, LEN(a.AssetNumber) - 5, 6) AS INT) AS i, a.AssignedEmployeeId, a.DepartmentId, a.LocationId,
       a.BuildingId, a.FloorId, a.RoomId, a.AssetStatusId
INTO #a FROM dbo.tblAsset a WHERE a.AssetNumber LIKE N'%-LT-%';
CREATE UNIQUE CLUSTERED INDEX IX ON #a (i);

-- ---------------------------------------------------------------- history
-- current assignment of every assigned asset, plus one returned (earlier) assignment for half of all assets
INSERT dbo.tblAssetAssignment (AssetId, EmployeeId, DepartmentId, AssignmentType, AssignedDate, IsCurrent, AcknowledgementReceived, AssignedBy, CreatedBy)
SELECT a.AssetId, a.AssignedEmployeeId, a.DepartmentId, N'Permanent', DATEADD(DAY, -(a.i % 700), SYSUTCDATETIME()), 1, CASE WHEN a.i % 5 = 0 THEN 0 ELSE 1 END, N'loadtest', N'LOADTEST'
FROM #a a WHERE a.AssignedEmployeeId IS NOT NULL;
INSERT dbo.tblAssetAssignment (AssetId, EmployeeId, DepartmentId, AssignmentType, AssignedDate, ReturnedDate, IsCurrent, AcknowledgementReceived, AssignedBy, ReturnedTo, CreatedBy)
SELECT a.AssetId, e.EmployeeId, e.DepartmentId, N'Permanent', DATEADD(DAY, -(800 + a.i % 900), SYSUTCDATETIME()), DATEADD(DAY, -(710 + a.i % 80), SYSUTCDATETIME()),
       0, 1, N'loadtest', N'loadtest', N'LOADTEST'
FROM #a a JOIN #emp e ON e.rn = 1 + (a.i * 13) % 3000 WHERE a.i % 2 = 0;
PRINT CONCAT('Assignments: ', (SELECT COUNT(*) FROM dbo.tblAssetAssignment WHERE AssignedBy = N'loadtest'));

DECLARE @wActive INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'ACTIVE'),
        @wExpiring INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'EXPIRING'),
        @wExpired INT = (SELECT WarrantyStatusId FROM dbo.tblWarrantyStatus WHERE StatusCode = N'EXPIRED');
INSERT dbo.tblWarranty (AssetId, WarrantyStatusId, WarrantyType, ContractNumber, StartDate, EndDate, SupportLevel, CreatedBy)
SELECT a.AssetId, CASE WHEN x.EndDate < CAST(SYSUTCDATETIME() AS DATE) THEN @wExpired
                       WHEN x.EndDate <= DATEADD(DAY, 90, CAST(SYSUTCDATETIME() AS DATE)) THEN @wExpiring ELSE @wActive END,
       CHOOSE(1 + a.i % 3, N'Manufacturer', N'Extended', N'SupportContract'), CONCAT(N'LT-W', a.i), DATEADD(YEAR, -3, x.EndDate), x.EndDate,
       N'Next business day on-site', N'LOADTEST'
FROM #a a CROSS APPLY (SELECT DATEADD(DAY, (ABS(CHECKSUM(a.i * 17)) % 1825) - 730, CAST(SYSUTCDATETIME() AS DATE)) AS EndDate) x
WHERE a.i % 10 < 7;
PRINT CONCAT('Warranties: ', @@ROWCOUNT);

INSERT dbo.tblAssetMaintenance (AssetId, MaintenanceType, MaintenanceStatus, ScheduledDate, CompletedDate, ProblemDescription, ResolutionDetails, PerformedBy, DowntimeHours, CreatedBy)
SELECT a.AssetId, CHOOSE(1 + n.i % 4, N'Preventive', N'Corrective', N'Upgrade', N'Inspection'),
       CASE WHEN n.i % 10 = 0 THEN N'Scheduled' WHEN n.i % 10 = 1 THEN N'InProgress' ELSE N'Completed' END,
       DATEADD(DAY, (n.i % 400) - 350, CAST(SYSUTCDATETIME() AS DATE)),
       CASE WHEN n.i % 10 >= 2 THEN DATEADD(DAY, -(n.i % 300), SYSUTCDATETIME()) END,
       CHOOSE(1 + n.i % 4, N'Quarterly service', N'Screen flickers', N'RAM upgrade', N'Safety inspection'),
       CASE WHEN n.i % 10 >= 2 THEN N'Done' END, CONCAT(N'Technician ', 1 + n.i % 25), CAST(n.i % 12 AS DECIMAL(9, 2)), N'LOADTEST'
FROM #n n JOIN #a a ON a.i = 1 + (n.i * 5) % @N WHERE n.i <= 20000;
PRINT CONCAT('Maintenance jobs: ', @@ROWCOUNT);

INSERT dbo.tblAssetMovement (AssetId, FromLocationId, FromBuildingId, ToLocationId, ToBuildingId, MovementType, MovementStatus, MovementReason,
    RequestedBy, RequestedDate, ApprovedBy, ApprovedDate, CompletedDate, CreatedBy)
SELECT a.AssetId, r.LocationId, r.BuildingId, a.LocationId, a.BuildingId, N'Relocation', N'Completed', N'Office reorganisation',
       N'loadtest', DATEADD(DAY, -(n.i % 600) - 2, SYSUTCDATETIME()), N'loadtest.manager', DATEADD(DAY, -(n.i % 600) - 1, SYSUTCDATETIME()),
       DATEADD(DAY, -(n.i % 600), SYSUTCDATETIME()), N'LOADTEST'
FROM #n n JOIN #a a ON a.i = 1 + (n.i * 9) % @N JOIN #room r ON r.rn = 1 + (n.i * 3) % 1000 WHERE n.i <= 10000;
PRINT CONCAT('Movements: ', @@ROWCOUNT);

DECLARE @Cycle NVARCHAR(20) = CONCAT(YEAR(SYSUTCDATETIME()), N'-Q', (MONTH(SYSUTCDATETIME()) - 1) / 3 + 1);
INSERT dbo.tblAssetAudit (AssetId, AuditCycle, AuditDate, AuditedBy, AuditResult, ScanMethod, Remarks, IsResolved, CreatedBy)
SELECT a.AssetId, @Cycle, DATEADD(MINUTE, -(a.i % 50000), SYSUTCDATETIME()), N'loadtest',
       CASE WHEN a.i % 50 = 0 THEN N'LocationMismatch' WHEN a.i % 97 = 0 THEN N'Damaged' ELSE N'Verified' END,
       N'QrScan', CASE WHEN a.i % 50 = 0 THEN N'Found in another room' WHEN a.i % 97 = 0 THEN N'Cracked screen' END, 0, N'LOADTEST'
FROM #a a WHERE a.i % 10 < 3 AND a.AssetStatusId <> @stDisposed;
PRINT CONCAT('Verifications (cycle ', @Cycle, '): ', @@ROWCOUNT);

INSERT dbo.tblAuditLog (EntityName, EntityKey, Action, ChangedColumns, OldValues, NewValues, PerformedBy, IpAddress, EventDate)
SELECT N'Asset', CAST(a.AssetId AS NVARCHAR(20)), CHOOSE(1 + n.i % 4, N'Create', N'Update', N'Update', N'View'),
       N'AssetStatusId,AssignedEmployeeId', N'{"AssetStatusId":2}', N'{"AssetStatusId":3}', CONCAT(N'loadtest.user', n.i % 30),
       N'10.0.0.10', DATEADD(MINUTE, -(n.i % 1051200), SYSUTCDATETIME())
FROM #n n JOIN #a a ON a.i = 1 + n.i % @N WHERE n.i <= 300000;
PRINT CONCAT('Audit-log rows: ', @@ROWCOUNT);

-- in-app notifications for the company's users (so the bell and the notification centre have volume)
INSERT dbo.tblNotification (UserId, Title, Message, NotificationType, Severity, RelatedEntityName, RelatedEntityId, ActionUrl, IsRead, CreatedBy)
SELECT u.UserId, CONCAT(N'Warranty ends in 30 days: ', @Prefix, N'-LT-', FORMAT(n.i, '000000')), N'Load-test notification.', N'WarrantyExpiry',
       N'Info', N'Asset', NULL, N'/notifications', CASE WHEN n.i % 4 = 0 THEN 0 ELSE 1 END, N'LOADTEST'
FROM #n n JOIN (SELECT cu.UserId, ROW_NUMBER() OVER (ORDER BY cu.UserId) AS rn, COUNT(*) OVER () AS cnt FROM dbo.tblCompanyUser cu WHERE cu.CompanyId = @CompanyId AND cu.IsActive = 1) u
  ON u.rn = 1 + n.i % u.cnt
WHERE n.i <= 20000;
PRINT CONCAT('Notifications: ', @@ROWCOUNT);

EXEC sys.sp_set_session_context @key = N'AMGH.SuppressAssetHistory', @value = 0;
-- fresh statistics so the first test run is representative
UPDATE STATISTICS dbo.tblAsset; UPDATE STATISTICS dbo.tblAssetAssignment; UPDATE STATISTICS dbo.tblWarranty;
UPDATE STATISTICS dbo.tblAssetMaintenance; UPDATE STATISTICS dbo.tblAssetMovement; UPDATE STATISTICS dbo.tblAssetAudit;
UPDATE STATISTICS dbo.tblAuditLog; UPDATE STATISTICS dbo.tblNotification; UPDATE STATISTICS dbo.tblEmployee;
PRINT CONCAT('Done in ', DATEDIFF(SECOND, @t0, SYSUTCDATETIME()), ' s.');
GO
