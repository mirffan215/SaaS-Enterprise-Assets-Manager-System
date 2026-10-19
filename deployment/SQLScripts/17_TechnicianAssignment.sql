/* =====================================================================
   EAMS - Release 2.10: technician assignment for maintenance and repairs.
   Idempotent; ADDS one column, its foreign key and an index. No existing data is changed:
   existing jobs keep their free-text "Technician" (PerformedBy) and have no assigned user.
     * tblAssetMaintenance.AssignedToUserId - the technician (a user account) the job is assigned to.
       The application only allows active members of the job's company who may manage maintenance.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'dbo.tblAssetMaintenance', N'AssignedToUserId') IS NULL
BEGIN
    ALTER TABLE dbo.tblAssetMaintenance ADD AssignedToUserId INT NULL;
    PRINT 'Added tblAssetMaintenance.AssignedToUserId';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblAssetMaintenance_AssignedToUserId')
BEGIN
    ALTER TABLE dbo.tblAssetMaintenance WITH CHECK ADD CONSTRAINT FK_tblAssetMaintenance_AssignedToUserId
        FOREIGN KEY (AssignedToUserId) REFERENCES dbo.tblUser (UserId);
    PRINT 'Added FK_tblAssetMaintenance_AssignedToUserId';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblAssetMaintenance_AssignedToUserId' AND object_id = OBJECT_ID(N'dbo.tblAssetMaintenance'))
BEGIN
    -- "My jobs": open jobs of one technician within a company
    CREATE NONCLUSTERED INDEX IX_tblAssetMaintenance_AssignedToUserId
        ON dbo.tblAssetMaintenance (CompanyId, AssignedToUserId, MaintenanceStatus)
        WHERE AssignedToUserId IS NOT NULL;
    PRINT 'Added IX_tblAssetMaintenance_AssignedToUserId';
END
GO
PRINT 'Script 17 completed.';
GO
