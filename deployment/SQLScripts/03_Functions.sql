/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 03 - Functions
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Re-deployment: a schema-bound function cannot be altered while another schema-bound
   function references it, so drop the dependant first; it is recreated below.
   (Views that use it are not schema-bound and are re-created by script 04.) */
DROP FUNCTION IF EXISTS dbo.fn_GetAssetBookValue;
GO

/* Formats an asset number, e.g. ('AMGH-IT','LAP',1,5) -> AMGH-IT-LAP-00001.
   Numbers wider than the padding are never truncated (AMGH-IT-PC-100000). */
CREATE OR ALTER FUNCTION dbo.fn_FormatAssetNumber
(
    @Prefix        NVARCHAR(20),
    @SequenceCode  NVARCHAR(10),
    @Number        BIGINT,
    @PaddingLength TINYINT,
    @Separator     NVARCHAR(1) = N'-'          -- Release 2.9: configurable per number series
)
RETURNS NVARCHAR(30)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @digits NVARCHAR(20) = CAST(@Number AS NVARCHAR(20));
    IF LEN(@digits) < @PaddingLength
        SET @digits = RIGHT(REPLICATE(N'0', @PaddingLength) + @digits, @PaddingLength);
    RETURN CONCAT(@Prefix, @Separator, @SequenceCode, @Separator, @digits);
END
GO

/* Warranty state code used to keep tblWarranty.WarrantyStatusId current. */
CREATE OR ALTER FUNCTION dbo.fn_GetWarrantyStateCode
(
    @EndDate      DATE,
    @AsOfDate     DATE,
    @ExpiringDays INT
)
RETURNS NVARCHAR(20)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CASE
        WHEN @EndDate < @AsOfDate THEN N'EXPIRED'
        WHEN @EndDate <= DATEADD(DAY, @ExpiringDays, @AsOfDate) THEN N'EXPIRING'
        ELSE N'ACTIVE'
    END;
END
GO

/* Whole months elapsed since purchase (never negative). */
CREATE OR ALTER FUNCTION dbo.fn_GetAssetAgeMonths
(
    @PurchaseDate DATE,
    @AsOfDate     DATE
)
RETURNS INT
WITH SCHEMABINDING
AS
BEGIN
    IF @PurchaseDate IS NULL OR @AsOfDate IS NULL OR @AsOfDate < @PurchaseDate RETURN NULL;
    DECLARE @m INT = DATEDIFF(MONTH, @PurchaseDate, @AsOfDate);
    IF DAY(@AsOfDate) < DAY(@PurchaseDate) SET @m = @m - 1;
    RETURN CASE WHEN @m < 0 THEN 0 ELSE @m END;
END
GO

/* Straight-line net book value; floors at salvage value. */
CREATE OR ALTER FUNCTION dbo.fn_GetAssetBookValue
(
    @PurchaseCost     DECIMAL(18,2),
    @SalvageValue     DECIMAL(18,2),
    @UsefulLifeMonths INT,
    @PurchaseDate     DATE,
    @AsOfDate         DATE
)
RETURNS DECIMAL(18,2)
WITH SCHEMABINDING
AS
BEGIN
    IF @PurchaseCost IS NULL OR @UsefulLifeMonths IS NULL OR @UsefulLifeMonths <= 0 OR @PurchaseDate IS NULL
        RETURN @PurchaseCost;
    DECLARE @salvage DECIMAL(18,2) = ISNULL(@SalvageValue, 0);
    DECLARE @age INT = dbo.fn_GetAssetAgeMonths(@PurchaseDate, @AsOfDate);
    IF @age IS NULL RETURN @PurchaseCost;
    IF @age >= @UsefulLifeMonths RETURN @salvage;
    RETURN ROUND(@PurchaseCost - ((@PurchaseCost - @salvage) * @age / CAST(@UsefulLifeMonths AS DECIMAL(18,4))), 2);
END
GO

/* Effective permissions for a user (active user, active roles, non-deleted links). */
CREATE OR ALTER FUNCTION dbo.fn_GetUserPermissions (@UserId INT)
RETURNS TABLE
AS
RETURN
    SELECT DISTINCT p.PermissionId, p.PermissionCode, p.ModuleName
    FROM dbo.tblUser u
    JOIN dbo.tblUserRole ur       ON ur.UserId = u.UserId AND ur.IsDeleted = 0
    JOIN dbo.tblRole r            ON r.RoleId = ur.RoleId AND r.IsDeleted = 0 AND r.IsActive = 1
    JOIN dbo.tblRolePermission rp ON rp.RoleId = r.RoleId AND rp.IsDeleted = 0
    JOIN dbo.tblPermission p      ON p.PermissionId = rp.PermissionId AND p.IsDeleted = 0
    WHERE u.UserId = @UserId AND u.IsDeleted = 0 AND u.IsActive = 1;
GO

CREATE OR ALTER FUNCTION dbo.fn_UserHasPermission
(
    @UserId         INT,
    @PermissionCode NVARCHAR(100)
)
RETURNS BIT
AS
BEGIN
    RETURN CASE WHEN EXISTS (SELECT 1 FROM dbo.fn_GetUserPermissions(@UserId) WHERE PermissionCode = @PermissionCode)
                THEN 1 ELSE 0 END;
END
GO

/* Current status code of an asset (used by procedures for state checks). */
CREATE OR ALTER FUNCTION dbo.fn_GetAssetStatusCode (@AssetId INT)
RETURNS NVARCHAR(20)
AS
BEGIN
    RETURN (SELECT s.StatusCode FROM dbo.tblAsset a
            JOIN dbo.tblAssetStatus s ON s.AssetStatusId = a.AssetStatusId
            WHERE a.AssetId = @AssetId);
END
GO
PRINT 'Script 03 completed.';
GO
