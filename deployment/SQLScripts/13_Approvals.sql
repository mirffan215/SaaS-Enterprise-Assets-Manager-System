/* =====================================================================
   AMGH Inventory Management System - Release 2.3 (asset lifecycle)
   Script 13 - Configurable approval stages for movements and disposals. Idempotent.
   The movement and disposal workflows themselves (requester <> approver, evidence rules) are in
   script 05; these tables add optional extra stages before the final approval.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation')
    THROW 52300, N'Script 13 needs the multi-company foundation (script 10).', 1;
GO
IF OBJECT_ID(N'dbo.tblApprovalRule', N'U') IS NULL
BEGIN
    -- Stage n of a request type must be approved by a member of ApproverRole. No rules = one stage, any approver.
    CREATE TABLE dbo.tblApprovalRule
    (
        ApprovalRuleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblApprovalRule PRIMARY KEY,
        CompanyId      INT NOT NULL CONSTRAINT DF_tblApprovalRule_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                       CONSTRAINT FK_tblApprovalRule_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        RequestType    NVARCHAR(30)  NOT NULL CONSTRAINT CK_tblApprovalRule_Type CHECK (RequestType IN (N'Movement', N'Disposal')),
        StageNo        TINYINT       NOT NULL CONSTRAINT CK_tblApprovalRule_Stage CHECK (StageNo BETWEEN 1 AND 5),
        ApproverRole   NVARCHAR(100) NOT NULL,
        ModifiedBy     NVARCHAR(100) NOT NULL,
        ModifiedDate   DATETIME2(3)  NOT NULL CONSTRAINT DF_tblApprovalRule_ModifiedDate DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblApprovalRule_Company_Type_Stage ON dbo.tblApprovalRule (CompanyId, RequestType, StageNo);
    PRINT 'Created dbo.tblApprovalRule';
END
GO
IF OBJECT_ID(N'dbo.tblApprovalStep', N'U') IS NULL
BEGIN
    -- Every decision on every stage (append-only): who decided, when, with which remarks.
    CREATE TABLE dbo.tblApprovalStep
    (
        ApprovalStepId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblApprovalStep PRIMARY KEY,
        CompanyId      INT NOT NULL CONSTRAINT DF_tblApprovalStep_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                       CONSTRAINT FK_tblApprovalStep_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        RequestType    NVARCHAR(30)  NOT NULL CONSTRAINT CK_tblApprovalStep_Type CHECK (RequestType IN (N'Movement', N'Disposal')),
        RequestId      INT           NOT NULL,
        StageNo        TINYINT       NOT NULL,
        ApproverRole   NVARCHAR(100) NULL,
        Decision       NVARCHAR(10)  NOT NULL CONSTRAINT CK_tblApprovalStep_Decision CHECK (Decision IN (N'Approved', N'Rejected')),
        DecidedBy      NVARCHAR(100) NOT NULL,
        DecidedDate    DATETIME2(3)  NOT NULL CONSTRAINT DF_tblApprovalStep_DecidedDate DEFAULT (SYSUTCDATETIME()),
        Remarks        NVARCHAR(1000) NULL
    );
    CREATE NONCLUSTERED INDEX IX_tblApprovalStep_Request ON dbo.tblApprovalStep (CompanyId, RequestType, RequestId);
    PRINT 'Created dbo.tblApprovalStep';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblApprovalRule') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalRule,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalRule AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalRule AFTER UPDATE;
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblApprovalStep') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalStep,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalStep AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblApprovalStep AFTER UPDATE;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tblApprovalRule TO amgh_app_role;   -- rules are configuration
    GRANT SELECT, INSERT ON dbo.tblApprovalStep TO amgh_app_role;
    DENY UPDATE, DELETE ON dbo.tblApprovalStep TO amgh_app_role;                  -- decisions are permanent
    DENY UPDATE ON dbo.tblApprovalRule (CompanyId) TO amgh_app_role;
END
GO
PRINT 'Script 13 completed.';
GO
