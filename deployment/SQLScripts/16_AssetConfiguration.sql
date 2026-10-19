/* =====================================================================
   EAMS - Release 2.9: asset configuration. Idempotent; ADDS columns and one table (no data is changed).
   Runs right after script 10 (before the functions and procedures, which use the new columns).
     * tblAssetNumberSequence.Separator   - configurable separator in generated numbers
     * tblAsset fixed-asset fields          - capitalisation, depreciation method, groups, D365 id, recorded book value...
     * tblRoleCategory                       - roles limited to asset categories (company-isolated)
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'dbo.tblAssetNumberSequence', N'Separator') IS NULL
    ALTER TABLE dbo.tblAssetNumberSequence ADD Separator NVARCHAR(1) NOT NULL CONSTRAINT DF_tblAssetNumberSequence_Separator DEFAULT (N'-');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblAssetNumberSequence_Separator')
    ALTER TABLE dbo.tblAssetNumberSequence ADD CONSTRAINT CK_tblAssetNumberSequence_Separator CHECK (Separator IN (N'-', N'/', N'.', N'_', N''));
GO
IF COL_LENGTH(N'dbo.tblAsset', N'CapitalizationDate') IS NULL    ALTER TABLE dbo.tblAsset ADD CapitalizationDate DATE NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'DepreciationMethod') IS NULL    ALTER TABLE dbo.tblAsset ADD DepreciationMethod NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'FixedAssetGroup') IS NULL       ALTER TABLE dbo.tblAsset ADD FixedAssetGroup NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'ErpFixedAssetId') IS NULL       ALTER TABLE dbo.tblAsset ADD ErpFixedAssetId NVARCHAR(50) NULL;     -- D365 fixed asset id
IF COL_LENGTH(N'dbo.tblAsset', N'RecordedBookValue') IS NULL     ALTER TABLE dbo.tblAsset ADD RecordedBookValue DECIMAL(18, 2) NULL; -- from finance; not calculated here
IF COL_LENGTH(N'dbo.tblAsset', N'RecordedBookValueDate') IS NULL ALTER TABLE dbo.tblAsset ADD RecordedBookValueDate DATE NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'CostCenter') IS NULL            ALTER TABLE dbo.tblAsset ADD CostCenter NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'AssetCondition') IS NULL        ALTER TABLE dbo.tblAsset ADD AssetCondition NVARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.tblAsset', N'MaintenanceRequired') IS NULL
    ALTER TABLE dbo.tblAsset ADD MaintenanceRequired BIT NOT NULL CONSTRAINT DF_tblAsset_MaintenanceRequired DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblAsset_DepreciationMethod')
    ALTER TABLE dbo.tblAsset ADD CONSTRAINT CK_tblAsset_DepreciationMethod CHECK (DepreciationMethod IS NULL OR DepreciationMethod IN (N'StraightLine', N'DecliningBalance', N'UnitsOfProduction', N'None'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblAsset_AssetCondition')
    ALTER TABLE dbo.tblAsset ADD CONSTRAINT CK_tblAsset_AssetCondition CHECK (AssetCondition IS NULL OR AssetCondition IN (N'New', N'Good', N'Fair', N'Poor', N'Damaged'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_Company_ErpFixedAssetId')
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_Company_ErpFixedAssetId ON dbo.tblAsset (CompanyId, ErpFixedAssetId) WHERE ErpFixedAssetId IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.tblRoleCategory', N'U') IS NULL
BEGIN
    -- A role limited to these categories. A role with no rows here covers every category.
    CREATE TABLE dbo.tblRoleCategory
    (
        CompanyId       INT NOT NULL CONSTRAINT DF_tblRoleCategory_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT)),
        RoleId          INT NOT NULL,
        AssetCategoryId INT NOT NULL,
        CreatedBy       NVARCHAR(100) NOT NULL,
        CreatedDate     DATETIME2(3)  NOT NULL CONSTRAINT DF_tblRoleCategory_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblRoleCategory PRIMARY KEY (RoleId, AssetCategoryId),
        -- the company is part of both keys: a role can only be limited to categories of its own company
        CONSTRAINT FK_tblRoleCategory_Role FOREIGN KEY (CompanyId, RoleId) REFERENCES dbo.tblRole (CompanyId, RoleId),
        CONSTRAINT FK_tblRoleCategory_Category FOREIGN KEY (CompanyId, AssetCategoryId) REFERENCES dbo.tblAssetCategory (CompanyId, AssetCategoryId)
    );
    PRINT 'Created dbo.tblRoleCategory';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblRoleCategory') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblRoleCategory,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblRoleCategory AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblRoleCategory AFTER UPDATE;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, DELETE ON dbo.tblRoleCategory TO amgh_app_role;   -- links are replaced when a role is saved
    DENY UPDATE ON dbo.tblRoleCategory TO amgh_app_role;
END
GO
/* ---------------------------------------------------------------------
   Release 2.9 fix: asset and licence numbers carry each company's own prefix and category code
   (AMGH-FA-00001, XYZ-IT-LAP-00001) and the series' separator (- / . _ or none). The original constraints only
   allowed AMGH-IT-..., which refused other companies and non-IT categories. Replaced by a company-neutral rule;
   existing numbers satisfy it.
   Idempotent: only acts while the old AMGH-only rule is in place.
   --------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblAsset_AssetNumber' AND parent_object_id = OBJECT_ID(N'dbo.tblAsset')
           AND definition LIKE N'%AMGH-IT-%')
BEGIN
    ALTER TABLE dbo.tblAsset DROP CONSTRAINT CK_tblAsset_AssetNumber;
    ALTER TABLE dbo.tblAsset WITH CHECK ADD CONSTRAINT CK_tblAsset_AssetNumber
        CHECK ([AssetNumber] NOT LIKE N'%[^A-Z0-9/._-]%' AND LEN([AssetNumber]) BETWEEN 3 AND 30);
    PRINT 'Asset number rule now company-neutral (CK_tblAsset_AssetNumber).';
END
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblSoftwareLicense_LicenseNumber' AND parent_object_id = OBJECT_ID(N'dbo.tblSoftwareLicense')
           AND definition LIKE N'%AMGH-IT-LIC-%')
BEGIN
    ALTER TABLE dbo.tblSoftwareLicense DROP CONSTRAINT CK_tblSoftwareLicense_LicenseNumber;
    ALTER TABLE dbo.tblSoftwareLicense WITH CHECK ADD CONSTRAINT CK_tblSoftwareLicense_LicenseNumber
        CHECK ([LicenseNumber] NOT LIKE N'%[^A-Z0-9/._-]%' AND [LicenseNumber] LIKE N'%LIC%' AND LEN([LicenseNumber]) BETWEEN 3 AND 30);
    PRINT 'Licence number rule now company-neutral (CK_tblSoftwareLicense_LicenseNumber).';
END
GO
PRINT 'Script 16 completed.';
GO
