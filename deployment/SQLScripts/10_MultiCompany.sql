/* =====================================================================
   AMGH Inventory Management System - Release 2.0 (multi-company foundation)
   Script 10 - Multi-company migration. GENERATED from the schema definition.

   What it does (idempotent, safe to re-run):
     1. creates tblCompany, tblCompanyUser, tblMigrationLog and the 'sec' schema
     2. creates company 1 = AMGH (Al Murjan Group Holding)
     3. adds CompanyId to the 34 company-owned tables and assigns every existing row to AMGH
     4. makes CompanyId mandatory, defaulted from SESSION_CONTEXT('CompanyId')
     5. rebuilds uniqueness per company and links company tables with composite foreign keys
     6. moves user roles into AMGH memberships; marks 'admin' as Platform Super Administrator
     7. reconciles (row counts, asset numbers, nulls) and STOPS if anything differs
     8. switches on Row-Level Security

   Run ORDER: 01, 02, 10, 03, 04, 05, 06, 07, 08 (Deploy-Database.ps1 does this).
   Requires sqlcmd variable AllowMultiCompany=YES (set by Deploy-Database.ps1 -EnableMultiCompany
   or by Invoke-MigrationRehearsal.ps1). The application from Release 2.0 must be deployed with it:
   the v1.x application cannot read data once Row-Level Security is on.
   Rollback: 10_MultiCompany_Rollback.sql (only while AMGH is the only company).
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF N'$(AllowMultiCompany)' <> N'YES'
    THROW 52000, N'Multi-company migration not enabled. Run through Deploy-Database.ps1 -EnableMultiCompany or Invoke-MigrationRehearsal.ps1.', 1;
GO
-- Backup gate: first migration of a database holding data needs a full backup from the last 24 hours.
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NULL
   AND EXISTS (SELECT 1 FROM dbo.tblAsset)
   AND DB_NAME() NOT LIKE N'%[_]Rehearsal'
   AND NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset
                   WHERE database_name = DB_NAME() AND type = 'D' AND backup_finish_date > DATEADD(HOUR, -24, GETDATE()))
    THROW 52001, N'No full backup of this database in the last 24 hours. Back up the database (and the encryption certificate) first.', 1;
GO
IF SCHEMA_ID(N'sec') IS NULL EXEC (N'CREATE SCHEMA sec AUTHORIZATION dbo;');
GO

/* ---------------------------------------------------- 1. new tables */
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblCompany
    (
        CompanyId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblCompany PRIMARY KEY CLUSTERED,
        CompanyCode      NVARCHAR(10)  NOT NULL CONSTRAINT CK_tblCompany_Code CHECK (CompanyCode NOT LIKE N'%[^A-Z0-9]%' AND LEN(CompanyCode) BETWEEN 2 AND 10),
        LegalName        NVARCHAR(200) NOT NULL,
        DisplayName      NVARCHAR(100) NOT NULL,
        RegistrationNo   NVARCHAR(50)  NULL,
        Industry         NVARCHAR(100) NULL,
        Email            NVARCHAR(256) NULL,
        Phone            NVARCHAR(30)  NULL,
        Website          NVARCHAR(250) NULL,
        Country          NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCompany_Country DEFAULT (N'Saudi Arabia'),
        City             NVARCHAR(100) NULL,
        AddressLine      NVARCHAR(500) NULL,
        TimeZone         NVARCHAR(64)  NOT NULL CONSTRAINT DF_tblCompany_TimeZone DEFAULT (N'Arab Standard Time'),
        LanguageCode     NVARCHAR(10)  NOT NULL CONSTRAINT DF_tblCompany_Language DEFAULT (N'en'),
        BaseCurrencyId   INT NULL CONSTRAINT FK_tblCompany_BaseCurrencyId REFERENCES dbo.tblCurrency (CurrencyId),
        AssetNumberPrefix NVARCHAR(20) NOT NULL,
        Status           NVARCHAR(20)  NOT NULL CONSTRAINT DF_tblCompany_Status DEFAULT (N'Active')
                         CONSTRAINT CK_tblCompany_Status CHECK (Status IN (N'Active', N'Suspended', N'Archived')),
        CreatedBy        NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCompany_CreatedBy DEFAULT (N'SYSTEM'),
        CreatedDate      DATETIME2(3)  NOT NULL CONSTRAINT DF_tblCompany_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedBy       NVARCHAR(100) NULL,
        ModifiedDate     DATETIME2(3)  NULL,
        RowVersion       ROWVERSION    NOT NULL,
        CONSTRAINT UQ_tblCompany_CompanyCode UNIQUE (CompanyCode),
        CONSTRAINT UQ_tblCompany_AssetNumberPrefix UNIQUE (AssetNumberPrefix)
    );
    PRINT 'Created dbo.tblCompany';
END
GO
IF OBJECT_ID(N'dbo.tblCompanyUser', N'U') IS NULL
BEGIN
    -- Membership: a platform user belongs to one or more companies; roles are held per membership (tblUserRole.CompanyId).
    CREATE TABLE dbo.tblCompanyUser
    (
        CompanyId    INT NOT NULL CONSTRAINT FK_tblCompanyUser_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        UserId       INT NOT NULL CONSTRAINT FK_tblCompanyUser_UserId REFERENCES dbo.tblUser (UserId),
        EmployeeId   INT NULL,
        IsActive     BIT NOT NULL CONSTRAINT DF_tblCompanyUser_IsActive DEFAULT (1),
        IsDefault    BIT NOT NULL CONSTRAINT DF_tblCompanyUser_IsDefault DEFAULT (0),
        CreatedBy    NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCompanyUser_CreatedBy DEFAULT (N'SYSTEM'),
        CreatedDate  DATETIME2(3)  NOT NULL CONSTRAINT DF_tblCompanyUser_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedBy   NVARCHAR(100) NULL,
        ModifiedDate DATETIME2(3)  NULL,
        RowVersion   ROWVERSION    NOT NULL,
        CONSTRAINT PK_tblCompanyUser PRIMARY KEY CLUSTERED (CompanyId, UserId)
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCompanyUser_UserId_Default ON dbo.tblCompanyUser (UserId) WHERE IsDefault = 1;
    PRINT 'Created dbo.tblCompanyUser';
END
GO
IF OBJECT_ID(N'dbo.tblMigrationLog', N'U') IS NULL
    CREATE TABLE dbo.tblMigrationLog
    (
        MigrationLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblMigrationLog PRIMARY KEY,
        Migration      NVARCHAR(50)  NOT NULL,
        CheckName      NVARCHAR(200) NOT NULL,
        Expected       NVARCHAR(200) NULL,
        Actual         NVARCHAR(200) NULL,
        Passed         BIT NULL,
        LoggedAt       DATETIME2(3) NOT NULL CONSTRAINT DF_tblMigrationLog_LoggedAt DEFAULT (SYSUTCDATETIME())
    );
GO
IF COL_LENGTH(N'dbo.tblUser', N'IsPlatformAdmin') IS NULL
    ALTER TABLE dbo.tblUser ADD IsPlatformAdmin BIT NOT NULL CONSTRAINT DF_tblUser_IsPlatformAdmin DEFAULT (0) WITH VALUES;
GO

/* ---------------------------------------------------- 2. initial company */
IF NOT EXISTS (SELECT 1 FROM dbo.tblCompany WHERE CompanyCode = N'AMGH')
BEGIN
    SET IDENTITY_INSERT dbo.tblCompany ON;
    INSERT dbo.tblCompany (CompanyId, CompanyCode, LegalName, DisplayName, Phone, Country, City, BaseCurrencyId, AssetNumberPrefix)
    VALUES (1, N'AMGH', N'Al Murjan Group Holding', N'Al Murjan Group', N'+966 12 6099955', N'Saudi Arabia', N'Jeddah',
            (SELECT CurrencyId FROM dbo.tblCurrency WHERE CurrencyCode = 'SAR'), N'AMGH-IT');
    SET IDENTITY_INSERT dbo.tblCompany OFF;
    PRINT 'Created company 1 = AMGH';
END
GO

/* ---------------------------------------------------- 3. baseline for reconciliation (first run only) */
IF NOT EXISTS (SELECT 1 FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' AND CheckName = N'baseline taken')
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'';
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetNumberSequence', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetNumberSequence];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetCategory', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetCategory];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetSubCategory', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetSubCategory];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblManufacturer', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblManufacturer];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblVendor', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblVendor];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblLocation', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblLocation];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblBuilding', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblBuilding];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblFloor', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblFloor];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblRoom', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRoom];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblDepartment', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblDepartment];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblEmployee', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblEmployee];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblClassification', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblClassification];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblRiskLevel', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRiskLevel];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblCriticality', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblCriticality];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblRole', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRole];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblRolePermission', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRolePermission];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblUserRole', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblUserRole];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAsset', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAsset];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetAssignment', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAssignment];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetMovement', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMovement];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetMaintenance', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMaintenance];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetAudit', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAudit];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetDisposal', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDisposal];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblWarranty', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblWarranty];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblSoftwareLicense', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicense];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblSoftwareLicenseAllocation', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicenseAllocation];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetQRCode', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetQRCode];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblQrLabelPrintLog', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblQrLabelPrintLog];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetDocument', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDocument];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAssetHistory', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetHistory];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblNotification', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotification];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblNotificationQueue', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotificationQueue];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblAuditLog', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAuditLog];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) SELECT N'MC-1', N'rows tblLoginAudit', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblLoginAudit];
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected)
    SELECT N'MC-1', N'asset number checksum', CAST(CHECKSUM_AGG(CHECKSUM(AssetId, AssetNumber)) AS NVARCHAR(30)) FROM dbo.tblAsset;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected) VALUES (N'MC-1', N'baseline taken', N'yes');
