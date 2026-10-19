/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 06 - Triggers  (Release 2.0: requires script 10 - CompanyId on company tables) (integrity rules that must hold regardless of
   which application or person writes to the database)
   NOTE for EF Core: every table with a trigger must be configured with
   .ToTable(t => t.HasTrigger("...")) so EF does not use OUTPUT clauses.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------------------------------------------- Number integrity */
CREATE OR ALTER TRIGGER dbo.trg_tblAssetNumberSequence_Protect
ON dbo.tblAssetNumberSequence
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.AssetNumberSequenceId = i.AssetNumberSequenceId
               WHERE i.LastNumber < d.LastNumber)
        THROW 51001, N'Asset number sequences cannot be decreased - numbers must never be reused.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.AssetNumberSequenceId = i.AssetNumberSequenceId
               WHERE d.LastNumber > 0 AND (i.SequenceCode <> d.SequenceCode OR i.Prefix <> d.Prefix))
        THROW 51002, N'Sequence code and prefix cannot change once numbers have been issued.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblAssetNumberSequence_NoDelete
ON dbo.tblAssetNumberSequence
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51003, N'Asset number sequences cannot be deleted. Set IsActive = 0 instead.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblAsset_NoHardDelete
ON dbo.tblAsset
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51004, N'Assets cannot be physically deleted. Use soft delete (IsDeleted = 1).', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblSoftwareLicense_NoHardDelete
ON dbo.tblSoftwareLicense
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51005, N'Software licenses cannot be physically deleted. Use soft delete (IsDeleted = 1).', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblSoftwareLicense_ProtectNumber
ON dbo.tblSoftwareLicense
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(LicenseNumber) AND EXISTS (SELECT 1 FROM inserted i JOIN deleted d
                                         ON d.SoftwareLicenseId = i.SoftwareLicenseId
                                         WHERE i.LicenseNumber <> d.LicenseNumber)
        THROW 51006, N'License numbers are permanent and cannot be changed.', 1;
END
GO

