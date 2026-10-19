/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 08 - Least-privilege database access for the web application
   Variables: DatabaseName, AppLogin  (e.g. IIS APPPOOL\AMGHITInventory)

   The application identity can read/write data and execute procedures,
   but CANNOT delete rows (soft delete only), alter the schema, or modify
   the append-only logs (also blocked by triggers).
   ===================================================================== */
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppLogin)')
BEGIN
    CREATE LOGIN [$(AppLogin)] FROM WINDOWS WITH DEFAULT_DATABASE = [$(DatabaseName)];
    PRINT 'Login $(AppLogin) created.';
END
GO
USE [$(DatabaseName)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppLogin)')
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)] WITH DEFAULT_SCHEMA = dbo;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'amgh_app_role' AND type = 'R')
    CREATE ROLE amgh_app_role AUTHORIZATION dbo;
GO
ALTER ROLE amgh_app_role ADD MEMBER [$(AppLogin)];
GO
GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO amgh_app_role;
GRANT EXECUTE ON SCHEMA::dbo TO amgh_app_role;
DENY DELETE ON SCHEMA::dbo TO amgh_app_role;
DENY UPDATE ON dbo.tblAuditLog TO amgh_app_role;
DENY UPDATE ON dbo.tblAssetHistory TO amgh_app_role;
DENY UPDATE ON dbo.tblLoginAudit TO amgh_app_role;
DENY UPDATE ON dbo.tblQrLabelPrintLog TO amgh_app_role;
DENY UPDATE ON dbo.tblUserPasswordHistory TO amgh_app_role;
GO
-- Column encryption (license keys)
GRANT VIEW DEFINITION ON SYMMETRIC KEY::AMGH_DataProtection_Key TO amgh_app_role;
GRANT CONTROL ON CERTIFICATE::AMGH_DataProtection_Cert TO amgh_app_role;
GO
-- Hangfire job storage lives in its own schema; the app may create/alter only there.
IF SCHEMA_ID(N'HangFire') IS NULL
    EXEC (N'CREATE SCHEMA [HangFire] AUTHORIZATION dbo;');
GO
GRANT CREATE TABLE TO amgh_app_role;
GRANT ALTER, SELECT, INSERT, UPDATE, DELETE, EXECUTE, REFERENCES ON SCHEMA::HangFire TO amgh_app_role;
GO
-- Release 2.0: the application can never change which company a row belongs to (defence in depth beside
-- Row-Level Security; Microsoft's recommended pattern for SESSION_CONTEXT multi-tenancy).
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NOT NULL
BEGIN
    DENY UPDATE ON dbo.[tblAssetNumberSequence] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetCategory] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetSubCategory] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblManufacturer] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblVendor] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblLocation] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblBuilding] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblFloor] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblRoom] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblDepartment] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblEmployee] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblClassification] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblRiskLevel] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblCriticality] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblRole] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblRolePermission] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblUserRole] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAsset] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetAssignment] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetMovement] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetMaintenance] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetAudit] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetDisposal] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblWarranty] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblSoftwareLicense] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblSoftwareLicenseAllocation] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetQRCode] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblQrLabelPrintLog] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetDocument] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAssetHistory] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblNotification] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblNotificationQueue] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblAuditLog] (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.[tblLoginAudit] (CompanyId) TO amgh_app_role;
    -- Memberships and companies are managed only through the application's audited services.
    GRANT SELECT, INSERT, UPDATE ON dbo.tblCompany TO amgh_app_role;
    GRANT SELECT, INSERT, UPDATE ON dbo.tblCompanyUser TO amgh_app_role;
    DENY INSERT, UPDATE ON dbo.tblMigrationLog TO amgh_app_role;
END
GO
PRINT 'Script 08 completed.';
GO