END
GO

/* ---------------------------------------------------- 4. CompanyId on company-owned tables; existing rows -> AMGH */
IF COL_LENGTH(N'dbo.tblAssetNumberSequence', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetNumberSequence] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetNumberSequence] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetNumberSequence];
    BEGIN TRY
        UPDATE dbo.[tblAssetNumberSequence] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetNumberSequence];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetNumberSequence];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetCategory', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetCategory] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetCategory] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetCategory];
    BEGIN TRY
        UPDATE dbo.[tblAssetCategory] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetCategory];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetCategory];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetSubCategory', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetSubCategory] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetSubCategory];
    BEGIN TRY
        UPDATE dbo.[tblAssetSubCategory] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetSubCategory];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetSubCategory];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblManufacturer', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblManufacturer] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblManufacturer] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblManufacturer];
    BEGIN TRY
        UPDATE dbo.[tblManufacturer] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblManufacturer];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblManufacturer];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblVendor', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblVendor] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblVendor] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblVendor];
    BEGIN TRY
        UPDATE dbo.[tblVendor] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblVendor];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblVendor];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblLocation', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblLocation] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblLocation] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblLocation];
    BEGIN TRY
        UPDATE dbo.[tblLocation] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblLocation];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblLocation];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblBuilding', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblBuilding] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblBuilding] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblBuilding];
    BEGIN TRY
        UPDATE dbo.[tblBuilding] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblBuilding];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblBuilding];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblFloor', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblFloor] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblFloor] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblFloor];
    BEGIN TRY
        UPDATE dbo.[tblFloor] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblFloor];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblFloor];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblRoom', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblRoom] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblRoom] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblRoom];
    BEGIN TRY
        UPDATE dbo.[tblRoom] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblRoom];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblRoom];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblDepartment', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblDepartment] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblDepartment] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblDepartment];
    BEGIN TRY
        UPDATE dbo.[tblDepartment] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblDepartment];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblDepartment];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblEmployee', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblEmployee] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblEmployee] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblEmployee];
    BEGIN TRY
        UPDATE dbo.[tblEmployee] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblEmployee];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblEmployee];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblClassification', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblClassification] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblClassification] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblClassification];
    BEGIN TRY
        UPDATE dbo.[tblClassification] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblClassification];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblClassification];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblRiskLevel', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblRiskLevel] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblRiskLevel] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblRiskLevel];
    BEGIN TRY
        UPDATE dbo.[tblRiskLevel] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblRiskLevel];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblRiskLevel];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblCriticality', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblCriticality] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblCriticality] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblCriticality];
    BEGIN TRY
        UPDATE dbo.[tblCriticality] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblCriticality];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblCriticality];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblRole', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblRole] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblRole] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblRole];
    BEGIN TRY
        UPDATE dbo.[tblRole] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblRole];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblRole];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblRolePermission', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblRolePermission] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblRolePermission] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblRolePermission];
    BEGIN TRY
        UPDATE dbo.[tblRolePermission] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblRolePermission];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblRolePermission];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblUserRole', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblUserRole] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblUserRole] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblUserRole];
    BEGIN TRY
        UPDATE dbo.[tblUserRole] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblUserRole];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblUserRole];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAsset', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAsset] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAsset] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAsset];
    BEGIN TRY
        UPDATE dbo.[tblAsset] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAsset];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAsset];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetAssignment', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetAssignment] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetAssignment] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetAssignment];
    BEGIN TRY
        UPDATE dbo.[tblAssetAssignment] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetAssignment];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetAssignment];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetMovement', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetMovement] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetMovement] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetMovement];
    BEGIN TRY
        UPDATE dbo.[tblAssetMovement] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetMovement];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetMovement];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetMaintenance', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetMaintenance] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetMaintenance];
    BEGIN TRY
        UPDATE dbo.[tblAssetMaintenance] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetMaintenance];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetMaintenance];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetAudit', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetAudit] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetAudit] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetAudit];
    BEGIN TRY
        UPDATE dbo.[tblAssetAudit] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetAudit];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetAudit];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetDisposal', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetDisposal] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetDisposal] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetDisposal];
    BEGIN TRY
        UPDATE dbo.[tblAssetDisposal] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetDisposal];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetDisposal];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblWarranty', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblWarranty] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblWarranty] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblWarranty];
    BEGIN TRY
        UPDATE dbo.[tblWarranty] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblWarranty];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblWarranty];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblSoftwareLicense', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblSoftwareLicense] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblSoftwareLicense];
    BEGIN TRY
        UPDATE dbo.[tblSoftwareLicense] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblSoftwareLicense];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblSoftwareLicense];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblSoftwareLicenseAllocation', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblSoftwareLicenseAllocation] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblSoftwareLicenseAllocation];
    BEGIN TRY
        UPDATE dbo.[tblSoftwareLicenseAllocation] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblSoftwareLicenseAllocation];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblSoftwareLicenseAllocation];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetQRCode', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetQRCode] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetQRCode] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetQRCode];
    BEGIN TRY
        UPDATE dbo.[tblAssetQRCode] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetQRCode];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetQRCode];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblQrLabelPrintLog', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblQrLabelPrintLog] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblQrLabelPrintLog];
    BEGIN TRY
        UPDATE dbo.[tblQrLabelPrintLog] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblQrLabelPrintLog];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblQrLabelPrintLog];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetDocument', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetDocument] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetDocument] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetDocument];
    BEGIN TRY
        UPDATE dbo.[tblAssetDocument] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetDocument];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetDocument];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAssetHistory', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAssetHistory] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAssetHistory] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAssetHistory];
    BEGIN TRY
        UPDATE dbo.[tblAssetHistory] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAssetHistory];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAssetHistory];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblNotification', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblNotification] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblNotification] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblNotification];
    BEGIN TRY
        UPDATE dbo.[tblNotification] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblNotification];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblNotification];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblNotificationQueue', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblNotificationQueue] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblNotificationQueue] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblNotificationQueue];
    BEGIN TRY
        UPDATE dbo.[tblNotificationQueue] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblNotificationQueue];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblNotificationQueue];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblAuditLog', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblAuditLog] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblAuditLog] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblAuditLog];
    BEGIN TRY
        UPDATE dbo.[tblAuditLog] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblAuditLog];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblAuditLog];
        THROW;
    END CATCH
END
GO
IF COL_LENGTH(N'dbo.tblLoginAudit', N'CompanyId') IS NULL
    ALTER TABLE dbo.[tblLoginAudit] ADD CompanyId INT NULL;
GO
IF EXISTS (SELECT 1 FROM dbo.[tblLoginAudit] WHERE CompanyId IS NULL)
BEGIN
    -- append-only and audit triggers would refuse or log this one-time ownership update
    DISABLE TRIGGER ALL ON dbo.[tblLoginAudit];
    BEGIN TRY
        UPDATE dbo.[tblLoginAudit] SET CompanyId = 1 WHERE CompanyId IS NULL;
        ENABLE TRIGGER ALL ON dbo.[tblLoginAudit];
    END TRY
    BEGIN CATCH
        ENABLE TRIGGER ALL ON dbo.[tblLoginAudit];
        THROW;
    END CATCH
