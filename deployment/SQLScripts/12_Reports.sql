/* =====================================================================
   AMGH Inventory Management System - Release 2.2 (dashboard and reports)
   Script 12 - Report history and saved report configurations. Idempotent.
   Both tables are company-owned and protected by Row-Level Security.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NULL OR NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation')
    THROW 52200, N'Script 12 needs the multi-company foundation (script 10).', 1;
GO
IF OBJECT_ID(N'dbo.tblReportRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblReportRun
    (
        ReportRunId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblReportRun PRIMARY KEY,
        CompanyId   INT NOT NULL CONSTRAINT DF_tblReportRun_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                    CONSTRAINT FK_tblReportRun_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        ReportCode  NVARCHAR(50)  NOT NULL,
        Format      NVARCHAR(10)  NOT NULL CONSTRAINT CK_tblReportRun_Format CHECK (Format IN (N'Excel', N'Csv', N'Word', N'Print')),
        [RowCount]  INT NOT NULL,
        Filters     NVARCHAR(1000) NULL,
        RunBy       NVARCHAR(100) NOT NULL,
        RunDate     DATETIME2(3)  NOT NULL CONSTRAINT DF_tblReportRun_RunDate DEFAULT (SYSUTCDATETIME())
    );
    CREATE NONCLUSTERED INDEX IX_tblReportRun_Company_RunDate ON dbo.tblReportRun (CompanyId, RunDate);
    PRINT 'Created dbo.tblReportRun';
END
GO
IF OBJECT_ID(N'dbo.tblSavedReport', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblSavedReport
    (
        SavedReportId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblSavedReport PRIMARY KEY,
        CompanyId     INT NOT NULL CONSTRAINT DF_tblSavedReport_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                      CONSTRAINT FK_tblSavedReport_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        ReportCode    NVARCHAR(50)   NOT NULL,
        Name          NVARCHAR(100)  NOT NULL,
        QueryString   NVARCHAR(2000) NOT NULL,
        IsShared      BIT NOT NULL CONSTRAINT DF_tblSavedReport_IsShared DEFAULT (0),
        OwnerUserId   INT NOT NULL CONSTRAINT FK_tblSavedReport_Owner REFERENCES dbo.tblUser (UserId),
        CreatedBy     NVARCHAR(100) NOT NULL,
        CreatedDate   DATETIME2(3)  NOT NULL CONSTRAINT DF_tblSavedReport_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedBy    NVARCHAR(100) NULL,
        ModifiedDate  DATETIME2(3)  NULL,
        DeletedBy     NVARCHAR(100) NULL,
        DeletedDate   DATETIME2(3)  NULL,
        IsDeleted     BIT NOT NULL CONSTRAINT DF_tblSavedReport_IsDeleted DEFAULT (0),
        RowVersion    ROWVERSION NOT NULL
    );
    CREATE NONCLUSTERED INDEX IX_tblSavedReport_Company_Report ON dbo.tblSavedReport (CompanyId, ReportCode);
    PRINT 'Created dbo.tblSavedReport';
END
GO
-- Row-Level Security: add both tables to the company isolation policy (script 10 re-creates the policy,
-- so this script runs after it on every deployment).
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblReportRun') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblReportRun,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblReportRun AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblReportRun AFTER UPDATE;
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblSavedReport') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblSavedReport,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblSavedReport AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblSavedReport AFTER UPDATE;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT ON dbo.tblReportRun TO amgh_app_role;
    DENY UPDATE, DELETE ON dbo.tblReportRun TO amgh_app_role;          -- export history is append-only
    GRANT SELECT, INSERT, UPDATE ON dbo.tblSavedReport TO amgh_app_role;
    DENY DELETE ON dbo.tblSavedReport TO amgh_app_role;                -- soft delete only
    DENY UPDATE ON dbo.tblReportRun (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.tblSavedReport (CompanyId) TO amgh_app_role;
END
GO
PRINT 'Script 12 completed.';
GO
