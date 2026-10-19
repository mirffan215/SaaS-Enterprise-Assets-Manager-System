/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 05 - Stored procedures
   Error numbers raised:
     50001 Not found          50002 Invalid state for operation
     50003 Validation failed  50004 Segregation-of-duties violation
   Lifecycle procedures write their own tblAssetHistory events and set
   SESSION_CONTEXT 'AMGH.SuppressAssetHistory' so the generic asset
   history trigger does not log the same change twice.
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ------------------------------------------------------------------
   Asset / license number generation. Numbers are never reused:
   LastNumber only increases (trigger-enforced) and assets/licenses
   are never hard-deleted (trigger-enforced).
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_AssetNumber_Generate
    @SequenceCode NVARCHAR(10),
    @AssetNumber  NVARCHAR(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Next BIGINT, @Prefix NVARCHAR(20), @Pad TINYINT, @Separator NVARCHAR(1);

    BEGIN TRAN;
    WHILE 1 = 1
    BEGIN
        UPDATE dbo.tblAssetNumberSequence WITH (ROWLOCK, UPDLOCK)
           SET @Next = LastNumber = LastNumber + 1,
               @Prefix = Prefix,
               @Pad = PaddingLength,
               @Separator = Separator,
               ModifiedBy = N'SYSTEM',
               ModifiedDate = SYSUTCDATETIME()
         WHERE SequenceCode = @SequenceCode AND IsActive = 1 AND IsDeleted = 0;

        IF @@ROWCOUNT = 0
        BEGIN
            IF @@TRANCOUNT > 0 ROLLBACK;
            THROW 50001, N'Asset number sequence not found or inactive.', 1;
        END

        SET @AssetNumber = dbo.fn_FormatAssetNumber(@Prefix, @SequenceCode, @Next, @Pad, @Separator);

        -- Defensive: skip any number already present (e.g. imported legacy data).
        IF NOT EXISTS (SELECT 1 FROM dbo.tblAsset WHERE AssetNumber = @AssetNumber)
           AND NOT EXISTS (SELECT 1 FROM dbo.tblSoftwareLicense WHERE LicenseNumber = @AssetNumber)
            BREAK;
    END
    COMMIT;
END
GO

/* ------------------------------------------------------------------
   Assignment
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_Asset_Assign
    @AssetId               INT,
    @EmployeeId            INT = NULL,
    @DepartmentId          INT = NULL,
    @LocationId            INT = NULL,
    @AssignmentType        NVARCHAR(30) = N'Permanent',
    @ExpectedReturnDate    DATE = NULL,
    @ConditionAtAssignment NVARCHAR(200) = NULL,
    @Notes                 NVARCHAR(1000) = NULL,
    @PerformedBy           NVARCHAR(100),
    @AssetAssignmentId     INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        IF @EmployeeId IS NULL AND @DepartmentId IS NULL
            THROW 50003, N'An asset must be assigned to an employee or a department.', 1;

        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @Assignable BIT, @Terminal BIT, @AssetLocationId INT;
        SELECT @Assignable = s.IsAssignable, @Terminal = s.IsTerminal, @AssetLocationId = a.LocationId
        FROM dbo.tblAsset a WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId
        WHERE a.AssetId = @AssetId AND a.IsDeleted = 0;

        IF @Assignable IS NULL THROW 50001, N'Asset not found.', 1;
        IF @Assignable = 0 THROW 50002, N'The asset''s current status does not allow assignment.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tblAssetAssignment WHERE AssetId = @AssetId AND IsCurrent = 1 AND IsDeleted = 0)
            THROW 50002, N'The asset already has a current assignment. Return it first.', 1;

        DECLARE @EmpDept INT, @EmpLoc INT, @EmpName NVARCHAR(201);
        IF @EmployeeId IS NOT NULL
        BEGIN
            SELECT @EmpDept = DepartmentId, @EmpLoc = LocationId, @EmpName = FullName
            FROM dbo.tblEmployee
            WHERE EmployeeId = @EmployeeId AND IsDeleted = 0 AND EmploymentStatus <> N'Terminated';
            IF @EmpDept IS NULL THROW 50003, N'Employee not found or terminated.', 1;
        END

        SET @DepartmentId = COALESCE(@DepartmentId, @EmpDept);
        SET @LocationId   = COALESCE(@LocationId, @EmpLoc, @AssetLocationId);

        INSERT dbo.tblAssetAssignment
            (AssetId, EmployeeId, DepartmentId, LocationId, AssignmentType, AssignedDate, ExpectedReturnDate,
             ConditionAtAssignment, IsCurrent, AssignedBy, Notes, CreatedBy)
        VALUES
            (@AssetId, @EmployeeId, @DepartmentId, @LocationId, @AssignmentType, SYSUTCDATETIME(), @ExpectedReturnDate,
             @ConditionAtAssignment, 1, @PerformedBy, @Notes, @PerformedBy);
        SET @AssetAssignmentId = SCOPE_IDENTITY();

        UPDATE dbo.tblAsset
           SET AssignedEmployeeId = @EmployeeId,
               DepartmentId = @DepartmentId,
               LocationId = @LocationId,
               AssetStatusId = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'ASSIGNED'),
               ModifiedBy = @PerformedBy,
               ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, FieldName, NewValue, Remarks, PerformedBy)
        VALUES (@AssetId, N'Assigned', N'AssignedEmployeeId',
                COALESCE(@EmpName, (SELECT DepartmentName FROM dbo.tblDepartment WHERE DepartmentId = @DepartmentId)),
                CONCAT(N'Assignment #', @AssetAssignmentId, N' (', @AssignmentType, N')'), @PerformedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Asset_Return
    @AssetId           INT,
    @ConditionAtReturn NVARCHAR(200) = NULL,
    @ReturnStatusCode  NVARCHAR(20) = N'IN_STOCK',
    @Notes             NVARCHAR(1000) = NULL,
    @PerformedBy       NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        DECLARE @ReturnStatusId INT = (SELECT AssetStatusId FROM dbo.tblAssetStatus
                                       WHERE StatusCode = @ReturnStatusCode AND IsTerminal = 0 AND IsDeleted = 0);
        IF @ReturnStatusId IS NULL THROW 50003, N'Invalid return status.', 1;

        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @AssignmentId INT, @EmpName NVARCHAR(201);
        SELECT @AssignmentId = aa.AssetAssignmentId, @EmpName = e.FullName
        FROM dbo.tblAssetAssignment aa WITH (UPDLOCK, HOLDLOCK)
        LEFT JOIN dbo.tblEmployee e ON e.EmployeeId = aa.EmployeeId
        WHERE aa.AssetId = @AssetId AND aa.IsCurrent = 1 AND aa.IsDeleted = 0;

        IF @AssignmentId IS NULL THROW 50002, N'The asset has no current assignment.', 1;

        UPDATE dbo.tblAssetAssignment
           SET IsCurrent = 0,
               ReturnedDate = SYSUTCDATETIME(),
               ConditionAtReturn = @ConditionAtReturn,
               ReturnedTo = @PerformedBy,
               Notes = COALESCE(@Notes, Notes),
               ModifiedBy = @PerformedBy,
               ModifiedDate = SYSUTCDATETIME()
         WHERE AssetAssignmentId = @AssignmentId;

        UPDATE dbo.tblAsset
           SET AssignedEmployeeId = NULL,
               AssetStatusId = @ReturnStatusId,
               ModifiedBy = @PerformedBy,
               ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, FieldName, OldValue, NewValue, Remarks, PerformedBy)
        VALUES (@AssetId, N'Returned', N'AssignedEmployeeId', @EmpName, NULL,
                CONCAT(N'Assignment #', @AssignmentId, N' closed. Condition: ', ISNULL(@ConditionAtReturn, N'n/a')),
                @PerformedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetAssignment_Acknowledge
    @AssetAssignmentId INT,
    @PerformedBy       NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tblAssetAssignment
       SET AcknowledgementReceived = 1,
           AcknowledgementDate = SYSUTCDATETIME(),
           ModifiedBy = @PerformedBy,
           ModifiedDate = SYSUTCDATETIME()
     WHERE AssetAssignmentId = @AssetAssignmentId AND IsCurrent = 1 AND IsDeleted = 0
       AND AcknowledgementReceived = 0;
    IF @@ROWCOUNT = 0
        THROW 50002, N'Assignment not found, not current, or already acknowledged.', 1;
END
GO

/* ------------------------------------------------------------------
   Movement (Change Management: request -> approve -> complete)
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_AssetMovement_Request
    @AssetId             INT,
    @ToLocationId        INT,
    @ToBuildingId        INT = NULL,
    @ToFloorId           INT = NULL,
    @ToRoomId            INT = NULL,
    @MovementType        NVARCHAR(30) = N'Relocation',
    @MovementReason      NVARCHAR(500),
    @ChangeRequestNumber NVARCHAR(50) = NULL,
    @RequestedBy         NVARCHAR(100),
    @AssetMovementId     INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @Terminal BIT;
        SELECT @Terminal = s.IsTerminal FROM dbo.tblAsset a
        JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId
        WHERE a.AssetId = @AssetId AND a.IsDeleted = 0;
        IF @Terminal IS NULL THROW 50001, N'Asset not found.', 1;
        IF @Terminal = 1 THROW 50002, N'Disposed assets cannot be moved.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tblAssetMovement WHERE AssetId = @AssetId AND IsDeleted = 0
                   AND MovementStatus IN (N'Requested', N'Approved'))
            THROW 50002, N'The asset already has an open movement request.', 1;

        -- Room must belong to the floor, floor to building, building to location.
        IF @ToRoomId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tblRoom WHERE RoomId = @ToRoomId AND FloorId = @ToFloorId)
            THROW 50003, N'Destination room does not belong to the selected floor.', 1;
        IF @ToFloorId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tblFloor WHERE FloorId = @ToFloorId AND BuildingId = @ToBuildingId)
            THROW 50003, N'Destination floor does not belong to the selected building.', 1;
        IF @ToBuildingId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tblBuilding WHERE BuildingId = @ToBuildingId AND LocationId = @ToLocationId)
            THROW 50003, N'Destination building does not belong to the selected location.', 1;

        INSERT dbo.tblAssetMovement
            (AssetId, FromLocationId, FromBuildingId, FromFloorId, FromRoomId,
             ToLocationId, ToBuildingId, ToFloorId, ToRoomId, MovementType, MovementStatus,
             MovementReason, ChangeRequestNumber, RequestedBy, CreatedBy)
        SELECT AssetId, LocationId, BuildingId, FloorId, RoomId,
               @ToLocationId, @ToBuildingId, @ToFloorId, @ToRoomId, @MovementType, N'Requested',
               @MovementReason, @ChangeRequestNumber, @RequestedBy, @RequestedBy
        FROM dbo.tblAsset WHERE AssetId = @AssetId;
        SET @AssetMovementId = SCOPE_IDENTITY();
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetMovement_Approve
    @AssetMovementId INT,
    @ApprovedBy      NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Status NVARCHAR(30), @RequestedBy NVARCHAR(100);
    SELECT @Status = MovementStatus, @RequestedBy = RequestedBy
    FROM dbo.tblAssetMovement WHERE AssetMovementId = @AssetMovementId AND IsDeleted = 0;
    IF @Status IS NULL THROW 50001, N'Movement request not found.', 1;
    IF @Status <> N'Requested' THROW 50002, N'Only requested movements can be approved.', 1;
    IF @RequestedBy = @ApprovedBy THROW 50004, N'A movement cannot be approved by the person who requested it.', 1;

    UPDATE dbo.tblAssetMovement
       SET MovementStatus = N'Approved', ApprovedBy = @ApprovedBy, ApprovedDate = SYSUTCDATETIME(),
           ModifiedBy = @ApprovedBy, ModifiedDate = SYSUTCDATETIME()
     WHERE AssetMovementId = @AssetMovementId AND MovementStatus = N'Requested';
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetMovement_Reject
    @AssetMovementId INT,
    @RejectedBy      NVARCHAR(100),
    @RejectionReason NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@RejectionReason)), N'') IS NULL
        THROW 50003, N'A rejection reason is required.', 1;
    UPDATE dbo.tblAssetMovement
       SET MovementStatus = N'Rejected', RejectionReason = @RejectionReason,
           ModifiedBy = @RejectedBy, ModifiedDate = SYSUTCDATETIME()
     WHERE AssetMovementId = @AssetMovementId AND MovementStatus = N'Requested' AND IsDeleted = 0;
    IF @@ROWCOUNT = 0 THROW 50002, N'Movement not found or not in Requested status.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetMovement_Complete
    @AssetMovementId INT,
    @PerformedBy     NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @AssetId INT, @Status NVARCHAR(30), @ToLoc INT, @ToBld INT, @ToFlr INT, @ToRoom INT;
        SELECT @AssetId = AssetId, @Status = MovementStatus,
               @ToLoc = ToLocationId, @ToBld = ToBuildingId, @ToFlr = ToFloorId, @ToRoom = ToRoomId
        FROM dbo.tblAssetMovement WITH (UPDLOCK, HOLDLOCK)
        WHERE AssetMovementId = @AssetMovementId AND IsDeleted = 0;

        IF @AssetId IS NULL THROW 50001, N'Movement request not found.', 1;
        IF @Status <> N'Approved' THROW 50002, N'Only approved movements can be completed.', 1;

        DECLARE @FromText NVARCHAR(400) =
            (SELECT CONCAT(l.LocationName, N' / ', b.BuildingName, N' / ', f.FloorName, N' / ', r.RoomName)
             FROM dbo.tblAsset a
             LEFT JOIN dbo.tblLocation l ON l.LocationId = a.LocationId
             LEFT JOIN dbo.tblBuilding b ON b.BuildingId = a.BuildingId
             LEFT JOIN dbo.tblFloor f ON f.FloorId = a.FloorId
             LEFT JOIN dbo.tblRoom r ON r.RoomId = a.RoomId
             WHERE a.AssetId = @AssetId);

        UPDATE dbo.tblAsset
           SET LocationId = @ToLoc, BuildingId = @ToBld, FloorId = @ToFlr, RoomId = @ToRoom,
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        UPDATE dbo.tblAssetMovement
           SET MovementStatus = N'Completed', CompletedDate = SYSUTCDATETIME(),
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetMovementId = @AssetMovementId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, FieldName, OldValue, NewValue, Remarks, PerformedBy)
        SELECT @AssetId, N'Moved', N'Location', @FromText,
               CONCAT(l.LocationName, N' / ', b.BuildingName, N' / ', f.FloorName, N' / ', r.RoomName),
               CONCAT(N'Movement #', @AssetMovementId), @PerformedBy
        FROM (SELECT 1 AS x) d
        LEFT JOIN dbo.tblLocation l ON l.LocationId = @ToLoc
        LEFT JOIN dbo.tblBuilding b ON b.BuildingId = @ToBld
        LEFT JOIN dbo.tblFloor f ON f.FloorId = @ToFlr
        LEFT JOIN dbo.tblRoom r ON r.RoomId = @ToRoom;

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

/* ------------------------------------------------------------------
   Physical audit / stock-take
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_AssetAudit_Record
    @AssetId         INT,
    @AuditCycle      NVARCHAR(20),
    @AuditResult     NVARCHAR(30),
    @ScanMethod      NVARCHAR(30) = N'Manual',
    @FoundLocationId INT = NULL,
    @FoundRoomId     INT = NULL,
    @FoundEmployeeId INT = NULL,
    @Remarks         NVARCHAR(1000) = NULL,
    @AuditedBy       NVARCHAR(100),
    @NextAuditMonths INT = 12,
    @AssetAuditId    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.tblAsset WHERE AssetId = @AssetId AND IsDeleted = 0)
            THROW 50001, N'Asset not found.', 1;

        -- One result per asset per cycle (UX_tblAssetAudit_AssetId_AuditCycle): scanning again in the same cycle
        -- replaces that result (and re-opens any finding). Every scan is still kept in tblAssetHistory below.
        -- UPDLOCK + HOLDLOCK: two simultaneous scans of the same asset cannot both insert.
        SELECT @AssetAuditId = AssetAuditId
          FROM dbo.tblAssetAudit WITH (UPDLOCK, HOLDLOCK)
         WHERE AssetId = @AssetId AND AuditCycle = @AuditCycle AND IsDeleted = 0;

        IF @AssetAuditId IS NULL
        BEGIN
            INSERT dbo.tblAssetAudit
                (AssetId, AuditCycle, AuditDate, AuditedBy, AuditResult, ScanMethod,
                 FoundLocationId, FoundRoomId, FoundEmployeeId, Remarks, CreatedBy)
            VALUES
                (@AssetId, @AuditCycle, SYSUTCDATETIME(), @AuditedBy, @AuditResult, @ScanMethod,
                 @FoundLocationId, @FoundRoomId, @FoundEmployeeId, @Remarks, @AuditedBy);
            SET @AssetAuditId = SCOPE_IDENTITY();
        END
        ELSE
            UPDATE dbo.tblAssetAudit
               SET AuditDate = SYSUTCDATETIME(), AuditedBy = @AuditedBy, AuditResult = @AuditResult, ScanMethod = @ScanMethod,
                   FoundLocationId = @FoundLocationId, FoundRoomId = @FoundRoomId, FoundEmployeeId = @FoundEmployeeId,
                   Remarks = @Remarks, IsResolved = 0, CorrectiveAction = NULL, ResolvedDate = NULL,
                   ModifiedBy = @AuditedBy, ModifiedDate = SYSUTCDATETIME()
             WHERE AssetAuditId = @AssetAuditId;

        UPDATE dbo.tblAsset
           SET NextAuditDueDate = DATEADD(MONTH, @NextAuditMonths, CAST(SYSUTCDATETIME() AS DATE)),
               AssetStatusId = CASE WHEN @AuditResult = N'Missing'
                                    THEN (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'MISSING')
                                    ELSE AssetStatusId END,
               ModifiedBy = @AuditedBy,
               ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, NewValue, Remarks, PerformedBy)
        VALUES (@AssetId, N'Audited', @AuditResult, CONCAT(N'Cycle ', @AuditCycle, N' - ', @Remarks), @AuditedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

/* ------------------------------------------------------------------
   Disposal (request -> approve -> complete with sanitisation evidence)
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_AssetDisposal_Request
    @AssetId          INT,
    @DisposalMethodId INT,
    @Reason           NVARCHAR(1000),
    @RequestedBy      NVARCHAR(100),
    @AssetDisposalId  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @Terminal BIT;
        SELECT @Terminal = s.IsTerminal
        FROM dbo.tblAsset a WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId
        WHERE a.AssetId = @AssetId AND a.IsDeleted = 0;

        IF @Terminal IS NULL THROW 50001, N'Asset not found.', 1;
        IF @Terminal = 1 THROW 50002, N'Asset is already disposed.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tblAssetAssignment WHERE AssetId = @AssetId AND IsCurrent = 1 AND IsDeleted = 0)
            THROW 50002, N'Return the asset from its current custodian before requesting disposal.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tblDisposalMethod WHERE DisposalMethodId = @DisposalMethodId AND IsActive = 1 AND IsDeleted = 0)
            THROW 50003, N'Invalid disposal method.', 1;

        INSERT dbo.tblAssetDisposal (AssetId, DisposalMethodId, DisposalStatus, Reason, RequestedBy, CreatedBy)
        VALUES (@AssetId, @DisposalMethodId, N'Requested', @Reason, @RequestedBy, @RequestedBy);
        SET @AssetDisposalId = SCOPE_IDENTITY();

        UPDATE dbo.tblAsset
           SET AssetStatusId = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'PEND_DISP'),
               ModifiedBy = @RequestedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, Remarks, PerformedBy)
        VALUES (@AssetId, N'DisposalRequested', CONCAT(N'Disposal #', @AssetDisposalId, N': ', @Reason), @RequestedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetDisposal_Approve
    @AssetDisposalId INT,
    @ApprovedBy      NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Status NVARCHAR(30), @RequestedBy NVARCHAR(100);
    SELECT @Status = DisposalStatus, @RequestedBy = RequestedBy
    FROM dbo.tblAssetDisposal WHERE AssetDisposalId = @AssetDisposalId AND IsDeleted = 0;
    IF @Status IS NULL THROW 50001, N'Disposal request not found.', 1;
    IF @Status <> N'Requested' THROW 50002, N'Only requested disposals can be approved.', 1;
    IF @RequestedBy = @ApprovedBy THROW 50004, N'A disposal cannot be approved by the person who requested it.', 1;

    UPDATE dbo.tblAssetDisposal
       SET DisposalStatus = N'Approved', ApprovedBy = @ApprovedBy, ApprovedDate = SYSUTCDATETIME(),
           ModifiedBy = @ApprovedBy, ModifiedDate = SYSUTCDATETIME()
     WHERE AssetDisposalId = @AssetDisposalId AND DisposalStatus = N'Requested';
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetDisposal_Reject
    @AssetDisposalId INT,
    @RejectedBy      NVARCHAR(100),
    @RejectionReason NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@RejectionReason)), N'') IS NULL
            THROW 50003, N'A rejection reason is required.', 1;
        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @AssetId INT;
        SELECT @AssetId = AssetId FROM dbo.tblAssetDisposal WITH (UPDLOCK, HOLDLOCK)
        WHERE AssetDisposalId = @AssetDisposalId AND DisposalStatus = N'Requested' AND IsDeleted = 0;
        IF @AssetId IS NULL THROW 50002, N'Disposal not found or not in Requested status.', 1;

        UPDATE dbo.tblAssetDisposal
           SET DisposalStatus = N'Rejected', RejectionReason = @RejectionReason,
               ModifiedBy = @RejectedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetDisposalId = @AssetDisposalId;

        UPDATE dbo.tblAsset
           SET AssetStatusId = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'IN_STOCK'),
               ModifiedBy = @RejectedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, FieldName, NewValue, Remarks, PerformedBy)
        VALUES (@AssetId, N'StatusChanged', N'AssetStatusId', N'IN_STOCK',
                CONCAT(N'Disposal #', @AssetDisposalId, N' rejected: ', @RejectionReason), @RejectedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssetDisposal_Complete
    @AssetDisposalId            INT,
    @DisposalDate               DATE,
    @DataSanitizationMethod     NVARCHAR(150) = NULL,
    @DataSanitizationDate       DATETIME2(3) = NULL,
    @DataSanitizationVerifiedBy NVARCHAR(100) = NULL,
    @CertificateNumber          NVARCHAR(100) = NULL,
    @RecipientName              NVARCHAR(200) = NULL,
    @SaleValue                  DECIMAL(18,2) = NULL,
    @CurrencyId                 INT = NULL,
    @Remarks                    NVARCHAR(1000) = NULL,
    @PerformedBy                NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 1;

        DECLARE @AssetId INT, @Status NVARCHAR(30), @NeedsSanitization BIT, @NeedsCert BIT;
        SELECT @AssetId = d.AssetId, @Status = d.DisposalStatus,
               @NeedsSanitization = m.RequiresDataSanitization, @NeedsCert = m.RequiresCertificate
        FROM dbo.tblAssetDisposal d WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.tblDisposalMethod m ON m.DisposalMethodId = d.DisposalMethodId
        WHERE d.AssetDisposalId = @AssetDisposalId AND d.IsDeleted = 0;

        IF @AssetId IS NULL THROW 50001, N'Disposal request not found.', 1;
        IF @Status <> N'Approved' THROW 50002, N'Only approved disposals can be completed.', 1;
        IF @NeedsSanitization = 1 AND (@DataSanitizationMethod IS NULL OR @DataSanitizationDate IS NULL OR @DataSanitizationVerifiedBy IS NULL)
            THROW 50003, N'This disposal method requires data sanitisation method, date and verifier.', 1;
        IF @NeedsCert = 1 AND @CertificateNumber IS NULL
            THROW 50003, N'This disposal method requires a certificate number.', 1;

        UPDATE dbo.tblAssetDisposal
           SET DisposalStatus = N'Completed', DisposalDate = @DisposalDate,
               DataSanitizationMethod = @DataSanitizationMethod, DataSanitizationDate = @DataSanitizationDate,
               DataSanitizationVerifiedBy = @DataSanitizationVerifiedBy, CertificateNumber = @CertificateNumber,
               RecipientName = @RecipientName, SaleValue = @SaleValue, CurrencyId = @CurrencyId,
               Remarks = COALESCE(@Remarks, Remarks),
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetDisposalId = @AssetDisposalId;

        -- Release software seats held by the asset
        UPDATE dbo.tblSoftwareLicenseAllocation
           SET IsCurrent = 0, RevokedDate = SYSUTCDATETIME(),
               Notes = CONCAT(ISNULL(Notes + N' | ', N''), N'Revoked on disposal #', @AssetDisposalId),
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId AND IsCurrent = 1 AND IsDeleted = 0;

        UPDATE dbo.tblAssetQRCode
           SET IsCurrent = 0, ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId AND IsCurrent = 1;

        UPDATE dbo.tblAsset
           SET AssetStatusId = (SELECT AssetStatusId FROM dbo.tblAssetStatus WHERE StatusCode = N'DISPOSED'),
               DecommissionDate = @DisposalDate,
               AssignedEmployeeId = NULL,
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId;

        INSERT dbo.tblAssetHistory (AssetId, EventType, Remarks, PerformedBy)
        VALUES (@AssetId, N'Disposed',
                CONCAT(N'Disposal #', @AssetDisposalId, N' completed. Certificate: ', ISNULL(@CertificateNumber, N'n/a')),
                @PerformedBy);

        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        EXEC sp_set_session_context N'AMGH.SuppressAssetHistory', 0;
        THROW;
    END CATCH
END
GO

/* ------------------------------------------------------------------
   QR codes and labels
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_AssetQrCode_Save
    @AssetId       INT,
    @Payload       NVARCHAR(2000),
    @PayloadHash   CHAR(64),
    @QrImagePng    VARBINARY(MAX),
    @GeneratedBy   NVARCHAR(100),
    @AssetQRCodeId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRAN;
        IF NOT EXISTS (SELECT 1 FROM dbo.tblAsset WITH (UPDLOCK, HOLDLOCK) WHERE AssetId = @AssetId AND IsDeleted = 0)
            THROW 50001, N'Asset not found.', 1;

        DECLARE @NextVersion INT = ISNULL((SELECT MAX(QrVersion) FROM dbo.tblAssetQRCode WHERE AssetId = @AssetId), 0) + 1;

        UPDATE dbo.tblAssetQRCode
           SET IsCurrent = 0, ModifiedBy = @GeneratedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE AssetId = @AssetId AND IsCurrent = 1;

        INSERT dbo.tblAssetQRCode (AssetId, QrVersion, Payload, PayloadHash, QrImagePng, IsCurrent, CreatedBy)
        VALUES (@AssetId, @NextVersion, @Payload, @PayloadHash, @QrImagePng, 1, @GeneratedBy);
        SET @AssetQRCodeId = SCOPE_IDENTITY();

        INSERT dbo.tblAssetHistory (AssetId, EventType, NewValue, PerformedBy)
        VALUES (@AssetId, N'QrGenerated', CONCAT(N'v', @NextVersion), @GeneratedBy);
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
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
        VALUES (@AssetId, N'LabelPrinted', CONCAT(CASE WHEN @LabelDesign = N'AssetTag' THEN N'Asset tag ' ELSE N'QR label ' END, @LabelSize, N' x', @Copies),
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

/* ------------------------------------------------------------------
   Software license key encryption (AES-256, key protected by cert)
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_SoftwareLicense_SetKey
    @SoftwareLicenseId INT,
    @LicenseKey        NVARCHAR(1000),
    @PerformedBy       NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    OPEN SYMMETRIC KEY AMGH_DataProtection_Key DECRYPTION BY CERTIFICATE AMGH_DataProtection_Cert;
    BEGIN TRY
        UPDATE dbo.tblSoftwareLicense
           SET LicenseKeyEncrypted = CASE WHEN @LicenseKey IS NULL THEN NULL
                                          ELSE ENCRYPTBYKEY(KEY_GUID(N'AMGH_DataProtection_Key'), @LicenseKey, 1,
                                                            CAST(@SoftwareLicenseId AS NVARCHAR(20))) END,
               ModifiedBy = @PerformedBy, ModifiedDate = SYSUTCDATETIME()
         WHERE SoftwareLicenseId = @SoftwareLicenseId AND IsDeleted = 0;
        IF @@ROWCOUNT = 0 THROW 50001, N'License not found.', 1;

        INSERT dbo.tblAuditLog (EntityName, EntityKey, Action, ChangedColumns, PerformedBy)
        VALUES (N'SoftwareLicense', CAST(@SoftwareLicenseId AS NVARCHAR(20)), N'Update', N'["LicenseKey"]', @PerformedBy);
        CLOSE SYMMETRIC KEY AMGH_DataProtection_Key;
    END TRY
    BEGIN CATCH
        IF EXISTS (SELECT 1 FROM sys.openkeys WHERE key_name = N'AMGH_DataProtection_Key')
            CLOSE SYMMETRIC KEY AMGH_DataProtection_Key;
        THROW;
    END CATCH
END
GO

/* Every key reveal is written to the audit log. */
CREATE OR ALTER PROCEDURE dbo.usp_SoftwareLicense_GetKey
    @SoftwareLicenseId INT,
    @RequestedBy       NVARCHAR(100),
    @IpAddress         NVARCHAR(45) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    OPEN SYMMETRIC KEY AMGH_DataProtection_Key DECRYPTION BY CERTIFICATE AMGH_DataProtection_Cert;
    BEGIN TRY
        -- Audit first, so the reveal is logged even if the caller stops reading after the value.
        INSERT dbo.tblAuditLog (EntityName, EntityKey, Action, ChangedColumns, PerformedBy, IpAddress)
        VALUES (N'SoftwareLicense', CAST(@SoftwareLicenseId AS NVARCHAR(20)), N'View', N'["LicenseKey"]', @RequestedBy, @IpAddress);

        SELECT CAST(DECRYPTBYKEY(LicenseKeyEncrypted, 1, CAST(@SoftwareLicenseId AS NVARCHAR(20))) AS NVARCHAR(1000)) AS LicenseKey
        FROM dbo.tblSoftwareLicense
        WHERE SoftwareLicenseId = @SoftwareLicenseId AND IsDeleted = 0;
        CLOSE SYMMETRIC KEY AMGH_DataProtection_Key;
    END TRY
    BEGIN CATCH
        IF EXISTS (SELECT 1 FROM sys.openkeys WHERE key_name = N'AMGH_DataProtection_Key')
            CLOSE SYMMETRIC KEY AMGH_DataProtection_Key;
        THROW;
    END CATCH
END
GO

/* ------------------------------------------------------------------
   Search (paged, filtered, whitelisted sorting)
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_Asset_Search
    @SearchText            NVARCHAR(100) = NULL,
    @AssetCategoryId       INT = NULL,
    @AssetStatusId         INT = NULL,
    @DepartmentId          INT = NULL,
    @LocationId            INT = NULL,
    @AssignedEmployeeId    INT = NULL,
    @ClassificationId      INT = NULL,
    @CriticalityId         INT = NULL,
    @ManufacturerId        INT = NULL,
    @WarrantyExpiringDays  INT = NULL,
    @PurchaseDateFrom      DATE = NULL,
    @PurchaseDateTo        DATE = NULL,
    @OnlyUnassigned        BIT = 0,
    @IncludeDisposed       BIT = 0,
    @SortColumn            NVARCHAR(50) = N'AssetNumber',
    @SortDescending        BIT = 0,
    @PageNumber            INT = 1,
    @PageSize              INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize NOT BETWEEN 1 AND 500 SET @PageSize = 25;
    DECLARE @Like NVARCHAR(102) = CASE WHEN NULLIF(LTRIM(RTRIM(@SearchText)), N'') IS NULL THEN NULL
                                       ELSE N'%' + REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@SearchText)),
                                            N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%' END;

    SELECT v.*, COUNT(*) OVER () AS TotalCount
    FROM dbo.vw_AssetDetails v
    WHERE (@Like IS NULL OR v.AssetNumber LIKE @Like OR v.AssetName LIKE @Like OR v.SerialNumber LIKE @Like
           OR v.Hostname LIKE @Like OR v.AssignedEmployeeName LIKE @Like OR v.ModelName LIKE @Like
           OR v.LegacyAssetTag LIKE @Like OR v.IpAddress LIKE @Like)
      AND (@AssetCategoryId IS NULL OR v.AssetCategoryId = @AssetCategoryId)
      AND (@AssetStatusId IS NULL OR v.AssetStatusId = @AssetStatusId)
      AND (@DepartmentId IS NULL OR v.DepartmentId = @DepartmentId)
      AND (@LocationId IS NULL OR v.LocationId = @LocationId)
      AND (@AssignedEmployeeId IS NULL OR v.AssignedEmployeeId = @AssignedEmployeeId)
      AND (@ClassificationId IS NULL OR v.ClassificationId = @ClassificationId)
      AND (@CriticalityId IS NULL OR v.CriticalityId = @CriticalityId)
      AND (@ManufacturerId IS NULL OR v.ManufacturerId = @ManufacturerId)
      AND (@WarrantyExpiringDays IS NULL OR v.WarrantyDaysRemaining BETWEEN 0 AND @WarrantyExpiringDays)
      AND (@PurchaseDateFrom IS NULL OR v.PurchaseDate >= @PurchaseDateFrom)
      AND (@PurchaseDateTo IS NULL OR v.PurchaseDate <= @PurchaseDateTo)
      AND (@OnlyUnassigned = 0 OR (v.AssignedEmployeeId IS NULL AND v.IsAssignable = 1))
      AND (@IncludeDisposed = 1 OR v.StatusCode <> N'DISPOSED')
    ORDER BY
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'AssetNumber'  THEN v.AssetNumber END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'AssetNumber'  THEN v.AssetNumber END DESC,
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'AssetName'    THEN v.AssetName END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'AssetName'    THEN v.AssetName END DESC,
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'Category'     THEN v.CategoryName END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'Category'     THEN v.CategoryName END DESC,
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'Status'       THEN v.StatusName END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'Status'       THEN v.StatusName END DESC,
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'AssignedTo'   THEN v.AssignedEmployeeName END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'AssignedTo'   THEN v.AssignedEmployeeName END DESC,
        CASE WHEN @SortDescending = 0 AND @SortColumn = N'PurchaseDate' THEN v.PurchaseDate END ASC,
        CASE WHEN @SortDescending = 1 AND @SortColumn = N'PurchaseDate' THEN v.PurchaseDate END DESC,
        v.AssetId
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);
END
GO

/* ------------------------------------------------------------------
   Dashboard - multiple result sets in one round trip
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_Dashboard_GetData
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.vw_DashboardKpi;                                                    -- 1 KPIs
    SELECT * FROM dbo.vw_AssetCountByCategory ORDER BY TotalAssets DESC;                  -- 2 by category
    SELECT * FROM dbo.vw_AssetCountByStatus ORDER BY SortOrder;                           -- 3 by status
    SELECT TOP (10) * FROM dbo.vw_AssetCountByDepartment ORDER BY TotalAssets DESC;       -- 4 top departments
    SELECT TOP (10) * FROM dbo.vw_WarrantyExpiring ORDER BY DaysRemaining;                -- 5 warranties
    SELECT TOP (20) h.AssetHistoryId, h.AssetId, a.AssetNumber, a.AssetName, h.EventType,
                    h.NewValue, h.Remarks, h.PerformedBy, h.EventDate                    -- 6 recent activity
    FROM dbo.tblAssetHistory h
    JOIN dbo.tblAsset a ON a.AssetId = h.AssetId
    ORDER BY h.AssetHistoryId DESC;
END
GO

/* ------------------------------------------------------------------
   Scheduled jobs (called by Hangfire)
   ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.usp_Warranty_RefreshStatuses
    @ExpiringDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
    UPDATE w
       SET WarrantyStatusId = ws.WarrantyStatusId,
           ModifiedBy = N'SYSTEM', ModifiedDate = SYSUTCDATETIME()
    FROM dbo.tblWarranty w
    JOIN dbo.tblWarrantyStatus cur ON cur.WarrantyStatusId = w.WarrantyStatusId
    JOIN dbo.tblWarrantyStatus ws  ON ws.StatusCode = dbo.fn_GetWarrantyStateCode(w.EndDate, @Today, @ExpiringDays)
    WHERE w.IsDeleted = 0
      AND cur.StatusCode <> N'VOID'
      AND w.WarrantyStatusId <> ws.WarrantyStatusId;
    SELECT @@ROWCOUNT AS UpdatedRows;
END
GO

/* In-app notifications per item + one e-mail digest per user per day. */
CREATE OR ALTER PROCEDURE dbo.usp_Notification_QueueExpiryAlerts
    @WarrantyDays INT = 30,
    @BaseUrl      NVARCHAR(200) = N''
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);

    CREATE TABLE #Items
    (
        PermissionCode NVARCHAR(100), NotificationType NVARCHAR(30), Severity NVARCHAR(30),
        EntityName NVARCHAR(100), EntityId NVARCHAR(50), Title NVARCHAR(200), Message NVARCHAR(2000),
        ActionUrl NVARCHAR(500)
    );

    INSERT #Items
    SELECT N'Warranties.Manage', N'WarrantyExpiry',
           CASE WHEN w.DaysRemaining < 0 THEN N'Critical' WHEN w.DaysRemaining <= 7 THEN N'Warning' ELSE N'Info' END,
           N'Warranty', CAST(w.WarrantyId AS NVARCHAR(50)),
           CONCAT(N'Warranty ', CASE WHEN w.DaysRemaining < 0 THEN N'expired' ELSE N'expiring' END, N': ', w.AssetNumber),
           CONCAT(w.AssetName, N' (', w.AssetNumber, N') warranty ends ', CONVERT(NVARCHAR(10), w.EndDate, 23),
                  N' - ', w.DaysRemaining, N' day(s).'),
           CONCAT(@BaseUrl, N'/Assets/Details/', w.AssetId)
    FROM dbo.vw_WarrantyExpiring w
    WHERE w.DaysRemaining <= @WarrantyDays;

    INSERT #Items
    SELECT N'Licenses.Manage', N'LicenseExpiry',
           CASE WHEN l.ComplianceState = N'Expired' THEN N'Critical' ELSE N'Warning' END,
           N'SoftwareLicense', CAST(l.SoftwareLicenseId AS NVARCHAR(50)),
           CONCAT(N'License ', CASE WHEN l.ComplianceState = N'Expired' THEN N'expired' ELSE N'renewal due' END, N': ', l.SoftwareName),
           CONCAT(l.SoftwareName, N' (', l.LicenseNumber, N') expires ', CONVERT(NVARCHAR(10), l.ExpiryDate, 23),
                  N' - ', l.DaysToExpiry, N' day(s). Seats used ', l.UsedSeats, N'/', ISNULL(CAST(l.TotalSeats AS NVARCHAR(10)), N'unlimited'), N'.'),
           CONCAT(@BaseUrl, N'/Licenses/Details/', l.SoftwareLicenseId)
    FROM dbo.vw_LicenseCompliance l
    WHERE l.IsActive = 1 AND l.ComplianceState IN (N'RenewalDue', N'Expired');

    BEGIN TRAN;
    -- In-app: once per user/item every 7 days
    INSERT dbo.tblNotification (UserId, Title, Message, NotificationType, Severity, RelatedEntityName, RelatedEntityId, ActionUrl)
    SELECT DISTINCT p.UserId, i.Title, i.Message, i.NotificationType, i.Severity, i.EntityName, i.EntityId, i.ActionUrl
    FROM #Items i
    JOIN dbo.vw_UserEffectivePermissions p ON p.PermissionCode = i.PermissionCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblNotification n
                      WHERE n.UserId = p.UserId AND n.RelatedEntityName = i.EntityName AND n.RelatedEntityId = i.EntityId
                        AND n.NotificationType = i.NotificationType AND n.IsDeleted = 0
                        AND n.CreatedDate >= DATEADD(DAY, -7, SYSUTCDATETIME()));

    -- E-mail: one digest per user per day (DeduplicationKey is unique)
    INSERT dbo.tblNotificationQueue (Channel, RecipientAddress, RecipientUserId, Subject, Body, IsHtml, DeduplicationKey)
    SELECT N'Email', u.Email, u.UserId,
           CONCAT(N'AMGH IT Inventory - ', COUNT(*), N' expiry alert(s)'),
           CONCAT(N'<p>The following items need attention:</p><ul>',
                  STRING_AGG(CAST(CONCAT(N'<li>', i.Message, N'</li>') AS NVARCHAR(MAX)), N'') WITHIN GROUP (ORDER BY i.Severity),
                  N'</ul>'),
           1,
           CONCAT(N'EXPIRY-DIGEST:', u.UserId, N':', CONVERT(NCHAR(8), @Today, 112))
    FROM #Items i
    JOIN (SELECT DISTINCT UserId, PermissionCode FROM dbo.vw_UserEffectivePermissions) p ON p.PermissionCode = i.PermissionCode
    JOIN dbo.tblUser u ON u.UserId = p.UserId
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblNotificationQueue q
                      WHERE q.DeduplicationKey = CONCAT(N'EXPIRY-DIGEST:', u.UserId, N':', CONVERT(NCHAR(8), @Today, 112)))
    GROUP BY u.UserId, u.Email;
    COMMIT;

    SELECT (SELECT COUNT(*) FROM #Items) AS ItemsFound;
END
GO
PRINT 'Script 05 completed.';
GO