END
GO
/* ---------------------------------------------------- 5. mandatory, defaulted from the session, linked to tblCompany */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetNumberSequence] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetNumberSequence_CompanyId')
    ALTER TABLE dbo.[tblAssetNumberSequence] ADD CONSTRAINT DF_tblAssetNumberSequence_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetNumberSequence_CompanyId')
    ALTER TABLE dbo.[tblAssetNumberSequence] ADD CONSTRAINT FK_tblAssetNumberSequence_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetNumberSequence_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence'))
    CREATE NONCLUSTERED INDEX IX_tblAssetNumberSequence_CompanyId ON dbo.[tblAssetNumberSequence] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetNumberSequence_CompanyId_AssetNumberSequenceId')
    ALTER TABLE dbo.[tblAssetNumberSequence] ADD CONSTRAINT UQ_tblAssetNumberSequence_CompanyId_AssetNumberSequenceId UNIQUE NONCLUSTERED (CompanyId, [AssetNumberSequenceId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetCategory') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetCategory] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetCategory_CompanyId')
    ALTER TABLE dbo.[tblAssetCategory] ADD CONSTRAINT DF_tblAssetCategory_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetCategory_CompanyId')
    ALTER TABLE dbo.[tblAssetCategory] ADD CONSTRAINT FK_tblAssetCategory_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetCategory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
    CREATE NONCLUSTERED INDEX IX_tblAssetCategory_CompanyId ON dbo.[tblAssetCategory] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetCategory_CompanyId_AssetCategoryId')
    ALTER TABLE dbo.[tblAssetCategory] ADD CONSTRAINT UQ_tblAssetCategory_CompanyId_AssetCategoryId UNIQUE NONCLUSTERED (CompanyId, [AssetCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetSubCategory') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetSubCategory] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetSubCategory_CompanyId')
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CONSTRAINT DF_tblAssetSubCategory_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetSubCategory_CompanyId')
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CONSTRAINT FK_tblAssetSubCategory_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetSubCategory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetSubCategory'))
    CREATE NONCLUSTERED INDEX IX_tblAssetSubCategory_CompanyId ON dbo.[tblAssetSubCategory] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetSubCategory_CompanyId_AssetSubCategoryId')
    ALTER TABLE dbo.[tblAssetSubCategory] ADD CONSTRAINT UQ_tblAssetSubCategory_CompanyId_AssetSubCategoryId UNIQUE NONCLUSTERED (CompanyId, [AssetSubCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblManufacturer') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblManufacturer] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblManufacturer_CompanyId')
    ALTER TABLE dbo.[tblManufacturer] ADD CONSTRAINT DF_tblManufacturer_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblManufacturer_CompanyId')
    ALTER TABLE dbo.[tblManufacturer] ADD CONSTRAINT FK_tblManufacturer_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblManufacturer_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
    CREATE NONCLUSTERED INDEX IX_tblManufacturer_CompanyId ON dbo.[tblManufacturer] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblManufacturer_CompanyId_ManufacturerId')
    ALTER TABLE dbo.[tblManufacturer] ADD CONSTRAINT UQ_tblManufacturer_CompanyId_ManufacturerId UNIQUE NONCLUSTERED (CompanyId, [ManufacturerId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblVendor') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblVendor] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblVendor_CompanyId')
    ALTER TABLE dbo.[tblVendor] ADD CONSTRAINT DF_tblVendor_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblVendor_CompanyId')
    ALTER TABLE dbo.[tblVendor] ADD CONSTRAINT FK_tblVendor_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblVendor_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblVendor'))
    CREATE NONCLUSTERED INDEX IX_tblVendor_CompanyId ON dbo.[tblVendor] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblVendor_CompanyId_VendorId')
    ALTER TABLE dbo.[tblVendor] ADD CONSTRAINT UQ_tblVendor_CompanyId_VendorId UNIQUE NONCLUSTERED (CompanyId, [VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblLocation') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblLocation] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblLocation_CompanyId')
    ALTER TABLE dbo.[tblLocation] ADD CONSTRAINT DF_tblLocation_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblLocation_CompanyId')
    ALTER TABLE dbo.[tblLocation] ADD CONSTRAINT FK_tblLocation_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLocation_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblLocation'))
    CREATE NONCLUSTERED INDEX IX_tblLocation_CompanyId ON dbo.[tblLocation] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblLocation_CompanyId_LocationId')
    ALTER TABLE dbo.[tblLocation] ADD CONSTRAINT UQ_tblLocation_CompanyId_LocationId UNIQUE NONCLUSTERED (CompanyId, [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblBuilding') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblBuilding] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblBuilding_CompanyId')
    ALTER TABLE dbo.[tblBuilding] ADD CONSTRAINT DF_tblBuilding_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblBuilding_CompanyId')
    ALTER TABLE dbo.[tblBuilding] ADD CONSTRAINT FK_tblBuilding_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblBuilding_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblBuilding'))
    CREATE NONCLUSTERED INDEX IX_tblBuilding_CompanyId ON dbo.[tblBuilding] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblBuilding_CompanyId_BuildingId')
    ALTER TABLE dbo.[tblBuilding] ADD CONSTRAINT UQ_tblBuilding_CompanyId_BuildingId UNIQUE NONCLUSTERED (CompanyId, [BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblFloor') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblFloor] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblFloor_CompanyId')
    ALTER TABLE dbo.[tblFloor] ADD CONSTRAINT DF_tblFloor_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblFloor_CompanyId')
    ALTER TABLE dbo.[tblFloor] ADD CONSTRAINT FK_tblFloor_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblFloor_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblFloor'))
    CREATE NONCLUSTERED INDEX IX_tblFloor_CompanyId ON dbo.[tblFloor] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblFloor_CompanyId_FloorId')
    ALTER TABLE dbo.[tblFloor] ADD CONSTRAINT UQ_tblFloor_CompanyId_FloorId UNIQUE NONCLUSTERED (CompanyId, [FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblRoom') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblRoom] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblRoom_CompanyId')
    ALTER TABLE dbo.[tblRoom] ADD CONSTRAINT DF_tblRoom_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRoom_CompanyId')
    ALTER TABLE dbo.[tblRoom] ADD CONSTRAINT FK_tblRoom_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRoom_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRoom'))
    CREATE NONCLUSTERED INDEX IX_tblRoom_CompanyId ON dbo.[tblRoom] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblRoom_CompanyId_RoomId')
    ALTER TABLE dbo.[tblRoom] ADD CONSTRAINT UQ_tblRoom_CompanyId_RoomId UNIQUE NONCLUSTERED (CompanyId, [RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblDepartment') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblDepartment] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblDepartment_CompanyId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT DF_tblDepartment_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_CompanyId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT FK_tblDepartment_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblDepartment_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
    CREATE NONCLUSTERED INDEX IX_tblDepartment_CompanyId ON dbo.[tblDepartment] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblDepartment_CompanyId_DepartmentId')
    ALTER TABLE dbo.[tblDepartment] ADD CONSTRAINT UQ_tblDepartment_CompanyId_DepartmentId UNIQUE NONCLUSTERED (CompanyId, [DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblEmployee') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblEmployee] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblEmployee_CompanyId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT DF_tblEmployee_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_CompanyId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT FK_tblEmployee_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblEmployee_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
    CREATE NONCLUSTERED INDEX IX_tblEmployee_CompanyId ON dbo.[tblEmployee] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblEmployee_CompanyId_EmployeeId')
    ALTER TABLE dbo.[tblEmployee] ADD CONSTRAINT UQ_tblEmployee_CompanyId_EmployeeId UNIQUE NONCLUSTERED (CompanyId, [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblClassification') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblClassification] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblClassification_CompanyId')
    ALTER TABLE dbo.[tblClassification] ADD CONSTRAINT DF_tblClassification_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblClassification_CompanyId')
    ALTER TABLE dbo.[tblClassification] ADD CONSTRAINT FK_tblClassification_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblClassification_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
    CREATE NONCLUSTERED INDEX IX_tblClassification_CompanyId ON dbo.[tblClassification] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblClassification_CompanyId_ClassificationId')
    ALTER TABLE dbo.[tblClassification] ADD CONSTRAINT UQ_tblClassification_CompanyId_ClassificationId UNIQUE NONCLUSTERED (CompanyId, [ClassificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblRiskLevel') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblRiskLevel] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblRiskLevel_CompanyId')
    ALTER TABLE dbo.[tblRiskLevel] ADD CONSTRAINT DF_tblRiskLevel_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRiskLevel_CompanyId')
    ALTER TABLE dbo.[tblRiskLevel] ADD CONSTRAINT FK_tblRiskLevel_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRiskLevel_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRiskLevel'))
    CREATE NONCLUSTERED INDEX IX_tblRiskLevel_CompanyId ON dbo.[tblRiskLevel] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblRiskLevel_CompanyId_RiskLevelId')
    ALTER TABLE dbo.[tblRiskLevel] ADD CONSTRAINT UQ_tblRiskLevel_CompanyId_RiskLevelId UNIQUE NONCLUSTERED (CompanyId, [RiskLevelId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblCriticality') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblCriticality] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblCriticality_CompanyId')
    ALTER TABLE dbo.[tblCriticality] ADD CONSTRAINT DF_tblCriticality_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblCriticality_CompanyId')
    ALTER TABLE dbo.[tblCriticality] ADD CONSTRAINT FK_tblCriticality_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblCriticality_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
    CREATE NONCLUSTERED INDEX IX_tblCriticality_CompanyId ON dbo.[tblCriticality] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblCriticality_CompanyId_CriticalityId')
    ALTER TABLE dbo.[tblCriticality] ADD CONSTRAINT UQ_tblCriticality_CompanyId_CriticalityId UNIQUE NONCLUSTERED (CompanyId, [CriticalityId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblRole') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblRole] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblRole_CompanyId')
    ALTER TABLE dbo.[tblRole] ADD CONSTRAINT DF_tblRole_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRole_CompanyId')
    ALTER TABLE dbo.[tblRole] ADD CONSTRAINT FK_tblRole_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRole_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRole'))
    CREATE NONCLUSTERED INDEX IX_tblRole_CompanyId ON dbo.[tblRole] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblRole_CompanyId_RoleId')
    ALTER TABLE dbo.[tblRole] ADD CONSTRAINT UQ_tblRole_CompanyId_RoleId UNIQUE NONCLUSTERED (CompanyId, [RoleId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblRolePermission') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblRolePermission] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblRolePermission_CompanyId')
    ALTER TABLE dbo.[tblRolePermission] ADD CONSTRAINT DF_tblRolePermission_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRolePermission_CompanyId')
    ALTER TABLE dbo.[tblRolePermission] ADD CONSTRAINT FK_tblRolePermission_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblRolePermission_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblRolePermission'))
    CREATE NONCLUSTERED INDEX IX_tblRolePermission_CompanyId ON dbo.[tblRolePermission] (CompanyId);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblUserRole') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblUserRole] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblUserRole_CompanyId')
    ALTER TABLE dbo.[tblUserRole] ADD CONSTRAINT DF_tblUserRole_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_CompanyId')
    ALTER TABLE dbo.[tblUserRole] ADD CONSTRAINT FK_tblUserRole_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUserRole_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblUserRole'))
    CREATE NONCLUSTERED INDEX IX_tblUserRole_CompanyId ON dbo.[tblUserRole] (CompanyId);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAsset') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAsset] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAsset_CompanyId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT DF_tblAsset_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_CompanyId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT FK_tblAsset_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAsset_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE NONCLUSTERED INDEX IX_tblAsset_CompanyId ON dbo.[tblAsset] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAsset_CompanyId_AssetId')
    ALTER TABLE dbo.[tblAsset] ADD CONSTRAINT UQ_tblAsset_CompanyId_AssetId UNIQUE NONCLUSTERED (CompanyId, [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetAssignment') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetAssignment] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetAssignment_CompanyId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT DF_tblAssetAssignment_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_CompanyId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT FK_tblAssetAssignment_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_CompanyId ON dbo.[tblAssetAssignment] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetAssignment_CompanyId_AssetAssignmentId')
    ALTER TABLE dbo.[tblAssetAssignment] ADD CONSTRAINT UQ_tblAssetAssignment_CompanyId_AssetAssignmentId UNIQUE NONCLUSTERED (CompanyId, [AssetAssignmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetMovement') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetMovement] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetMovement_CompanyId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT DF_tblAssetMovement_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_CompanyId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT FK_tblAssetMovement_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_CompanyId ON dbo.[tblAssetMovement] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetMovement_CompanyId_AssetMovementId')
    ALTER TABLE dbo.[tblAssetMovement] ADD CONSTRAINT UQ_tblAssetMovement_CompanyId_AssetMovementId UNIQUE NONCLUSTERED (CompanyId, [AssetMovementId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetMaintenance') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetMaintenance] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetMaintenance_CompanyId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT DF_tblAssetMaintenance_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_CompanyId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT FK_tblAssetMaintenance_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_CompanyId ON dbo.[tblAssetMaintenance] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetMaintenance_CompanyId_AssetMaintenanceId')
    ALTER TABLE dbo.[tblAssetMaintenance] ADD CONSTRAINT UQ_tblAssetMaintenance_CompanyId_AssetMaintenanceId UNIQUE NONCLUSTERED (CompanyId, [AssetMaintenanceId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetAudit') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetAudit] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetAudit_CompanyId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT DF_tblAssetAudit_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_CompanyId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT FK_tblAssetAudit_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_CompanyId ON dbo.[tblAssetAudit] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetAudit_CompanyId_AssetAuditId')
    ALTER TABLE dbo.[tblAssetAudit] ADD CONSTRAINT UQ_tblAssetAudit_CompanyId_AssetAuditId UNIQUE NONCLUSTERED (CompanyId, [AssetAuditId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetDisposal') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetDisposal] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetDisposal_CompanyId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT DF_tblAssetDisposal_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_CompanyId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT FK_tblAssetDisposal_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDisposal_CompanyId ON dbo.[tblAssetDisposal] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetDisposal_CompanyId_AssetDisposalId')
    ALTER TABLE dbo.[tblAssetDisposal] ADD CONSTRAINT UQ_tblAssetDisposal_CompanyId_AssetDisposalId UNIQUE NONCLUSTERED (CompanyId, [AssetDisposalId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblWarranty') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblWarranty] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblWarranty_CompanyId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT DF_tblWarranty_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_CompanyId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT FK_tblWarranty_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_CompanyId ON dbo.[tblWarranty] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblWarranty_CompanyId_WarrantyId')
    ALTER TABLE dbo.[tblWarranty] ADD CONSTRAINT UQ_tblWarranty_CompanyId_WarrantyId UNIQUE NONCLUSTERED (CompanyId, [WarrantyId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblSoftwareLicense') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblSoftwareLicense] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblSoftwareLicense_CompanyId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT DF_tblSoftwareLicense_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_CompanyId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT FK_tblSoftwareLicense_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicense_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicense'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicense_CompanyId ON dbo.[tblSoftwareLicense] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblSoftwareLicense_CompanyId_SoftwareLicenseId')
    ALTER TABLE dbo.[tblSoftwareLicense] ADD CONSTRAINT UQ_tblSoftwareLicense_CompanyId_SoftwareLicenseId UNIQUE NONCLUSTERED (CompanyId, [SoftwareLicenseId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblSoftwareLicenseAllocation_CompanyId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT DF_tblSoftwareLicenseAllocation_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_CompanyId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblSoftwareLicenseAllocation_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblSoftwareLicenseAllocation'))
    CREATE NONCLUSTERED INDEX IX_tblSoftwareLicenseAllocation_CompanyId ON dbo.[tblSoftwareLicenseAllocation] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblSoftwareLicenseAllocation_CompanyId_SoftwareLicenseAllocationId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] ADD CONSTRAINT UQ_tblSoftwareLicenseAllocation_CompanyId_SoftwareLicenseAllocationId UNIQUE NONCLUSTERED (CompanyId, [SoftwareLicenseAllocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetQRCode') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetQRCode] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetQRCode_CompanyId')
    ALTER TABLE dbo.[tblAssetQRCode] ADD CONSTRAINT DF_tblAssetQRCode_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetQRCode_CompanyId')
    ALTER TABLE dbo.[tblAssetQRCode] ADD CONSTRAINT FK_tblAssetQRCode_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetQRCode_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetQRCode'))
    CREATE NONCLUSTERED INDEX IX_tblAssetQRCode_CompanyId ON dbo.[tblAssetQRCode] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetQRCode_CompanyId_AssetQRCodeId')
    ALTER TABLE dbo.[tblAssetQRCode] ADD CONSTRAINT UQ_tblAssetQRCode_CompanyId_AssetQRCodeId UNIQUE NONCLUSTERED (CompanyId, [AssetQRCodeId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblQrLabelPrintLog] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblQrLabelPrintLog_CompanyId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT DF_tblQrLabelPrintLog_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_CompanyId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT FK_tblQrLabelPrintLog_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblQrLabelPrintLog_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblQrLabelPrintLog'))
    CREATE NONCLUSTERED INDEX IX_tblQrLabelPrintLog_CompanyId ON dbo.[tblQrLabelPrintLog] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblQrLabelPrintLog_CompanyId_QrLabelPrintLogId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] ADD CONSTRAINT UQ_tblQrLabelPrintLog_CompanyId_QrLabelPrintLogId UNIQUE NONCLUSTERED (CompanyId, [QrLabelPrintLogId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetDocument') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetDocument] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetDocument_CompanyId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT DF_tblAssetDocument_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_CompanyId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT FK_tblAssetDocument_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDocument_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetDocument'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDocument_CompanyId ON dbo.[tblAssetDocument] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetDocument_CompanyId_AssetDocumentId')
    ALTER TABLE dbo.[tblAssetDocument] ADD CONSTRAINT UQ_tblAssetDocument_CompanyId_AssetDocumentId UNIQUE NONCLUSTERED (CompanyId, [AssetDocumentId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblAssetHistory') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblAssetHistory] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAssetHistory_CompanyId')
    ALTER TABLE dbo.[tblAssetHistory] ADD CONSTRAINT DF_tblAssetHistory_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetHistory_CompanyId')
    ALTER TABLE dbo.[tblAssetHistory] ADD CONSTRAINT FK_tblAssetHistory_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetHistory_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAssetHistory'))
    CREATE NONCLUSTERED INDEX IX_tblAssetHistory_CompanyId ON dbo.[tblAssetHistory] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblAssetHistory_CompanyId_AssetHistoryId')
    ALTER TABLE dbo.[tblAssetHistory] ADD CONSTRAINT UQ_tblAssetHistory_CompanyId_AssetHistoryId UNIQUE NONCLUSTERED (CompanyId, [AssetHistoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblNotification') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblNotification] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblNotification_CompanyId')
    ALTER TABLE dbo.[tblNotification] ADD CONSTRAINT DF_tblNotification_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblNotification_CompanyId')
    ALTER TABLE dbo.[tblNotification] ADD CONSTRAINT FK_tblNotification_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotification_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblNotification'))
    CREATE NONCLUSTERED INDEX IX_tblNotification_CompanyId ON dbo.[tblNotification] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblNotification_CompanyId_NotificationId')
    ALTER TABLE dbo.[tblNotification] ADD CONSTRAINT UQ_tblNotification_CompanyId_NotificationId UNIQUE NONCLUSTERED (CompanyId, [NotificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblNotificationQueue') AND name = N'CompanyId' AND is_nullable = 1)
    ALTER TABLE dbo.[tblNotificationQueue] ALTER COLUMN CompanyId INT NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblNotificationQueue_CompanyId')
    ALTER TABLE dbo.[tblNotificationQueue] ADD CONSTRAINT DF_tblNotificationQueue_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblNotificationQueue_CompanyId')
    ALTER TABLE dbo.[tblNotificationQueue] ADD CONSTRAINT FK_tblNotificationQueue_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblNotificationQueue_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
    CREATE NONCLUSTERED INDEX IX_tblNotificationQueue_CompanyId ON dbo.[tblNotificationQueue] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblNotificationQueue_CompanyId_NotificationQueueId')
    ALTER TABLE dbo.[tblNotificationQueue] ADD CONSTRAINT UQ_tblNotificationQueue_CompanyId_NotificationQueueId UNIQUE NONCLUSTERED (CompanyId, [NotificationQueueId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblAuditLog_CompanyId')
    ALTER TABLE dbo.[tblAuditLog] ADD CONSTRAINT DF_tblAuditLog_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAuditLog_CompanyId')
    ALTER TABLE dbo.[tblAuditLog] ADD CONSTRAINT FK_tblAuditLog_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAuditLog_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblAuditLog'))
    CREATE NONCLUSTERED INDEX IX_tblAuditLog_CompanyId ON dbo.[tblAuditLog] (CompanyId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N'DF_tblLoginAudit_CompanyId')
    ALTER TABLE dbo.[tblLoginAudit] ADD CONSTRAINT DF_tblLoginAudit_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)) FOR CompanyId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblLoginAudit_CompanyId')
    ALTER TABLE dbo.[tblLoginAudit] ADD CONSTRAINT FK_tblLoginAudit_CompanyId FOREIGN KEY (CompanyId) REFERENCES dbo.tblCompany (CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblLoginAudit_CompanyId' AND object_id = OBJECT_ID(N'dbo.tblLoginAudit'))
    CREATE NONCLUSTERED INDEX IX_tblLoginAudit_CompanyId ON dbo.[tblLoginAudit] (CompanyId);
GO
/* ---------------------------------------------------- 6. uniqueness per company (same index names, CompanyId first) */
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAssetNumberSequence_SequenceCode' AND i.object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAssetNumberSequence_SequenceCode')
        ALTER TABLE dbo.[tblAssetNumberSequence] DROP CONSTRAINT UX_tblAssetNumberSequence_SequenceCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetNumberSequence_SequenceCode' AND object_id = OBJECT_ID(N'dbo.tblAssetNumberSequence'))
        DROP INDEX UX_tblAssetNumberSequence_SequenceCode ON dbo.[tblAssetNumberSequence];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetNumberSequence_SequenceCode ON dbo.[tblAssetNumberSequence] ([CompanyId], [SequenceCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAssetCategory_CategoryCode' AND i.object_id = OBJECT_ID(N'dbo.tblAssetCategory') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAssetCategory_CategoryCode')
        ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT UX_tblAssetCategory_CategoryCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
        DROP INDEX UX_tblAssetCategory_CategoryCode ON dbo.[tblAssetCategory];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryCode ON dbo.[tblAssetCategory] ([CompanyId], [CategoryCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAssetCategory_CategoryName' AND i.object_id = OBJECT_ID(N'dbo.tblAssetCategory') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAssetCategory_CategoryName')
        ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT UX_tblAssetCategory_CategoryName;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetCategory_CategoryName' AND object_id = OBJECT_ID(N'dbo.tblAssetCategory'))
        DROP INDEX UX_tblAssetCategory_CategoryName ON dbo.[tblAssetCategory];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetCategory_CategoryName ON dbo.[tblAssetCategory] ([CompanyId], [CategoryName]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode' AND i.object_id = OBJECT_ID(N'dbo.tblAssetSubCategory') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode')
        ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode' AND object_id = OBJECT_ID(N'dbo.tblAssetSubCategory'))
        DROP INDEX UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode ON dbo.[tblAssetSubCategory];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetSubCategory_AssetCategoryId_SubCategoryCode ON dbo.[tblAssetSubCategory] ([CompanyId], [AssetCategoryId], [SubCategoryCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblManufacturer_ManufacturerCode' AND i.object_id = OBJECT_ID(N'dbo.tblManufacturer') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblManufacturer_ManufacturerCode')
        ALTER TABLE dbo.[tblManufacturer] DROP CONSTRAINT UX_tblManufacturer_ManufacturerCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerCode' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
        DROP INDEX UX_tblManufacturer_ManufacturerCode ON dbo.[tblManufacturer];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerCode ON dbo.[tblManufacturer] ([CompanyId], [ManufacturerCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblManufacturer_ManufacturerName' AND i.object_id = OBJECT_ID(N'dbo.tblManufacturer') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblManufacturer_ManufacturerName')
        ALTER TABLE dbo.[tblManufacturer] DROP CONSTRAINT UX_tblManufacturer_ManufacturerName;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblManufacturer_ManufacturerName' AND object_id = OBJECT_ID(N'dbo.tblManufacturer'))
        DROP INDEX UX_tblManufacturer_ManufacturerName ON dbo.[tblManufacturer];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblManufacturer_ManufacturerName ON dbo.[tblManufacturer] ([CompanyId], [ManufacturerName]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblVendor_VendorCode' AND i.object_id = OBJECT_ID(N'dbo.tblVendor') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblVendor_VendorCode')
        ALTER TABLE dbo.[tblVendor] DROP CONSTRAINT UX_tblVendor_VendorCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblVendor_VendorCode' AND object_id = OBJECT_ID(N'dbo.tblVendor'))
        DROP INDEX UX_tblVendor_VendorCode ON dbo.[tblVendor];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblVendor_VendorCode ON dbo.[tblVendor] ([CompanyId], [VendorCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblLocation_LocationCode' AND i.object_id = OBJECT_ID(N'dbo.tblLocation') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblLocation_LocationCode')
        ALTER TABLE dbo.[tblLocation] DROP CONSTRAINT UX_tblLocation_LocationCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblLocation_LocationCode' AND object_id = OBJECT_ID(N'dbo.tblLocation'))
        DROP INDEX UX_tblLocation_LocationCode ON dbo.[tblLocation];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblLocation_LocationCode ON dbo.[tblLocation] ([CompanyId], [LocationCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblBuilding_LocationId_BuildingCode' AND i.object_id = OBJECT_ID(N'dbo.tblBuilding') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblBuilding_LocationId_BuildingCode')
        ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT UX_tblBuilding_LocationId_BuildingCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblBuilding_LocationId_BuildingCode' AND object_id = OBJECT_ID(N'dbo.tblBuilding'))
        DROP INDEX UX_tblBuilding_LocationId_BuildingCode ON dbo.[tblBuilding];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblBuilding_LocationId_BuildingCode ON dbo.[tblBuilding] ([CompanyId], [LocationId], [BuildingCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblFloor_BuildingId_FloorCode' AND i.object_id = OBJECT_ID(N'dbo.tblFloor') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblFloor_BuildingId_FloorCode')
        ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT UX_tblFloor_BuildingId_FloorCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblFloor_BuildingId_FloorCode' AND object_id = OBJECT_ID(N'dbo.tblFloor'))
        DROP INDEX UX_tblFloor_BuildingId_FloorCode ON dbo.[tblFloor];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblFloor_BuildingId_FloorCode ON dbo.[tblFloor] ([CompanyId], [BuildingId], [FloorCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblRoom_FloorId_RoomCode' AND i.object_id = OBJECT_ID(N'dbo.tblRoom') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblRoom_FloorId_RoomCode')
        ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT UX_tblRoom_FloorId_RoomCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRoom_FloorId_RoomCode' AND object_id = OBJECT_ID(N'dbo.tblRoom'))
        DROP INDEX UX_tblRoom_FloorId_RoomCode ON dbo.[tblRoom];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRoom_FloorId_RoomCode ON dbo.[tblRoom] ([CompanyId], [FloorId], [RoomCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblDepartment_DepartmentCode' AND i.object_id = OBJECT_ID(N'dbo.tblDepartment') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblDepartment_DepartmentCode')
        ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT UX_tblDepartment_DepartmentCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblDepartment_DepartmentCode' AND object_id = OBJECT_ID(N'dbo.tblDepartment'))
        DROP INDEX UX_tblDepartment_DepartmentCode ON dbo.[tblDepartment];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblDepartment_DepartmentCode ON dbo.[tblDepartment] ([CompanyId], [DepartmentCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblEmployee_EmployeeNumber' AND i.object_id = OBJECT_ID(N'dbo.tblEmployee') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblEmployee_EmployeeNumber')
        ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT UX_tblEmployee_EmployeeNumber;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_EmployeeNumber' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
        DROP INDEX UX_tblEmployee_EmployeeNumber ON dbo.[tblEmployee];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_EmployeeNumber ON dbo.[tblEmployee] ([CompanyId], [EmployeeNumber]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblEmployee_Email' AND i.object_id = OBJECT_ID(N'dbo.tblEmployee') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblEmployee_Email')
        ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT UX_tblEmployee_Email;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblEmployee_Email' AND object_id = OBJECT_ID(N'dbo.tblEmployee'))
        DROP INDEX UX_tblEmployee_Email ON dbo.[tblEmployee];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblEmployee_Email ON dbo.[tblEmployee] ([CompanyId], [Email]) WHERE [Email] IS NOT NULL AND [IsDeleted] = 0;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblClassification_ClassificationCode' AND i.object_id = OBJECT_ID(N'dbo.tblClassification') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblClassification_ClassificationCode')
        ALTER TABLE dbo.[tblClassification] DROP CONSTRAINT UX_tblClassification_ClassificationCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationCode' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
        DROP INDEX UX_tblClassification_ClassificationCode ON dbo.[tblClassification];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationCode ON dbo.[tblClassification] ([CompanyId], [ClassificationCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblClassification_ClassificationLevel' AND i.object_id = OBJECT_ID(N'dbo.tblClassification') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblClassification_ClassificationLevel')
        ALTER TABLE dbo.[tblClassification] DROP CONSTRAINT UX_tblClassification_ClassificationLevel;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblClassification_ClassificationLevel' AND object_id = OBJECT_ID(N'dbo.tblClassification'))
        DROP INDEX UX_tblClassification_ClassificationLevel ON dbo.[tblClassification];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblClassification_ClassificationLevel ON dbo.[tblClassification] ([CompanyId], [ClassificationLevel]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblRiskLevel_RiskLevelCode' AND i.object_id = OBJECT_ID(N'dbo.tblRiskLevel') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblRiskLevel_RiskLevelCode')
        ALTER TABLE dbo.[tblRiskLevel] DROP CONSTRAINT UX_tblRiskLevel_RiskLevelCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRiskLevel_RiskLevelCode' AND object_id = OBJECT_ID(N'dbo.tblRiskLevel'))
        DROP INDEX UX_tblRiskLevel_RiskLevelCode ON dbo.[tblRiskLevel];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRiskLevel_RiskLevelCode ON dbo.[tblRiskLevel] ([CompanyId], [RiskLevelCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblCriticality_CriticalityCode' AND i.object_id = OBJECT_ID(N'dbo.tblCriticality') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblCriticality_CriticalityCode')
        ALTER TABLE dbo.[tblCriticality] DROP CONSTRAINT UX_tblCriticality_CriticalityCode;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityCode' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
        DROP INDEX UX_tblCriticality_CriticalityCode ON dbo.[tblCriticality];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityCode ON dbo.[tblCriticality] ([CompanyId], [CriticalityCode]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblCriticality_CriticalityLevel' AND i.object_id = OBJECT_ID(N'dbo.tblCriticality') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblCriticality_CriticalityLevel')
        ALTER TABLE dbo.[tblCriticality] DROP CONSTRAINT UX_tblCriticality_CriticalityLevel;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblCriticality_CriticalityLevel' AND object_id = OBJECT_ID(N'dbo.tblCriticality'))
        DROP INDEX UX_tblCriticality_CriticalityLevel ON dbo.[tblCriticality];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblCriticality_CriticalityLevel ON dbo.[tblCriticality] ([CompanyId], [CriticalityLevel]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblRole_RoleName' AND i.object_id = OBJECT_ID(N'dbo.tblRole') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblRole_RoleName')
        ALTER TABLE dbo.[tblRole] DROP CONSTRAINT UX_tblRole_RoleName;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblRole_RoleName' AND object_id = OBJECT_ID(N'dbo.tblRole'))
        DROP INDEX UX_tblRole_RoleName ON dbo.[tblRole];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblRole_RoleName ON dbo.[tblRole] ([CompanyId], [RoleName]);
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAsset_ManufacturerId_SerialNumber' AND i.object_id = OBJECT_ID(N'dbo.tblAsset') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAsset_ManufacturerId_SerialNumber')
        ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT UX_tblAsset_ManufacturerId_SerialNumber;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_ManufacturerId_SerialNumber' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
        DROP INDEX UX_tblAsset_ManufacturerId_SerialNumber ON dbo.[tblAsset];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_ManufacturerId_SerialNumber ON dbo.[tblAsset] ([CompanyId], [ManufacturerId], [SerialNumber]) WHERE [SerialNumber] IS NOT NULL AND [IsDeleted] = 0;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblAsset_Hostname' AND i.object_id = OBJECT_ID(N'dbo.tblAsset') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblAsset_Hostname')
        ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT UX_tblAsset_Hostname;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_Hostname' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
        DROP INDEX UX_tblAsset_Hostname ON dbo.[tblAsset];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_Hostname ON dbo.[tblAsset] ([CompanyId], [Hostname]) WHERE [Hostname] IS NOT NULL AND [IsDeleted] = 0;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
               WHERE i.name = N'UX_tblNotificationQueue_DeduplicationKey' AND i.object_id = OBJECT_ID(N'dbo.tblNotificationQueue') AND ic.key_ordinal = 1
                 AND COL_NAME(ic.object_id, ic.column_id) = N'CompanyId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UX_tblNotificationQueue_DeduplicationKey')
        ALTER TABLE dbo.[tblNotificationQueue] DROP CONSTRAINT UX_tblNotificationQueue_DeduplicationKey;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblNotificationQueue_DeduplicationKey' AND object_id = OBJECT_ID(N'dbo.tblNotificationQueue'))
        DROP INDEX UX_tblNotificationQueue_DeduplicationKey ON dbo.[tblNotificationQueue];
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblNotificationQueue_DeduplicationKey ON dbo.[tblNotificationQueue] ([CompanyId], [DeduplicationKey]) WHERE [DeduplicationKey] IS NOT NULL;
END
GO
/* ---------------------------------------------------- 7. composite foreign keys: a row can only reference rows of its own company */
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetCategory_AssetNumberSequenceId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetCategory] DROP CONSTRAINT FK_tblAssetCategory_AssetNumberSequenceId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetCategory_AssetNumberSequenceId')
    ALTER TABLE dbo.[tblAssetCategory] WITH CHECK ADD CONSTRAINT FK_tblAssetCategory_AssetNumberSequenceId FOREIGN KEY ([CompanyId], [AssetNumberSequenceId])
        REFERENCES dbo.[tblAssetNumberSequence] ([CompanyId], [AssetNumberSequenceId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetSubCategory_AssetCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetSubCategory] DROP CONSTRAINT FK_tblAssetSubCategory_AssetCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetSubCategory_AssetCategoryId')
    ALTER TABLE dbo.[tblAssetSubCategory] WITH CHECK ADD CONSTRAINT FK_tblAssetSubCategory_AssetCategoryId FOREIGN KEY ([CompanyId], [AssetCategoryId])
        REFERENCES dbo.[tblAssetCategory] ([CompanyId], [AssetCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblBuilding_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblBuilding] DROP CONSTRAINT FK_tblBuilding_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblBuilding_LocationId')
    ALTER TABLE dbo.[tblBuilding] WITH CHECK ADD CONSTRAINT FK_tblBuilding_LocationId FOREIGN KEY ([CompanyId], [LocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblFloor_BuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblFloor] DROP CONSTRAINT FK_tblFloor_BuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblFloor_BuildingId')
    ALTER TABLE dbo.[tblFloor] WITH CHECK ADD CONSTRAINT FK_tblFloor_BuildingId FOREIGN KEY ([CompanyId], [BuildingId])
        REFERENCES dbo.[tblBuilding] ([CompanyId], [BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblRoom_FloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblRoom] DROP CONSTRAINT FK_tblRoom_FloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRoom_FloorId')
    ALTER TABLE dbo.[tblRoom] WITH CHECK ADD CONSTRAINT FK_tblRoom_FloorId FOREIGN KEY ([CompanyId], [FloorId])
        REFERENCES dbo.[tblFloor] ([CompanyId], [FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblDepartment_ParentDepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT FK_tblDepartment_ParentDepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_ParentDepartmentId')
    ALTER TABLE dbo.[tblDepartment] WITH CHECK ADD CONSTRAINT FK_tblDepartment_ParentDepartmentId FOREIGN KEY ([CompanyId], [ParentDepartmentId])
        REFERENCES dbo.[tblDepartment] ([CompanyId], [DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblDepartment_HeadEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblDepartment] DROP CONSTRAINT FK_tblDepartment_HeadEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblDepartment_HeadEmployeeId')
    ALTER TABLE dbo.[tblDepartment] WITH CHECK ADD CONSTRAINT FK_tblDepartment_HeadEmployeeId FOREIGN KEY ([CompanyId], [HeadEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_DepartmentId')
    ALTER TABLE dbo.[tblEmployee] WITH CHECK ADD CONSTRAINT FK_tblEmployee_DepartmentId FOREIGN KEY ([CompanyId], [DepartmentId])
        REFERENCES dbo.[tblDepartment] ([CompanyId], [DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_LocationId')
    ALTER TABLE dbo.[tblEmployee] WITH CHECK ADD CONSTRAINT FK_tblEmployee_LocationId FOREIGN KEY ([CompanyId], [LocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblEmployee_ManagerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblEmployee] DROP CONSTRAINT FK_tblEmployee_ManagerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblEmployee_ManagerEmployeeId')
    ALTER TABLE dbo.[tblEmployee] WITH CHECK ADD CONSTRAINT FK_tblEmployee_ManagerEmployeeId FOREIGN KEY ([CompanyId], [ManagerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblRolePermission_RoleId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblRolePermission] DROP CONSTRAINT FK_tblRolePermission_RoleId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblRolePermission_RoleId')
    ALTER TABLE dbo.[tblRolePermission] WITH CHECK ADD CONSTRAINT FK_tblRolePermission_RoleId FOREIGN KEY ([CompanyId], [RoleId])
        REFERENCES dbo.[tblRole] ([CompanyId], [RoleId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblUserRole_RoleId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblUserRole] DROP CONSTRAINT FK_tblUserRole_RoleId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_RoleId')
    ALTER TABLE dbo.[tblUserRole] WITH CHECK ADD CONSTRAINT FK_tblUserRole_RoleId FOREIGN KEY ([CompanyId], [RoleId])
        REFERENCES dbo.[tblRole] ([CompanyId], [RoleId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetCategoryId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_AssetCategoryId FOREIGN KEY ([CompanyId], [AssetCategoryId])
        REFERENCES dbo.[tblAssetCategory] ([CompanyId], [AssetCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetSubCategoryId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetSubCategoryId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetSubCategoryId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_AssetSubCategoryId FOREIGN KEY ([CompanyId], [AssetSubCategoryId])
        REFERENCES dbo.[tblAssetSubCategory] ([CompanyId], [AssetSubCategoryId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_ManufacturerId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_ManufacturerId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ManufacturerId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_ManufacturerId FOREIGN KEY ([CompanyId], [ManufacturerId])
        REFERENCES dbo.[tblManufacturer] ([CompanyId], [ManufacturerId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_VendorId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_VendorId FOREIGN KEY ([CompanyId], [VendorId])
        REFERENCES dbo.[tblVendor] ([CompanyId], [VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssignedEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssignedEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssignedEmployeeId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_AssignedEmployeeId FOREIGN KEY ([CompanyId], [AssignedEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_DepartmentId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_DepartmentId FOREIGN KEY ([CompanyId], [DepartmentId])
        REFERENCES dbo.[tblDepartment] ([CompanyId], [DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_LocationId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_LocationId FOREIGN KEY ([CompanyId], [LocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_BuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_BuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_BuildingId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_BuildingId FOREIGN KEY ([CompanyId], [BuildingId])
        REFERENCES dbo.[tblBuilding] ([CompanyId], [BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_FloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_FloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_FloorId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_FloorId FOREIGN KEY ([CompanyId], [FloorId])
        REFERENCES dbo.[tblFloor] ([CompanyId], [FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_RoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_RoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RoomId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_RoomId FOREIGN KEY ([CompanyId], [RoomId])
        REFERENCES dbo.[tblRoom] ([CompanyId], [RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_AssetOwnerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_AssetOwnerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_AssetOwnerEmployeeId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_AssetOwnerEmployeeId FOREIGN KEY ([CompanyId], [AssetOwnerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_ClassificationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_ClassificationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_ClassificationId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_ClassificationId FOREIGN KEY ([CompanyId], [ClassificationId])
        REFERENCES dbo.[tblClassification] ([CompanyId], [ClassificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_CriticalityId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_CriticalityId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_CriticalityId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_CriticalityId FOREIGN KEY ([CompanyId], [CriticalityId])
        REFERENCES dbo.[tblCriticality] ([CompanyId], [CriticalityId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAsset_RiskLevelId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAsset] DROP CONSTRAINT FK_tblAsset_RiskLevelId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAsset_RiskLevelId')
    ALTER TABLE dbo.[tblAsset] WITH CHECK ADD CONSTRAINT FK_tblAsset_RiskLevelId FOREIGN KEY ([CompanyId], [RiskLevelId])
        REFERENCES dbo.[tblRiskLevel] ([CompanyId], [RiskLevelId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_AssetId')
    ALTER TABLE dbo.[tblAssetAssignment] WITH CHECK ADD CONSTRAINT FK_tblAssetAssignment_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_EmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_EmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_EmployeeId')
    ALTER TABLE dbo.[tblAssetAssignment] WITH CHECK ADD CONSTRAINT FK_tblAssetAssignment_EmployeeId FOREIGN KEY ([CompanyId], [EmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_DepartmentId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_DepartmentId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_DepartmentId')
    ALTER TABLE dbo.[tblAssetAssignment] WITH CHECK ADD CONSTRAINT FK_tblAssetAssignment_DepartmentId FOREIGN KEY ([CompanyId], [DepartmentId])
        REFERENCES dbo.[tblDepartment] ([CompanyId], [DepartmentId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAssignment_LocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAssignment] DROP CONSTRAINT FK_tblAssetAssignment_LocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAssignment_LocationId')
    ALTER TABLE dbo.[tblAssetAssignment] WITH CHECK ADD CONSTRAINT FK_tblAssetAssignment_LocationId FOREIGN KEY ([CompanyId], [LocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_AssetId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromLocationId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_FromLocationId FOREIGN KEY ([CompanyId], [FromLocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromBuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromBuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_FromBuildingId FOREIGN KEY ([CompanyId], [FromBuildingId])
        REFERENCES dbo.[tblBuilding] ([CompanyId], [BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromFloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromFloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromFloorId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_FromFloorId FOREIGN KEY ([CompanyId], [FromFloorId])
        REFERENCES dbo.[tblFloor] ([CompanyId], [FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_FromRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_FromRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_FromRoomId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_FromRoomId FOREIGN KEY ([CompanyId], [FromRoomId])
        REFERENCES dbo.[tblRoom] ([CompanyId], [RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToLocationId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_ToLocationId FOREIGN KEY ([CompanyId], [ToLocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToBuildingId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToBuildingId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToBuildingId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_ToBuildingId FOREIGN KEY ([CompanyId], [ToBuildingId])
        REFERENCES dbo.[tblBuilding] ([CompanyId], [BuildingId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToFloorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToFloorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToFloorId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_ToFloorId FOREIGN KEY ([CompanyId], [ToFloorId])
        REFERENCES dbo.[tblFloor] ([CompanyId], [FloorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMovement_ToRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMovement] DROP CONSTRAINT FK_tblAssetMovement_ToRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMovement_ToRoomId')
    ALTER TABLE dbo.[tblAssetMovement] WITH CHECK ADD CONSTRAINT FK_tblAssetMovement_ToRoomId FOREIGN KEY ([CompanyId], [ToRoomId])
        REFERENCES dbo.[tblRoom] ([CompanyId], [RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMaintenance_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT FK_tblAssetMaintenance_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_AssetId')
    ALTER TABLE dbo.[tblAssetMaintenance] WITH CHECK ADD CONSTRAINT FK_tblAssetMaintenance_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetMaintenance_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetMaintenance] DROP CONSTRAINT FK_tblAssetMaintenance_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_VendorId')
    ALTER TABLE dbo.[tblAssetMaintenance] WITH CHECK ADD CONSTRAINT FK_tblAssetMaintenance_VendorId FOREIGN KEY ([CompanyId], [VendorId])
        REFERENCES dbo.[tblVendor] ([CompanyId], [VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_AssetId')
    ALTER TABLE dbo.[tblAssetAudit] WITH CHECK ADD CONSTRAINT FK_tblAssetAudit_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundLocationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundLocationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundLocationId')
    ALTER TABLE dbo.[tblAssetAudit] WITH CHECK ADD CONSTRAINT FK_tblAssetAudit_FoundLocationId FOREIGN KEY ([CompanyId], [FoundLocationId])
        REFERENCES dbo.[tblLocation] ([CompanyId], [LocationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundRoomId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundRoomId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundRoomId')
    ALTER TABLE dbo.[tblAssetAudit] WITH CHECK ADD CONSTRAINT FK_tblAssetAudit_FoundRoomId FOREIGN KEY ([CompanyId], [FoundRoomId])
        REFERENCES dbo.[tblRoom] ([CompanyId], [RoomId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetAudit_FoundEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetAudit] DROP CONSTRAINT FK_tblAssetAudit_FoundEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetAudit_FoundEmployeeId')
    ALTER TABLE dbo.[tblAssetAudit] WITH CHECK ADD CONSTRAINT FK_tblAssetAudit_FoundEmployeeId FOREIGN KEY ([CompanyId], [FoundEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDisposal_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetDisposal] DROP CONSTRAINT FK_tblAssetDisposal_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDisposal_AssetId')
    ALTER TABLE dbo.[tblAssetDisposal] WITH CHECK ADD CONSTRAINT FK_tblAssetDisposal_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblWarranty_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT FK_tblWarranty_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_AssetId')
    ALTER TABLE dbo.[tblWarranty] WITH CHECK ADD CONSTRAINT FK_tblWarranty_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblWarranty_ProviderVendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblWarranty] DROP CONSTRAINT FK_tblWarranty_ProviderVendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblWarranty_ProviderVendorId')
    ALTER TABLE dbo.[tblWarranty] WITH CHECK ADD CONSTRAINT FK_tblWarranty_ProviderVendorId FOREIGN KEY ([CompanyId], [ProviderVendorId])
        REFERENCES dbo.[tblVendor] ([CompanyId], [VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_PublisherManufacturerId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_PublisherManufacturerId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_PublisherManufacturerId')
    ALTER TABLE dbo.[tblSoftwareLicense] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicense_PublisherManufacturerId FOREIGN KEY ([CompanyId], [PublisherManufacturerId])
        REFERENCES dbo.[tblManufacturer] ([CompanyId], [ManufacturerId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_VendorId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_VendorId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_VendorId')
    ALTER TABLE dbo.[tblSoftwareLicense] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicense_VendorId FOREIGN KEY ([CompanyId], [VendorId])
        REFERENCES dbo.[tblVendor] ([CompanyId], [VendorId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicense_ContractOwnerEmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicense] DROP CONSTRAINT FK_tblSoftwareLicense_ContractOwnerEmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicense_ContractOwnerEmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicense] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicense_ContractOwnerEmployeeId FOREIGN KEY ([CompanyId], [ContractOwnerEmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_SoftwareLicenseId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_SoftwareLicenseId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_SoftwareLicenseId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_SoftwareLicenseId FOREIGN KEY ([CompanyId], [SoftwareLicenseId])
        REFERENCES dbo.[tblSoftwareLicense] ([CompanyId], [SoftwareLicenseId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_AssetId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblSoftwareLicenseAllocation_EmployeeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] DROP CONSTRAINT FK_tblSoftwareLicenseAllocation_EmployeeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblSoftwareLicenseAllocation_EmployeeId')
    ALTER TABLE dbo.[tblSoftwareLicenseAllocation] WITH CHECK ADD CONSTRAINT FK_tblSoftwareLicenseAllocation_EmployeeId FOREIGN KEY ([CompanyId], [EmployeeId])
        REFERENCES dbo.[tblEmployee] ([CompanyId], [EmployeeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetQRCode_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetQRCode] DROP CONSTRAINT FK_tblAssetQRCode_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetQRCode_AssetId')
    ALTER TABLE dbo.[tblAssetQRCode] WITH CHECK ADD CONSTRAINT FK_tblAssetQRCode_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblQrLabelPrintLog_AssetQRCodeId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT FK_tblQrLabelPrintLog_AssetQRCodeId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetQRCodeId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] WITH CHECK ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetQRCodeId FOREIGN KEY ([CompanyId], [AssetQRCodeId])
        REFERENCES dbo.[tblAssetQRCode] ([CompanyId], [AssetQRCodeId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblQrLabelPrintLog_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblQrLabelPrintLog] DROP CONSTRAINT FK_tblQrLabelPrintLog_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblQrLabelPrintLog_AssetId')
    ALTER TABLE dbo.[tblQrLabelPrintLog] WITH CHECK ADD CONSTRAINT FK_tblQrLabelPrintLog_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDocument_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT FK_tblAssetDocument_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_AssetId')
    ALTER TABLE dbo.[tblAssetDocument] WITH CHECK ADD CONSTRAINT FK_tblAssetDocument_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetDocument_ClassificationId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetDocument] DROP CONSTRAINT FK_tblAssetDocument_ClassificationId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetDocument_ClassificationId')
    ALTER TABLE dbo.[tblAssetDocument] WITH CHECK ADD CONSTRAINT FK_tblAssetDocument_ClassificationId FOREIGN KEY ([CompanyId], [ClassificationId])
        REFERENCES dbo.[tblClassification] ([CompanyId], [ClassificationId]);
GO
IF EXISTS (SELECT 1 FROM sys.foreign_keys fk WHERE fk.name = N'FK_tblAssetHistory_AssetId'
           AND (SELECT COUNT(*) FROM sys.foreign_key_columns c WHERE c.constraint_object_id = fk.object_id) = 1)
    ALTER TABLE dbo.[tblAssetHistory] DROP CONSTRAINT FK_tblAssetHistory_AssetId;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetHistory_AssetId')
    ALTER TABLE dbo.[tblAssetHistory] WITH CHECK ADD CONSTRAINT FK_tblAssetHistory_AssetId FOREIGN KEY ([CompanyId], [AssetId])
        REFERENCES dbo.[tblAsset] ([CompanyId], [AssetId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUserRole_Membership')
BEGIN
    INSERT dbo.tblCompanyUser (CompanyId, UserId, IsDefault)
    SELECT DISTINCT ur.CompanyId, ur.UserId, 1 FROM dbo.tblUserRole ur
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCompanyUser cu WHERE cu.CompanyId = ur.CompanyId AND cu.UserId = ur.UserId);
    ALTER TABLE dbo.tblUserRole WITH CHECK ADD CONSTRAINT FK_tblUserRole_Membership FOREIGN KEY (CompanyId, UserId)
        REFERENCES dbo.tblCompanyUser (CompanyId, UserId);
END
GO

/* ---------------------------------------------------- 8. memberships for every user; employee link moves to the membership */
INSERT dbo.tblCompanyUser (CompanyId, UserId, IsDefault)
SELECT 1, u.UserId, 1 FROM dbo.tblUser u
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCompanyUser cu WHERE cu.UserId = u.UserId);
UPDATE cu SET EmployeeId = u.EmployeeId
FROM dbo.tblCompanyUser cu JOIN dbo.tblUser u ON u.UserId = cu.UserId
WHERE cu.CompanyId = 1 AND cu.EmployeeId IS NULL AND u.EmployeeId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblCompanyUser_Employee')
    ALTER TABLE dbo.tblCompanyUser WITH CHECK ADD CONSTRAINT FK_tblCompanyUser_Employee FOREIGN KEY (CompanyId, EmployeeId)
        REFERENCES dbo.tblEmployee (CompanyId, EmployeeId);
-- The deployment administrator account becomes Platform Super Administrator (confirmed 14 Oct 2026).
UPDATE dbo.tblUser SET IsPlatformAdmin = 1 WHERE Username = N'$(AdminUsername)' AND IsPlatformAdmin = 0;
GO

/* ---------------------------------------------------- 9. reconciliation: stop before Row-Level Security if anything differs */
IF NOT EXISTS (SELECT 1 FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' AND CheckName = N'reconciled')
BEGIN
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetNumberSequence]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetNumberSequence';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetCategory]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetCategory';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetSubCategory]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetSubCategory';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblManufacturer]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblManufacturer';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblVendor]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblVendor';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblLocation]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblLocation';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblBuilding]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblBuilding';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblFloor]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblFloor';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRoom]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblRoom';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblDepartment]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblDepartment';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblEmployee]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblEmployee';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblClassification]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblClassification';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRiskLevel]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblRiskLevel';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblCriticality]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblCriticality';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRole]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblRole';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRolePermission]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblRolePermission';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblUserRole]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblUserRole';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAsset]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAsset';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAssignment]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetAssignment';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMovement]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetMovement';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMaintenance]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetMaintenance';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAudit]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetAudit';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDisposal]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetDisposal';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblWarranty]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblWarranty';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicense]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblSoftwareLicense';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicenseAllocation]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblSoftwareLicenseAllocation';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetQRCode]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetQRCode';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblQrLabelPrintLog]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblQrLabelPrintLog';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDocument]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetDocument';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetHistory]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAssetHistory';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotification]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblNotification';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotificationQueue]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblNotificationQueue';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAuditLog]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblAuditLog';
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblLoginAudit]) WHERE Migration = N'MC-1' AND CheckName = N'rows tblLoginAudit';
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetNumberSequence', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetNumberSequence] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetCategory', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetCategory] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetSubCategory', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetSubCategory] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblManufacturer', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblManufacturer] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblVendor', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblVendor] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblLocation', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblLocation] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblBuilding', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblBuilding] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblFloor', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblFloor] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblRoom', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRoom] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblDepartment', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblDepartment] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblEmployee', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblEmployee] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblClassification', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblClassification] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblRiskLevel', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRiskLevel] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblCriticality', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblCriticality] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblRole', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRole] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblRolePermission', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblRolePermission] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblUserRole', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblUserRole] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAsset', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAsset] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetAssignment', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAssignment] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetMovement', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMovement] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetMaintenance', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetMaintenance] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetAudit', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetAudit] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetDisposal', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDisposal] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblWarranty', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblWarranty] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblSoftwareLicense', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicense] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblSoftwareLicenseAllocation', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblSoftwareLicenseAllocation] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetQRCode', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetQRCode] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblQrLabelPrintLog', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblQrLabelPrintLog] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetDocument', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetDocument] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblAssetHistory', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblAssetHistory] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblNotification', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotification] WHERE CompanyId IS NULL;
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual) SELECT N'MC-1', N'no company missing tblNotificationQueue', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.[tblNotificationQueue] WHERE CompanyId IS NULL;
    UPDATE dbo.tblMigrationLog SET Actual = (SELECT CAST(CHECKSUM_AGG(CHECKSUM(AssetId, AssetNumber)) AS NVARCHAR(30)) FROM dbo.tblAsset)
    WHERE Migration = N'MC-1' AND CheckName = N'asset number checksum';
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual)
    SELECT N'MC-1', N'users without membership', N'0', CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM dbo.tblUser u
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCompanyUser cu WHERE cu.UserId = u.UserId);
    UPDATE dbo.tblMigrationLog SET Passed = CASE WHEN ISNULL(Expected, N'') = ISNULL(Actual, N'') THEN 1 ELSE 0 END
    WHERE Migration = N'MC-1' AND CheckName <> N'baseline taken' AND Passed IS NULL;

    IF EXISTS (SELECT 1 FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' AND Passed = 0)
    BEGIN
        SELECT CheckName, Expected, Actual FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' AND Passed = 0;
        THROW 52002, N'Reconciliation failed - see dbo.tblMigrationLog (Passed = 0). Row-Level Security was NOT enabled. Run the rollback script or restore the backup.', 1;
    END
    INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual, Passed) VALUES (N'MC-1', N'reconciled', N'yes', N'yes', 1);
    PRINT 'Reconciliation passed.';
END
GO

/* ---------------------------------------------------- 10. Row-Level Security
   A row is visible / writable when its CompanyId equals SESSION_CONTEXT('CompanyId'), which the application sets
   READ-ONLY on every connection. With no company in the session, only a database owner (deployment scripts,
   DBA maintenance) sees rows; the application login never does. Platform-level events (CompanyId NULL in the
   audit and login logs) are visible when the session is marked as a platform session. */
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation' AND SCHEMA_NAME(schema_id) = N'sec')
    DROP SECURITY POLICY sec.CompanyIsolation;
GO
CREATE OR ALTER FUNCTION sec.fn_CompanyAccess (@CompanyId INT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS Allowed
    WHERE (@CompanyId IS NOT NULL AND @CompanyId = CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
       OR (@CompanyId IS NULL AND CAST(SESSION_CONTEXT(N'Platform') AS INT) = 1)
       OR (SESSION_CONTEXT(N'CompanyId') IS NULL AND CAST(SESSION_CONTEXT(N'Platform') AS INT) IS NULL
           AND IS_ROLEMEMBER(N'db_owner') = 1);
GO
CREATE SECURITY POLICY sec.CompanyIsolation
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetNumberSequence],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetNumberSequence] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetNumberSequence] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetCategory],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetCategory] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetCategory] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetSubCategory],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetSubCategory] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetSubCategory] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblManufacturer],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblManufacturer] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblManufacturer] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblVendor],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblVendor] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblVendor] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLocation],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLocation] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLocation] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblBuilding],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblBuilding] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblBuilding] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblFloor],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblFloor] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblFloor] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRoom],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRoom] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRoom] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblDepartment],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblDepartment] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblDepartment] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblEmployee],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblEmployee] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblEmployee] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblClassification],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblClassification] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblClassification] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRiskLevel],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRiskLevel] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRiskLevel] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblCriticality],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblCriticality] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblCriticality] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRole],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRole] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRole] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRolePermission],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRolePermission] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblRolePermission] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblUserRole],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblUserRole] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblUserRole] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAsset],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAsset] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAsset] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAssignment],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAssignment] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAssignment] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMovement],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMovement] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMovement] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMaintenance],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMaintenance] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetMaintenance] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAudit],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAudit] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetAudit] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDisposal],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDisposal] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDisposal] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblWarranty],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblWarranty] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblWarranty] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicense],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicense] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicense] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicenseAllocation],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicenseAllocation] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblSoftwareLicenseAllocation] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetQRCode],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetQRCode] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetQRCode] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblQrLabelPrintLog],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblQrLabelPrintLog] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblQrLabelPrintLog] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDocument],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDocument] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetDocument] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetHistory],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetHistory] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAssetHistory] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotification],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotification] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotification] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotificationQueue],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotificationQueue] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblNotificationQueue] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAuditLog],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAuditLog] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblAuditLog] AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLoginAudit],
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLoginAudit] AFTER INSERT,
    ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.[tblLoginAudit] AFTER UPDATE
    WITH (STATE = ON, SCHEMABINDING = ON);
GO
INSERT dbo.tblMigrationLog (Migration, CheckName, Expected, Actual, Passed)
SELECT N'MC-1', N'row-level security on', N'1', CAST(is_enabled AS NVARCHAR(5)), is_enabled
FROM sys.security_policies WHERE name = N'CompanyIsolation';
GO
PRINT 'Script 10 completed: multi-company foundation in place, Row-Level Security ON.';
GO