/* ---------------------------------------------- Asset history */
CREATE OR ALTER TRIGGER dbo.trg_tblAsset_History
ON dbo.tblAsset
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    -- Business-number protection runs even when history is suppressed.
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.AssetId = i.AssetId
               WHERE i.AssetNumber <> d.AssetNumber)
        THROW 51007, N'Asset numbers are permanent and cannot be changed.', 1;

    IF CAST(SESSION_CONTEXT(N'AMGH.SuppressAssetHistory') AS INT) = 1 RETURN;

    -- History rows take the asset's own company (never the session's), so maintenance by a DBA keeps ownership.
    -- INSERT
    INSERT dbo.tblAssetHistory (CompanyId, AssetId, EventType, NewValue, PerformedBy, EventDate)
    SELECT i.CompanyId, i.AssetId, N'Created', i.AssetNumber, i.CreatedBy, SYSUTCDATETIME()
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted);

    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    -- UPDATE: soft delete / restore
    INSERT dbo.tblAssetHistory (CompanyId, AssetId, EventType, PerformedBy, EventDate)
    SELECT i.CompanyId, i.AssetId, CASE WHEN i.IsDeleted = 1 THEN N'Deleted' ELSE N'Restored' END,
           COALESCE(i.DeletedBy, i.ModifiedBy, N'SYSTEM'), SYSUTCDATETIME()
    FROM inserted i JOIN deleted d ON d.AssetId = i.AssetId
    WHERE i.IsDeleted <> d.IsDeleted;

    -- UPDATE: status change (with readable codes)
    INSERT dbo.tblAssetHistory (CompanyId, AssetId, EventType, FieldName, OldValue, NewValue, PerformedBy, EventDate)
    SELECT i.CompanyId, i.AssetId, N'StatusChanged', N'AssetStatusId', so.StatusCode, sn.StatusCode,
           ISNULL(i.ModifiedBy, N'SYSTEM'), SYSUTCDATETIME()
    FROM inserted i
    JOIN deleted d ON d.AssetId = i.AssetId
    JOIN dbo.tblAssetStatus so ON so.AssetStatusId = d.AssetStatusId
    JOIN dbo.tblAssetStatus sn ON sn.AssetStatusId = i.AssetStatusId
    WHERE i.AssetStatusId <> d.AssetStatusId;

    -- UPDATE: tracked fields
    INSERT dbo.tblAssetHistory (CompanyId, AssetId, EventType, FieldName, OldValue, NewValue, PerformedBy, EventDate)
    SELECT i.CompanyId, i.AssetId, N'Updated', x.FieldName, x.OldValue, x.NewValue, ISNULL(i.ModifiedBy, N'SYSTEM'), SYSUTCDATETIME()
    FROM inserted i
    JOIN deleted d ON d.AssetId = i.AssetId
    CROSS APPLY (VALUES
        (N'AssetName',          CAST(d.AssetName AS NVARCHAR(1000)),          CAST(i.AssetName AS NVARCHAR(1000))),
        (N'SerialNumber',       d.SerialNumber,                               i.SerialNumber),
        (N'Hostname',           d.Hostname,                                   i.Hostname),
        (N'IpAddress',          d.IpAddress,                                  i.IpAddress),
        (N'AssetCategoryId',    CAST(d.AssetCategoryId AS NVARCHAR(20)),      CAST(i.AssetCategoryId AS NVARCHAR(20))),
        (N'AssignedEmployeeId', CAST(d.AssignedEmployeeId AS NVARCHAR(20)),   CAST(i.AssignedEmployeeId AS NVARCHAR(20))),
        (N'DepartmentId',       CAST(d.DepartmentId AS NVARCHAR(20)),         CAST(i.DepartmentId AS NVARCHAR(20))),
        (N'LocationId',         CAST(d.LocationId AS NVARCHAR(20)),           CAST(i.LocationId AS NVARCHAR(20))),
        (N'RoomId',             CAST(d.RoomId AS NVARCHAR(20)),               CAST(i.RoomId AS NVARCHAR(20))),
        (N'AssetOwnerEmployeeId', CAST(d.AssetOwnerEmployeeId AS NVARCHAR(20)), CAST(i.AssetOwnerEmployeeId AS NVARCHAR(20))),
        (N'ClassificationId',   CAST(d.ClassificationId AS NVARCHAR(20)),     CAST(i.ClassificationId AS NVARCHAR(20))),
        (N'CriticalityId',      CAST(d.CriticalityId AS NVARCHAR(20)),        CAST(i.CriticalityId AS NVARCHAR(20))),
        (N'RiskLevelId',        CAST(d.RiskLevelId AS NVARCHAR(20)),          CAST(i.RiskLevelId AS NVARCHAR(20))),
        (N'IsDiskEncrypted',    CAST(d.IsDiskEncrypted AS NVARCHAR(5)),       CAST(i.IsDiskEncrypted AS NVARCHAR(5))),
        (N'EndpointProtectionInstalled', CAST(d.EndpointProtectionInstalled AS NVARCHAR(5)), CAST(i.EndpointProtectionInstalled AS NVARCHAR(5))),
        (N'StoresSensitiveData', CAST(d.StoresSensitiveData AS NVARCHAR(5)),  CAST(i.StoresSensitiveData AS NVARCHAR(5))),
        (N'PurchaseCost',       CAST(d.PurchaseCost AS NVARCHAR(30)),         CAST(i.PurchaseCost AS NVARCHAR(30)))
    ) x (FieldName, OldValue, NewValue)
    WHERE ISNULL(x.OldValue, N'<null>') <> ISNULL(x.NewValue, N'<null>');
END
GO

/* ---------------------------------------------- Immutable logs (ISO 27001 A.8.15) */
CREATE OR ALTER TRIGGER dbo.trg_tblAssetHistory_Immutable
ON dbo.tblAssetHistory INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51010, N'Asset history is append-only.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblAuditLog_Immutable
ON dbo.tblAuditLog INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51011, N'The audit log is append-only.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblLoginAudit_Immutable
ON dbo.tblLoginAudit INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51012, N'The login audit log is append-only.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblQrLabelPrintLog_Immutable
ON dbo.tblQrLabelPrintLog INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51013, N'The label print log is append-only.', 1;
END
GO

