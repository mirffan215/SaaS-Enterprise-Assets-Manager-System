/* =====================================================================
   AMGH Inventory Management System - Release 2.1 (company workspaces)
   Script 11 - Company branding. Idempotent. Runs after script 10.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.tblCompany', N'U') IS NULL
    THROW 52100, N'Script 11 needs the multi-company foundation (script 10).', 1;
GO
IF OBJECT_ID(N'dbo.tblCompanyBranding', N'U') IS NULL
BEGIN
    -- One row per company. Shown on the public company picker and sign-in page, so it holds no confidential data.
    CREATE TABLE dbo.tblCompanyBranding
    (
        CompanyId        INT NOT NULL CONSTRAINT PK_tblCompanyBranding PRIMARY KEY
                         CONSTRAINT FK_tblCompanyBranding_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        LogoImage        VARBINARY(MAX) NULL,
        LogoContentType  NVARCHAR(50)  NULL CONSTRAINT CK_tblCompanyBranding_LogoType CHECK (LogoContentType IS NULL OR LogoContentType IN (N'image/png', N'image/jpeg')),
        HeaderColor      CHAR(7) NOT NULL CONSTRAINT DF_tblCompanyBranding_Header DEFAULT ('#0E3B43'),
        PrimaryColor     CHAR(7) NOT NULL CONSTRAINT DF_tblCompanyBranding_Primary DEFAULT ('#13707A'),
        AccentColor      CHAR(7) NOT NULL CONSTRAINT DF_tblCompanyBranding_Accent DEFAULT ('#E8604C'),
        LoginTagline     NVARCHAR(150) NULL,
        ModifiedBy       NVARCHAR(100) NOT NULL CONSTRAINT DF_tblCompanyBranding_ModifiedBy DEFAULT (N'SYSTEM'),
        ModifiedDate     DATETIME2(3)  NOT NULL CONSTRAINT DF_tblCompanyBranding_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        RowVersion       ROWVERSION    NOT NULL,
        CONSTRAINT CK_tblCompanyBranding_Colors CHECK (HeaderColor LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'
            AND PrimaryColor LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'
            AND AccentColor LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]')
    );
    PRINT 'Created dbo.tblCompanyBranding';
END
GO
-- AMGH keeps today's colours (the application's built-in palette).
INSERT dbo.tblCompanyBranding (CompanyId)
SELECT c.CompanyId FROM dbo.tblCompany c
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCompanyBranding b WHERE b.CompanyId = c.CompanyId);
GO
-- Company setup happens through the application's platform screens; deletion is never allowed.
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON dbo.tblCompanyBranding TO amgh_app_role;
    DENY DELETE ON dbo.tblCompanyBranding TO amgh_app_role;
    DENY DELETE ON dbo.tblCompany TO amgh_app_role;
    DENY DELETE ON dbo.tblCompanyUser TO amgh_app_role;
END
GO
PRINT 'Script 11 completed.';
GO
