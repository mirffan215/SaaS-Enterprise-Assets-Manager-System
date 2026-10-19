/* =====================================================================
   EAMS - Release 2.8: company profile and system settings. Idempotent; adds columns only (no data is changed).
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'dbo.tblCompany', N'NameArabic') IS NULL
    ALTER TABLE dbo.tblCompany ADD NameArabic NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.tblCompany', N'ParentCompanyId') IS NULL
    ALTER TABLE dbo.tblCompany ADD ParentCompanyId INT NULL
        CONSTRAINT FK_tblCompany_ParentCompanyId REFERENCES dbo.tblCompany (CompanyId);
IF COL_LENGTH(N'dbo.tblCompany', N'CompanyType') IS NULL
    ALTER TABLE dbo.tblCompany ADD CompanyType NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.tblCompany', N'VatNumber') IS NULL
    ALTER TABLE dbo.tblCompany ADD VatNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblCompany', N'Region') IS NULL
    ALTER TABLE dbo.tblCompany ADD Region NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tblCompany', N'DateFormat') IS NULL
    ALTER TABLE dbo.tblCompany ADD DateFormat NVARCHAR(20) NOT NULL CONSTRAINT DF_tblCompany_DateFormat DEFAULT (N'dd MMM yyyy');
IF COL_LENGTH(N'dbo.tblCompany', N'NotificationsEnabled') IS NULL
    ALTER TABLE dbo.tblCompany ADD NotificationsEnabled BIT NOT NULL CONSTRAINT DF_tblCompany_NotificationsEnabled DEFAULT (1);
IF COL_LENGTH(N'dbo.tblCompany', N'AssetAuditEnabled') IS NULL
    ALTER TABLE dbo.tblCompany ADD AssetAuditEnabled BIT NOT NULL CONSTRAINT DF_tblCompany_AssetAuditEnabled DEFAULT (1);
IF COL_LENGTH(N'dbo.tblCompanyBranding', N'LogoSourceUrl') IS NULL
    ALTER TABLE dbo.tblCompanyBranding ADD LogoSourceUrl NVARCHAR(500) NULL;   -- where a logo loaded from a URL came from
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblCompany_NotOwnParent')
    ALTER TABLE dbo.tblCompany ADD CONSTRAINT CK_tblCompany_NotOwnParent CHECK (ParentCompanyId IS NULL OR ParentCompanyId <> CompanyId);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblCompany_DateFormat')
    ALTER TABLE dbo.tblCompany ADD CONSTRAINT CK_tblCompany_DateFormat CHECK (DateFormat IN (N'dd MMM yyyy', N'dd/MM/yyyy', N'MM/dd/yyyy', N'yyyy-MM-dd'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblCompany_ParentCompanyId')
    CREATE NONCLUSTERED INDEX IX_tblCompany_ParentCompanyId ON dbo.tblCompany (ParentCompanyId) WHERE ParentCompanyId IS NOT NULL;
GO
PRINT 'Script 15 completed.';
GO
