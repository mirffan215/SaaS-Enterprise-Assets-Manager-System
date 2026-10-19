/* =====================================================================
   Script 10 VERIFY - proves company isolation. FOR THE REHEARSAL COPY ONLY:
   it creates a test company DEMO with sample rows (refuses to run on a database
   whose name does not end in _Rehearsal or _Test).
   Each check records PASS/FAIL; the script fails if any check fails.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
GO
IF DB_NAME() NOT LIKE N'%[_]Rehearsal' AND DB_NAME() NOT LIKE N'%[_]Test'
    THROW 52020, N'The verification script writes test data; run it only on a *_Rehearsal or *_Test database.', 1;
GO
IF OBJECT_ID(N'tempdb..#Checks') IS NOT NULL DROP TABLE #Checks;
CREATE TABLE #Checks (Id INT IDENTITY, CheckName NVARCHAR(200), Passed BIT, Detail NVARCHAR(400));
GO
-- ---------------------------------------------------------------- setup: company DEMO with a department and location
EXEC sp_set_session_context @key = N'CompanyId', @value = NULL;
IF NOT EXISTS (SELECT 1 FROM dbo.tblCompany WHERE CompanyCode = N'DEMO')
    INSERT dbo.tblCompany (CompanyCode, LegalName, DisplayName, AssetNumberPrefix) VALUES (N'DEMO', N'Demo Company (test)', N'Demo', N'DEMO-IT');
DECLARE @demo INT = (SELECT CompanyId FROM dbo.tblCompany WHERE CompanyCode = N'DEMO');
EXEC sp_set_session_context @key = N'CompanyId', @value = @demo;
-- Same codes as AMGH on purpose: uniqueness is per company now.
IF NOT EXISTS (SELECT 1 FROM dbo.tblDepartment WHERE DepartmentCode = N'IT')
    INSERT dbo.tblDepartment (DepartmentCode, DepartmentName) VALUES (N'IT', N'Demo IT');
IF NOT EXISTS (SELECT 1 FROM dbo.tblLocation WHERE LocationCode = N'HQ')
    INSERT dbo.tblLocation (LocationCode, LocationName) VALUES (N'HQ', N'Demo Head Office');
INSERT #Checks SELECT N'Same department code IT allowed in a second company', 1, NULL;
GO
-- ---------------------------------------------------------------- reads are isolated
DECLARE @demo INT = (SELECT CompanyId FROM dbo.tblCompany WHERE CompanyCode = N'DEMO');
EXEC sp_set_session_context @key = N'CompanyId', @value = 1;
INSERT #Checks SELECT N'AMGH session sees no DEMO department', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' visible')
FROM dbo.tblDepartment WHERE CompanyId = @demo;
INSERT #Checks SELECT N'AMGH session sees exactly one IT department (its own)', CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' rows')
FROM dbo.tblDepartment WHERE DepartmentCode = N'IT';
INSERT #Checks SELECT N'AMGH asset count unchanged by the migration',
    CASE WHEN CAST(COUNT_BIG(*) AS NVARCHAR(30)) = (SELECT Expected FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' AND CheckName = N'rows tblAsset') THEN 1 ELSE 0 END,
    CONCAT(COUNT_BIG(*), N' assets')
FROM dbo.tblAsset;

EXEC sp_set_session_context @key = N'CompanyId', @value = @demo;
INSERT #Checks SELECT N'DEMO session sees no AMGH asset', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END, CONCAT(COUNT(*), N' visible') FROM dbo.tblAsset;
INSERT #Checks SELECT N'DEMO session sees no AMGH user role or audit row',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblUserRole) + (SELECT COUNT(*) FROM dbo.tblAuditLog) = 0 THEN 1 ELSE 0 END, NULL;
INSERT #Checks SELECT N'DEMO session sees only its own department', CASE WHEN COUNT(*) = 1 AND MIN(DepartmentName) = N'Demo IT' THEN 1 ELSE 0 END,
    CONCAT(COUNT(*), N' rows') FROM dbo.tblDepartment;
GO
-- ---------------------------------------------------------------- writes are isolated
DECLARE @demo INT = (SELECT CompanyId FROM dbo.tblCompany WHERE CompanyCode = N'DEMO');
DECLARE @amghIt INT, @demoIt INT;
EXEC sp_set_session_context @key = N'CompanyId', @value = 1;
SELECT @amghIt = DepartmentId FROM dbo.tblDepartment WHERE DepartmentCode = N'IT';

-- 1) AMGH session inserting a row for DEMO is blocked by the block predicate
BEGIN TRY
    INSERT dbo.tblDepartment (CompanyId, DepartmentCode, DepartmentName) VALUES (@demo, N'HACK', N'Injected');
    INSERT #Checks VALUES (N'Insert into another company is blocked', 0, N'insert succeeded');
END TRY
BEGIN CATCH
    INSERT #Checks VALUES (N'Insert into another company is blocked', 1, CONCAT(N'error ', ERROR_NUMBER()));
END CATCH

-- 2) AMGH session cannot update DEMO rows (they are invisible: 0 rows affected)
UPDATE dbo.tblDepartment SET DepartmentName = N'Changed by AMGH' WHERE CompanyId = @demo;
INSERT #Checks VALUES (N'Update of another company''s rows affects nothing', CASE WHEN @@ROWCOUNT = 0 THEN 1 ELSE 0 END, NULL);

-- 3) AMGH session cannot move its own row to DEMO
BEGIN TRY
    UPDATE dbo.tblDepartment SET CompanyId = @demo WHERE DepartmentId = @amghIt;
    INSERT #Checks VALUES (N'Moving a row to another company is blocked', CASE WHEN @@ROWCOUNT = 0 THEN 1 ELSE 0 END, N'no error');
END TRY
BEGIN CATCH
    INSERT #Checks VALUES (N'Moving a row to another company is blocked', 1, CONCAT(N'error ', ERROR_NUMBER()));
END CATCH

-- 4) DEMO cannot link its employee to AMGH's department (composite foreign key)
EXEC sp_set_session_context @key = N'CompanyId', @value = @demo;
BEGIN TRY
    INSERT dbo.tblEmployee (EmployeeNumber, FirstName, LastName, DepartmentId) VALUES (N'X-TEST', N'Cross', N'Link', @amghIt);
    INSERT #Checks VALUES (N'Cross-company reference is rejected', 0, N'insert succeeded');
END TRY
BEGIN CATCH
    INSERT #Checks VALUES (N'Cross-company reference is rejected', CASE WHEN ERROR_NUMBER() IN (547, 33504) THEN 1 ELSE 0 END, CONCAT(N'error ', ERROR_NUMBER()));
END CATCH

-- 5) DEMO can create its own employee in its own department (CompanyId comes from the session default)
SELECT @demoIt = DepartmentId FROM dbo.tblDepartment WHERE DepartmentCode = N'IT';
BEGIN TRY
    IF NOT EXISTS (SELECT 1 FROM dbo.tblEmployee WHERE EmployeeNumber = N'D-0001')
        INSERT dbo.tblEmployee (EmployeeNumber, FirstName, LastName, DepartmentId) VALUES (N'D-0001', N'Demo', N'User', @demoIt);
    INSERT #Checks SELECT N'Own-company insert works and takes the session company', CASE WHEN CompanyId = @demo THEN 1 ELSE 0 END, NULL
    FROM dbo.tblEmployee WHERE EmployeeNumber = N'D-0001';
END TRY
BEGIN CATCH
    INSERT #Checks VALUES (N'Own-company insert works and takes the session company', 0, ERROR_MESSAGE());
END CATCH
GO
-- ---------------------------------------------------------------- no company in session (application login without context) sees nothing
-- Simulated with a user that is not db_owner.
IF USER_ID(N'mc_verify_user') IS NULL CREATE USER mc_verify_user WITHOUT LOGIN;
GRANT SELECT ON dbo.tblAsset TO mc_verify_user;
GRANT SELECT ON dbo.tblDepartment TO mc_verify_user;
EXEC sp_set_session_context @key = N'CompanyId', @value = NULL;
EXECUTE AS USER = N'mc_verify_user';
DECLARE @n BIGINT = (SELECT COUNT_BIG(*) FROM dbo.tblAsset) + (SELECT COUNT_BIG(*) FROM dbo.tblDepartment);
REVERT;
INSERT #Checks VALUES (N'Application login without a company sees no rows', CASE WHEN @n = 0 THEN 1 ELSE 0 END, CONCAT(@n, N' rows visible'));
DROP USER mc_verify_user;
GO
-- ---------------------------------------------------------------- a read-only session company cannot be switched by later SQL
EXEC sp_set_session_context @key = N'CompanyId', @value = 1, @read_only = 1;
BEGIN TRY
    EXEC sp_set_session_context @key = N'CompanyId', @value = 2;
    INSERT #Checks VALUES (N'Session company cannot be changed once set by the application', 0, N'changed');
END TRY
BEGIN CATCH
    INSERT #Checks VALUES (N'Session company cannot be changed once set by the application', 1, CONCAT(N'error ', ERROR_NUMBER()));
END CATCH
GO
-- ---------------------------------------------------------------- result
SELECT Id, CASE Passed WHEN 1 THEN N'PASS' ELSE N'FAIL' END AS Result, CheckName, Detail FROM #Checks ORDER BY Id;
IF EXISTS (SELECT 1 FROM #Checks WHERE Passed = 0)
    THROW 52021, N'Isolation verification FAILED - see the results above.', 1;
PRINT 'Isolation verification passed.';
GO
