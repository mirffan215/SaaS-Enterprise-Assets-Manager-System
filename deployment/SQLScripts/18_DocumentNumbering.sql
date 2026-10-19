/* =====================================================================
   EAMS - Release 2.13: document numbering (SAP-style document numbers), ERP references. Idempotent.
   * tblCompany: numbering settings (Continuous or Yearly, fiscal year start month, year format, digits)
   * tblDocumentSequence: per-company counters (per fiscal year in Yearly mode), company-isolated
   * Six document tables: DocumentNumber (unique per company) and ErpReference
   * dbo.usp_DocumentNumber_Next: the next counter value (same locking pattern as the asset numbers)
   * Existing documents are numbered (Continuous), per company, in the order they were created.
   The application formats the numbers: AMGH-MOV-000123, or AMGH-MOV-2026-000123 / AMGH-MOV-FY27-000123.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation')
    THROW 51800, N'Script 18 needs the multi-company foundation (script 10).', 1;
GO
-- ---------------------------------------------------------------- company settings
IF COL_LENGTH(N'dbo.tblCompany', N'DocNumberingMode') IS NULL
    ALTER TABLE dbo.tblCompany ADD DocNumberingMode NVARCHAR(10) NOT NULL CONSTRAINT DF_tblCompany_DocNumberingMode DEFAULT (N'Continuous')
        CONSTRAINT CK_tblCompany_DocNumberingMode CHECK (DocNumberingMode IN (N'Continuous', N'Yearly'));
IF COL_LENGTH(N'dbo.tblCompany', N'FiscalYearStartMonth') IS NULL
    ALTER TABLE dbo.tblCompany ADD FiscalYearStartMonth TINYINT NOT NULL CONSTRAINT DF_tblCompany_FiscalYearStartMonth DEFAULT (1)
        CONSTRAINT CK_tblCompany_FiscalYearStartMonth CHECK (FiscalYearStartMonth BETWEEN 1 AND 12);
IF COL_LENGTH(N'dbo.tblCompany', N'DocYearFormat') IS NULL
    ALTER TABLE dbo.tblCompany ADD DocYearFormat NVARCHAR(10) NOT NULL CONSTRAINT DF_tblCompany_DocYearFormat DEFAULT (N'Year')
        CONSTRAINT CK_tblCompany_DocYearFormat CHECK (DocYearFormat IN (N'Year', N'FY'));
IF COL_LENGTH(N'dbo.tblCompany', N'DocNumberDigits') IS NULL
    ALTER TABLE dbo.tblCompany ADD DocNumberDigits TINYINT NOT NULL CONSTRAINT DF_tblCompany_DocNumberDigits DEFAULT (6)
        CONSTRAINT CK_tblCompany_DocNumberDigits CHECK (DocNumberDigits BETWEEN 5 AND 8);
GO
-- ---------------------------------------------------------------- counters
IF OBJECT_ID(N'dbo.tblDocumentSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblDocumentSequence
    (
        CompanyId    INT NOT NULL CONSTRAINT DF_tblDocumentSequence_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                     CONSTRAINT FK_tblDocumentSequence_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        DocumentType NVARCHAR(10) NOT NULL CONSTRAINT CK_tblDocumentSequence_Type CHECK (DocumentType IN (N'MOV', N'MNT', N'DSP', N'ASG', N'VER', N'WRN')),
        Period       NVARCHAR(10) NOT NULL,          -- '' (Continuous) or the fiscal year label, e.g. '2026' / 'FY27'
        LastNumber   INT NOT NULL CONSTRAINT CK_tblDocumentSequence_LastNumber CHECK (LastNumber >= 0),
        ModifiedDate DATETIME2(3) NOT NULL CONSTRAINT DF_tblDocumentSequence_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblDocumentSequence PRIMARY KEY (CompanyId, DocumentType, Period)
    );
    PRINT 'Created dbo.tblDocumentSequence';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.tblDocumentSequence') AND predicate_type = 0)
    ALTER SECURITY POLICY sec.CompanyIsolation
        ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDocumentSequence,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDocumentSequence AFTER INSERT,
        ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.tblDocumentSequence AFTER UPDATE;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON dbo.tblDocumentSequence TO amgh_app_role;
    DENY DELETE ON dbo.tblDocumentSequence TO amgh_app_role;
    DENY UPDATE ON dbo.tblDocumentSequence (CompanyId) TO amgh_app_role;
END
GO
-- ---------------------------------------------------------------- the counter
CREATE OR ALTER PROCEDURE dbo.usp_DocumentNumber_Next
    @DocumentType NVARCHAR(10),
    @Period       NVARCHAR(10),
    @Next         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @CompanyId INT = CAST(SESSION_CONTEXT(N'CompanyId') AS INT);
    IF @CompanyId IS NULL THROW 51801, N'Document numbers need a company context.', 1;
    IF @DocumentType NOT IN (N'MOV', N'MNT', N'DSP', N'ASG', N'VER', N'WRN') THROW 51802, N'Unknown document type.', 1;
    SET @Period = ISNULL(@Period, N'');

    BEGIN TRANSACTION;   -- joins the caller's transaction: a rolled-back document never consumes a number
        UPDATE dbo.tblDocumentSequence WITH (UPDLOCK, HOLDLOCK)
           SET @Next = LastNumber = LastNumber + 1, ModifiedDate = SYSUTCDATETIME()
         WHERE CompanyId = @CompanyId AND DocumentType = @DocumentType AND Period = @Period;
        IF @@ROWCOUNT = 0
        BEGIN
            -- HOLDLOCK keeps the key range locked, so a concurrent first number waits instead of colliding
            INSERT dbo.tblDocumentSequence (CompanyId, DocumentType, Period, LastNumber) VALUES (@CompanyId, @DocumentType, @Period, 1);
            SET @Next = 1;
        END
    COMMIT TRANSACTION;
END
GO
-- ---------------------------------------------------------------- tblAssetMovement (MOV)
IF COL_LENGTH(N'dbo.tblAssetMovement', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblAssetMovement ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAssetMovement', N'ErpReference') IS NULL ALTER TABLE dbo.tblAssetMovement ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetMovement_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetMovement_DocumentNumber ON dbo.tblAssetMovement (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMovement_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblAssetMovement'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMovement_ErpReference ON dbo.tblAssetMovement (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.AssetMovementId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.AssetMovementId) AS n
    FROM dbo.tblAssetMovement d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'MOV' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-MOV-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblAssetMovement d JOIN todo t ON t.AssetMovementId = d.AssetMovementId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-MOV-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblAssetMovement WHERE DocumentNumber LIKE N'%-MOV-[0-9]%' AND DocumentNumber NOT LIKE N'%-MOV-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'MOV' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'MOV', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblAssetMovement: numbered ', @numbered, ' existing document(s)');
GO
-- ---------------------------------------------------------------- tblAssetMaintenance (MNT)
IF COL_LENGTH(N'dbo.tblAssetMaintenance', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblAssetMaintenance ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAssetMaintenance', N'ErpReference') IS NULL ALTER TABLE dbo.tblAssetMaintenance ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetMaintenance_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetMaintenance_DocumentNumber ON dbo.tblAssetMaintenance (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_ErpReference ON dbo.tblAssetMaintenance (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.AssetMaintenanceId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.AssetMaintenanceId) AS n
    FROM dbo.tblAssetMaintenance d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'MNT' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-MNT-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblAssetMaintenance d JOIN todo t ON t.AssetMaintenanceId = d.AssetMaintenanceId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-MNT-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblAssetMaintenance WHERE DocumentNumber LIKE N'%-MNT-[0-9]%' AND DocumentNumber NOT LIKE N'%-MNT-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'MNT' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'MNT', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblAssetMaintenance: numbered ', @numbered, ' existing document(s)');
GO
-- ---------------------------------------------------------------- tblAssetDisposal (DSP)
IF COL_LENGTH(N'dbo.tblAssetDisposal', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblAssetDisposal ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAssetDisposal', N'ErpReference') IS NULL ALTER TABLE dbo.tblAssetDisposal ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetDisposal_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetDisposal_DocumentNumber ON dbo.tblAssetDisposal (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetDisposal_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblAssetDisposal'))
    CREATE NONCLUSTERED INDEX IX_tblAssetDisposal_ErpReference ON dbo.tblAssetDisposal (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.AssetDisposalId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.AssetDisposalId) AS n
    FROM dbo.tblAssetDisposal d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'DSP' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-DSP-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblAssetDisposal d JOIN todo t ON t.AssetDisposalId = d.AssetDisposalId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-DSP-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblAssetDisposal WHERE DocumentNumber LIKE N'%-DSP-[0-9]%' AND DocumentNumber NOT LIKE N'%-DSP-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'DSP' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'DSP', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblAssetDisposal: numbered ', @numbered, ' existing document(s)');
GO
-- ---------------------------------------------------------------- tblAssetAssignment (ASG)
IF COL_LENGTH(N'dbo.tblAssetAssignment', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblAssetAssignment ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAssetAssignment', N'ErpReference') IS NULL ALTER TABLE dbo.tblAssetAssignment ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetAssignment_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetAssignment_DocumentNumber ON dbo.tblAssetAssignment (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAssignment_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblAssetAssignment'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAssignment_ErpReference ON dbo.tblAssetAssignment (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.AssetAssignmentId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.AssetAssignmentId) AS n
    FROM dbo.tblAssetAssignment d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'ASG' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-ASG-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblAssetAssignment d JOIN todo t ON t.AssetAssignmentId = d.AssetAssignmentId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-ASG-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblAssetAssignment WHERE DocumentNumber LIKE N'%-ASG-[0-9]%' AND DocumentNumber NOT LIKE N'%-ASG-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'ASG' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'ASG', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblAssetAssignment: numbered ', @numbered, ' existing document(s)');
GO
-- ---------------------------------------------------------------- tblAssetAudit (VER)
IF COL_LENGTH(N'dbo.tblAssetAudit', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblAssetAudit ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblAssetAudit', N'ErpReference') IS NULL ALTER TABLE dbo.tblAssetAudit ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAssetAudit_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAssetAudit_DocumentNumber ON dbo.tblAssetAudit (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetAudit_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblAssetAudit'))
    CREATE NONCLUSTERED INDEX IX_tblAssetAudit_ErpReference ON dbo.tblAssetAudit (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.AssetAuditId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.AssetAuditId) AS n
    FROM dbo.tblAssetAudit d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'VER' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-VER-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblAssetAudit d JOIN todo t ON t.AssetAuditId = d.AssetAuditId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-VER-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblAssetAudit WHERE DocumentNumber LIKE N'%-VER-[0-9]%' AND DocumentNumber NOT LIKE N'%-VER-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'VER' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'VER', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblAssetAudit: numbered ', @numbered, ' existing document(s)');
GO
-- ---------------------------------------------------------------- tblWarranty (WRN)
IF COL_LENGTH(N'dbo.tblWarranty', N'DocumentNumber') IS NULL ALTER TABLE dbo.tblWarranty ADD DocumentNumber NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.tblWarranty', N'ErpReference') IS NULL ALTER TABLE dbo.tblWarranty ADD ErpReference NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblWarranty_DocumentNumber' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblWarranty_DocumentNumber ON dbo.tblWarranty (CompanyId, DocumentNumber) WHERE DocumentNumber IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblWarranty_ErpReference' AND object_id = OBJECT_ID(N'dbo.tblWarranty'))
    CREATE NONCLUSTERED INDEX IX_tblWarranty_ErpReference ON dbo.tblWarranty (CompanyId, ErpReference) WHERE ErpReference IS NOT NULL;
GO
-- number the documents that have none yet (Continuous, continuing each company's counter), in creation order
;WITH todo AS (
    SELECT d.WarrantyId, d.CompanyId, c.CompanyCode, c.DocNumberDigits,
           ISNULL(s.LastNumber, 0) + ROW_NUMBER() OVER (PARTITION BY d.CompanyId ORDER BY d.WarrantyId) AS n
    FROM dbo.tblWarranty d
    JOIN dbo.tblCompany c ON c.CompanyId = d.CompanyId
    LEFT JOIN dbo.tblDocumentSequence s ON s.CompanyId = d.CompanyId AND s.DocumentType = N'WRN' AND s.Period = N''
    WHERE d.DocumentNumber IS NULL)
UPDATE d SET DocumentNumber = t.CompanyCode + N'-WRN-' +
       CASE WHEN LEN(CAST(t.n AS NVARCHAR(12))) >= t.DocNumberDigits THEN CAST(t.n AS NVARCHAR(12))
            ELSE RIGHT(REPLICATE(N'0', t.DocNumberDigits) + CAST(t.n AS NVARCHAR(12)), t.DocNumberDigits) END
FROM dbo.tblWarranty d JOIN todo t ON t.WarrantyId = d.WarrantyId;
DECLARE @numbered INT = @@ROWCOUNT;
MERGE dbo.tblDocumentSequence AS s
USING (SELECT CompanyId, MAX(TRY_CAST(SUBSTRING(DocumentNumber, CHARINDEX(N'-WRN-', DocumentNumber) + 5, 20) AS INT)) AS Cnt
       FROM dbo.tblWarranty WHERE DocumentNumber LIKE N'%-WRN-[0-9]%' AND DocumentNumber NOT LIKE N'%-WRN-%-%' GROUP BY CompanyId) AS x
   ON s.CompanyId = x.CompanyId AND s.DocumentType = N'WRN' AND s.Period = N''
WHEN MATCHED AND x.Cnt IS NOT NULL AND s.LastNumber < x.Cnt THEN UPDATE SET LastNumber = CAST(x.Cnt AS INT), ModifiedDate = SYSUTCDATETIME()
WHEN NOT MATCHED AND x.Cnt IS NOT NULL THEN INSERT (CompanyId, DocumentType, Period, LastNumber) VALUES (x.CompanyId, N'WRN', N'', CAST(x.Cnt AS INT));
IF @numbered > 0 PRINT CONCAT('tblWarranty: numbered ', @numbered, ' existing document(s)');
GO
PRINT 'Script 18 completed.';
GO