/* ---------------------------------------------- License seat limit */
CREATE OR ALTER TRIGGER dbo.trg_tblSoftwareLicenseAllocation_SeatLimit
ON dbo.tblSoftwareLicenseAllocation
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT SoftwareLicenseId FROM inserted) x
        JOIN dbo.tblSoftwareLicense l ON l.SoftwareLicenseId = x.SoftwareLicenseId
        CROSS APPLY (SELECT COUNT(*) AS Used FROM dbo.tblSoftwareLicenseAllocation a
                     WHERE a.SoftwareLicenseId = x.SoftwareLicenseId AND a.IsCurrent = 1 AND a.IsDeleted = 0) u
        WHERE l.TotalSeats IS NOT NULL AND u.Used > l.TotalSeats)
        THROW 51020, N'Allocation exceeds the number of licensed seats.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.tblSoftwareLicense l ON l.SoftwareLicenseId = i.SoftwareLicenseId
               WHERE i.IsCurrent = 1 AND (l.IsActive = 0 OR l.IsDeleted = 1))
        THROW 51021, N'Seats cannot be allocated from an inactive license.', 1;
END
GO

/* ---------------------------------------------- Disposal evidence (defence in depth) */
CREATE OR ALTER TRIGGER dbo.trg_tblAssetDisposal_Evidence
ON dbo.tblAssetDisposal
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.tblDisposalMethod m ON m.DisposalMethodId = i.DisposalMethodId
               WHERE i.DisposalStatus = N'Completed' AND m.RequiresDataSanitization = 1
                 AND (i.DataSanitizationMethod IS NULL OR i.DataSanitizationDate IS NULL OR i.DataSanitizationVerifiedBy IS NULL))
        THROW 51030, N'Completed disposal requires data sanitisation evidence for this method.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.tblDisposalMethod m ON m.DisposalMethodId = i.DisposalMethodId
               WHERE i.DisposalStatus = N'Completed' AND m.RequiresCertificate = 1 AND i.CertificateNumber IS NULL)
        THROW 51031, N'Completed disposal requires a certificate number for this method.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.AssetDisposalId = i.AssetDisposalId
               WHERE d.DisposalStatus IN (N'Completed', N'Rejected', N'Cancelled') AND i.DisposalStatus <> d.DisposalStatus)
        THROW 51032, N'A closed disposal record cannot be reopened.', 1;
END
GO

/* ---------------------------------------------- Security model protection */
CREATE OR ALTER TRIGGER dbo.trg_tblRole_ProtectSystemRoles
ON dbo.tblRole
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.RoleId = i.RoleId
               WHERE d.IsSystemRole = 1 AND (i.IsDeleted = 1 OR i.IsActive = 0 OR i.RoleName <> d.RoleName OR i.IsSystemRole = 0))
        THROW 51040, N'System roles cannot be renamed, deactivated or deleted.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblRole_NoHardDelete
ON dbo.tblRole
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51041, N'Roles cannot be physically deleted. Use soft delete.', 1;
END
GO

/* Keep at least one active System Administrator at all times. */
CREATE OR ALTER TRIGGER dbo.trg_tblUserRole_KeepAdmin
ON dbo.tblUserRole
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.tblUserRole ur
                   JOIN dbo.tblRole r ON r.RoleId = ur.RoleId AND r.RoleName = N'SystemAdministrator'
                   JOIN dbo.tblUser u ON u.UserId = ur.UserId AND u.IsActive = 1 AND u.IsDeleted = 0
                   WHERE ur.IsDeleted = 0)
        THROW 51042, N'At least one active System Administrator must remain.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_tblUser_KeepAdmin
ON dbo.tblUser
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF (UPDATE(IsActive) OR UPDATE(IsDeleted))
       AND NOT EXISTS (SELECT 1 FROM dbo.tblUserRole ur
                       JOIN dbo.tblRole r ON r.RoleId = ur.RoleId AND r.RoleName = N'SystemAdministrator'
                       JOIN dbo.tblUser u ON u.UserId = ur.UserId AND u.IsActive = 1 AND u.IsDeleted = 0
                       WHERE ur.IsDeleted = 0)
        THROW 51043, N'At least one active System Administrator must remain.', 1;
END
GO
PRINT 'Script 06 completed.';
GO
