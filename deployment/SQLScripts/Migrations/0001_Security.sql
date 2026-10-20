/* =====================================================================
   0001 - Sign-in security: two-step verification (authenticator app), recovery codes, per-company
   policy, IP allow-list and single sign-on (OpenID Connect). Safe to run twice.
   ===================================================================== */
SET NOCOUNT ON;
GO
USE [$(DatabaseName)];
GO
IF COL_LENGTH('dbo.tblUser', 'TotpSecret') IS NULL
    ALTER TABLE dbo.tblUser ADD
        TotpSecret      NVARCHAR(400) NULL,       -- protected by the application (Data Protection), never plain text
        TotpEnabledDate DATETIME2(3)  NULL,
        TotpLastStep    BIGINT        NULL;       -- last accepted 30-second step: a code works once
GO
IF OBJECT_ID(N'dbo.tblUserRecoveryCode', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblUserRecoveryCode
    (
        UserRecoveryCodeId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblUserRecoveryCode PRIMARY KEY CLUSTERED,
        UserId      INT          NOT NULL CONSTRAINT FK_tblUserRecoveryCode_UserId REFERENCES dbo.tblUser (UserId),
        CodeHash    CHAR(64)     NOT NULL,        -- SHA-256 of the code; the code itself is shown once
        CreatedDate DATETIME2(3) NOT NULL CONSTRAINT DF_tblUserRecoveryCode_Created DEFAULT (SYSUTCDATETIME()),
        UsedDate    DATETIME2(3) NULL
    );
    CREATE INDEX IX_tblUserRecoveryCode_UserId ON dbo.tblUserRecoveryCode (UserId);
END
GO
IF COL_LENGTH('dbo.tblCompany', 'MfaPolicy') IS NULL
    ALTER TABLE dbo.tblCompany ADD
        MfaPolicy        TINYINT       NOT NULL CONSTRAINT DF_tblCompany_MfaPolicy DEFAULT (0),   -- 0 optional, 1 required for administrators, 2 required for everyone
        AllowedIpRanges  NVARCHAR(1000) NULL,                                                      -- comma/space separated addresses or CIDR ranges; empty = anywhere
        SsoEnabled       BIT           NOT NULL CONSTRAINT DF_tblCompany_SsoEnabled DEFAULT (0),
        SsoAuthority     NVARCHAR(300) NULL,                                                       -- https://login.microsoftonline.com/<tenant>/v2.0 or https://accounts.google.com
        SsoClientId      NVARCHAR(200) NULL,
        SsoClientSecret  NVARCHAR(1000) NULL,                                                      -- protected by the application
        SsoAllowedDomain NVARCHAR(200) NULL;                                                       -- only e-mail addresses of this domain may sign in this way
GO
-- the sign-in history also records two-step verification and single sign-on events
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblLoginAudit_EventType_Domain' AND definition NOT LIKE N'%SsoSignIn%')
BEGIN
    ALTER TABLE dbo.tblLoginAudit DROP CONSTRAINT CK_tblLoginAudit_EventType_Domain;
    ALTER TABLE dbo.tblLoginAudit ADD CONSTRAINT CK_tblLoginAudit_EventType_Domain CHECK ([EventType] IN
        (N'LoginSuccess', N'LoginFailed', N'Logout', N'Lockout', N'PasswordChanged', N'PasswordReset', N'SessionExpired', N'AccountDisabled', N'MfaEnabled', N'MfaReset', N'RecoveryCodeUsed', N'SsoSignIn'));
END
GO
