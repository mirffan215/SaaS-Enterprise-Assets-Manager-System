/* =====================================================================
   EAMS - company data export with dual-administrator approval. Idempotent.
   * three permissions (request, approve, download) granted to the SystemAdministrator role of every company
   * tblDataExportRequest: one row per full-export request (company-isolated by Row-Level Security)
   * the approval tables (tblApprovalRule, tblApprovalStep) accept the request type 'DataExport'
   Requires scripts 10 and 13. For existing databases run it once after upgrading the application.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation')
    THROW 51900, N'Script 19 needs the multi-company foundation (script 10).', 1;
IF OBJECT_ID(N'dbo.tblApprovalStep', N'U') IS NULL
    THROW 51901, N'Script 19 needs the approval tables (script 13).', 1;
GO
-- ---------------------------------------------------------------- permissions
INSERT dbo.tblPermission (PermissionCode, ModuleName, PermissionName, SortOrder)
SELECT v.Code, v.Module, v.Name, v.SortOrder FROM (VALUES
    (N'CompanyAdmin.ExportDataRequest',  N'CompanyAdmin', N'Request a full export of the company data', 460),
    (N'CompanyAdmin.ExportDataApprove',  N'CompanyAdmin', N'Approve or reject a full data export (a second administrator)', 470),
    (N'CompanyAdmin.ExportDataDownload', N'CompanyAdmin', N'Download an approved data export', 480)
) v (Code, Module, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPermission p WHERE p.PermissionCode = v.Code);
GO
INSERT dbo.tblRolePermission (RoleId, PermissionId, CompanyId)
SELECT r.RoleId, p.PermissionId, r.CompanyId
FROM dbo.tblRole r CROSS JOIN dbo.tblPermission p
WHERE r.RoleName = N'SystemAdministrator' AND r.IsDeleted = 0
  AND p.PermissionCode IN (N'CompanyAdmin.ExportDataRequest', N'CompanyAdmin.ExportDataApprove', N'CompanyAdmin.ExportDataDownload')
  AND NOT EXISTS (SELECT 1 FROM dbo.tblRolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
-- ---------------------------------------------------------------- approval tables accept the new request type
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblApprovalRule_Type' AND definition NOT LIKE N'%DataExport%')
BEGIN
    ALTER TABLE dbo.tblApprovalRule DROP CONSTRAINT CK_tblApprovalRule_Type;
    ALTER TABLE dbo.tblApprovalRule ADD CONSTRAINT CK_tblApprovalRule_Type CHECK (RequestType IN (N'Movement', N'Disposal', N'DataExport'));
END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblApprovalStep_Type' AND definition NOT LIKE N'%DataExport%')
BEGIN
    ALTER TABLE dbo.tblApprovalStep DROP CONSTRAINT CK_tblApprovalStep_Type;
    ALTER TABLE dbo.tblApprovalStep ADD CONSTRAINT CK_tblApprovalStep_Type CHECK (RequestType IN (N'Movement', N'Disposal', N'DataExport'));
END
GO
-- ---------------------------------------------------------------- export requests
IF OBJECT_ID(N'dbo.tblDataExportRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblDataExportRequest
    (
        DataExportRequestId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblDataExportRequest PRIMARY KEY CLUSTERED,
        CompanyId           INT NOT NULL CONSTRAINT DF_tblDataExportRequest_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                            CONSTRAINT FK_tblDataExportRequest_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        Status              NVARCHAR(20)  NOT NULL CONSTRAINT DF_tblDataExportRequest_Status DEFAULT (N'Requested')
                            CONSTRAINT CK_tblDataExportRequest_Status CHECK (Status IN (N'Requested', N'Approved', N'Rejected', N'Downloaded', N'Expired')),
        Reason              NVARCHAR(500) NOT NULL,
        RequestedBy         NVARCHAR(100) NOT NULL,
        RequestedDate       DATETIME2(3)  NOT NULL CONSTRAINT DF_tblDataExportRequest_RequestedDate DEFAULT (SYSUTCDATETIME()),
        ApprovedBy          NVARCHAR(100) NULL,
        ApprovedDate        DATETIME2(3)  NULL,
        RejectionReason     NVARCHAR(500) NULL,
        -- the one-time download token and the archive password, encrypted by the application (ASP.NET Data Protection)
        TokenProtected      NVARCHAR(600) NULL,
        PasswordProtected   NVARCHAR(600) NULL,
        TokenExpiresDate    DATETIME2(3)  NULL,
        DownloadedBy        NVARCHAR(100) NULL,
        DownloadedDate      DATETIME2(3)  NULL,
        CreatedBy           NVARCHAR(100) NOT NULL CONSTRAINT DF_tblDataExportRequest_CreatedBy DEFAULT (N'SYSTEM'),
        CreatedDate         DATETIME2(3)  NOT NULL CONSTRAINT DF_tblDataExportRequest_CreatedDate DEFAULT (SYSUTCDATETIME()),
        ModifiedBy          NVARCHAR(100) NULL,
        ModifiedDate        DATETIME2(3)  NULL,
        DeletedBy           NVARCHAR(100) NULL,
        DeletedDate         DATETIME2(3)  NULL,
        IsDeleted           BIT NOT NULL CONSTRAINT DF_tblDataExportRequest_IsDeleted DEFAULT (0),
        RowVersion          ROWVERSION NOT NULL,
        CONSTRAINT CK_tblDataExportRequest_Approver CHECK (ApprovedBy IS NULL OR ApprovedBy <> RequestedBy)   -- two different administrators
    );
    CREATE NONCLUSTERED INDEX IX_tblDataExportRequest_Company ON dbo.tblDataExportRequest (CompanyId, Status);
    PRINT 'Created dbo.tblDataExportRequest';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblDataExportRequest') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDataExportRequest,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDataExportRequest AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDataExportRequest AFTER UPDATE;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON dbo.tblDataExportRequest TO amgh_app_role;
    DENY DELETE ON dbo.tblDataExportRequest TO amgh_app_role;
    DENY UPDATE ON dbo.tblDataExportRequest (CompanyId) TO amgh_app_role;
END
GO
PRINT 'Script 19 completed.';
GO
