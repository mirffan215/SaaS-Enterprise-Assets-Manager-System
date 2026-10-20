/* =====================================================================
   0002 - RFID tags and the invoice number of maintenance work. Safe to run twice.
   * dbo.tblAsset.RfidEpc: the 96-bit EPC (24 hex characters) written to the asset's RFID tag, generated when an RFID label is first made.
   * label design "RfidLabel" in the print log (check constraint and usp_QrLabel_LogPrint).
   * dbo.tblAssetMaintenance.InvoiceNumber: the vendor invoice behind a repair / maintenance cost.
   ===================================================================== */
SET NOCOUNT ON;
GO
USE [$(DatabaseName)];
GO
IF COL_LENGTH('dbo.tblAsset', 'RfidEpc') IS NULL
    ALTER TABLE dbo.tblAsset ADD RfidEpc CHAR(24) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblAsset_RfidEpc' AND object_id = OBJECT_ID(N'dbo.tblAsset'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblAsset_RfidEpc ON dbo.tblAsset (RfidEpc) WHERE RfidEpc IS NOT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblQrLabelPrintLog_LabelDesign_Domain' AND definition NOT LIKE N'%RfidLabel%')
BEGIN
    ALTER TABLE dbo.tblQrLabelPrintLog DROP CONSTRAINT CK_tblQrLabelPrintLog_LabelDesign_Domain;
    ALTER TABLE dbo.tblQrLabelPrintLog ADD CONSTRAINT CK_tblQrLabelPrintLog_LabelDesign_Domain CHECK ([LabelDesign] IN (N'QrLabel', N'AssetTag', N'RfidLabel'));
END
GO
IF COL_LENGTH('dbo.tblAssetMaintenance', 'InvoiceNumber') IS NULL
    ALTER TABLE dbo.tblAssetMaintenance ADD InvoiceNumber NVARCHAR(50) NULL;
GO
CREATE OR ALTER PROCEDURE dbo.usp_QrLabel_LogPrint
    @AssetQRCodeId INT,
    @LabelSize     NVARCHAR(30),
    @Copies        SMALLINT = 1,
    @PrintBatchId  UNIQUEIDENTIFIER,
    @IsReprint     BIT = 0,
    @ReprintReason NVARCHAR(500) = NULL,
    @PrintedBy     NVARCHAR(100),
    @LabelDesign   NVARCHAR(30) = N'QrLabel'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @AssetId INT, @Version INT;
        SELECT @AssetId = AssetId, @Version = QrVersion FROM dbo.tblAssetQRCode
        WHERE AssetQRCodeId = @AssetQRCodeId AND IsDeleted = 0;
        IF @AssetId IS NULL THROW 50001, N'QR code not found.', 1;

        INSERT dbo.tblQrLabelPrintLog (AssetQRCodeId, AssetId, LabelSize, LabelDesign, Copies, PrintBatchId, IsReprint, ReprintReason, PrintedBy)
        VALUES (@AssetQRCodeId, @AssetId, @LabelSize, @LabelDesign, @Copies, @PrintBatchId, @IsReprint, @ReprintReason, @PrintedBy);

        INSERT dbo.tblAssetHistory (AssetId, EventType, NewValue, Remarks, PerformedBy)
        VALUES (@AssetId, N'LabelPrinted',
                CONCAT(CASE @LabelDesign WHEN N'AssetTag' THEN N'Asset tag ' WHEN N'RfidLabel' THEN N'RFID label ' ELSE N'QR label ' END, @LabelSize, N' x', @Copies),
                CONCAT(N'QR v', @Version, CASE WHEN @IsReprint = 1 THEN CONCAT(N' (reprint: ', @ReprintReason, N')') ELSE N'' END),
                @PrintedBy);
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
