/* =====================================================================
   AMGH Inventory Management System - Release 2.5 (notifications)
   Script 14 - Company e-mail settings, templates, reminder rules, user preferences. Idempotent.
   The in-app notification table and the e-mail queue already exist (script 02).
   ===================================================================== */
USE [$(DatabaseName)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'CompanyIsolation')
    THROW 52400, N'Script 14 needs the multi-company foundation (script 10).', 1;
GO
IF OBJECT_ID(N'dbo.tblCompanyMailSettings', N'U') IS NULL
BEGIN
    -- One row per company. The secret (SMTP password or OAuth client secret) is encrypted by the application with
    -- ASP.NET Data Protection (keys protected by Windows DPAPI on the server) and is never shown again.
    CREATE TABLE dbo.tblCompanyMailSettings
    (
        CompanyId        INT NOT NULL CONSTRAINT PK_tblCompanyMailSettings PRIMARY KEY
                         CONSTRAINT FK_tblCompanyMailSettings_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        IsEnabled        BIT NOT NULL CONSTRAINT DF_tblCompanyMailSettings_IsEnabled DEFAULT (0),
        Mode             NVARCHAR(30) NOT NULL CONSTRAINT CK_tblCompanyMailSettings_Mode
                         CHECK (Mode IN (N'ConnectorRelay', N'SmtpAuth', N'SmtpOAuth', N'HighVolumeEmail', N'CustomSmtp')),
        Host             NVARCHAR(255) NOT NULL,
        Port             INT NOT NULL CONSTRAINT CK_tblCompanyMailSettings_Port CHECK (Port BETWEEN 1 AND 65535),
        Security         NVARCHAR(20) NOT NULL CONSTRAINT CK_tblCompanyMailSettings_Security CHECK (Security IN (N'StartTls', N'SslOnConnect', N'None')),
        -- Internal relays with a private certificate: the certificate's SHA-256 thumbprint is pinned (only that certificate is accepted).
        CertificateThumbprint NVARCHAR(128) NULL,
        Username         NVARCHAR(256) NULL,
        SecretProtected  NVARCHAR(MAX) NULL,
        TenantId         NVARCHAR(100) NULL,
        ClientId         NVARCHAR(100) NULL,
        FromAddress      NVARCHAR(256) NOT NULL,
        FromName         NVARCHAR(100) NULL,
        ReplyTo          NVARCHAR(256) NULL,
        AppBaseUrl       NVARCHAR(250) NULL,
        LastTestDate     DATETIME2(3) NULL,
        LastTestResult   NVARCHAR(500) NULL,
        ModifiedBy       NVARCHAR(100) NOT NULL,
        ModifiedDate     DATETIME2(3) NOT NULL CONSTRAINT DF_tblCompanyMailSettings_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        RowVersion       ROWVERSION NOT NULL
    );
    PRINT 'Created dbo.tblCompanyMailSettings';
END
GO
IF OBJECT_ID(N'dbo.tblNotificationTemplate', N'U') IS NULL
BEGIN
    -- A company's own version of a built-in template (absent = the built-in text is used).
    CREATE TABLE dbo.tblNotificationTemplate
    (
        NotificationTemplateId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNotificationTemplate PRIMARY KEY,
        CompanyId    INT NOT NULL CONSTRAINT DF_tblNotificationTemplate_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                     CONSTRAINT FK_tblNotificationTemplate_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        TemplateKey  NVARCHAR(50)  NOT NULL,
        Subject      NVARCHAR(200) NOT NULL,
        BodyHtml     NVARCHAR(MAX) NOT NULL,
        ModifiedBy   NVARCHAR(100) NOT NULL,
        ModifiedDate DATETIME2(3)  NOT NULL CONSTRAINT DF_tblNotificationTemplate_ModifiedDate DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblNotificationTemplate_Company_Key ON dbo.tblNotificationTemplate (CompanyId, TemplateKey);
    PRINT 'Created dbo.tblNotificationTemplate';
END
GO
IF OBJECT_ID(N'dbo.tblNotificationRule', N'U') IS NULL
BEGIN
    -- A company's settings for one kind of notification (absent = built-in defaults).
    CREATE TABLE dbo.tblNotificationRule
    (
        NotificationRuleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNotificationRule PRIMARY KEY,
        CompanyId      INT NOT NULL CONSTRAINT DF_tblNotificationRule_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                       CONSTRAINT FK_tblNotificationRule_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        RuleKey        NVARCHAR(50)  NOT NULL,
        IsEnabled      BIT NOT NULL,
        EmailEnabled   BIT NOT NULL,
        InAppEnabled   BIT NOT NULL,
        DaysBefore     NVARCHAR(100) NULL,     -- e.g. "90,60,30,7,0"
        RecipientRoles NVARCHAR(400) NULL,     -- role names, comma-separated
        LastRunDate    DATE NULL,
        ModifiedBy     NVARCHAR(100) NOT NULL,
        ModifiedDate   DATETIME2(3)  NOT NULL CONSTRAINT DF_tblNotificationRule_ModifiedDate DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_tblNotificationRule_Company_Key ON dbo.tblNotificationRule (CompanyId, RuleKey);
    PRINT 'Created dbo.tblNotificationRule';
END
GO
IF OBJECT_ID(N'dbo.tblNotificationPreference', N'U') IS NULL
BEGIN
    -- A user's choice to receive a kind of notification in the app only (no e-mail).
    CREATE TABLE dbo.tblNotificationPreference
    (
        CompanyId    INT NOT NULL CONSTRAINT DF_tblNotificationPreference_CompanyId DEFAULT (CAST(SESSION_CONTEXT(N'CompanyId') AS INT))
                     CONSTRAINT FK_tblNotificationPreference_CompanyId REFERENCES dbo.tblCompany (CompanyId),
        UserId       INT NOT NULL CONSTRAINT FK_tblNotificationPreference_UserId REFERENCES dbo.tblUser (UserId),
        RuleKey      NVARCHAR(50) NOT NULL,
        EmailEnabled BIT NOT NULL,
        ModifiedDate DATETIME2(3) NOT NULL CONSTRAINT DF_tblNotificationPreference_ModifiedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_tblNotificationPreference PRIMARY KEY (CompanyId, UserId, RuleKey)
    );
    PRINT 'Created dbo.tblNotificationPreference';
END
GO
DECLARE @t NVARCHAR(128), @sql NVARCHAR(MAX);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT v FROM (VALUES (N'tblCompanyMailSettings'), (N'tblNotificationTemplate'), (N'tblNotificationRule'), (N'tblNotificationPreference')) x (v);
OPEN c; FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'dbo.' + @t) AND predicate_type = 0)
    BEGIN
        SET @sql = N'ALTER SECURITY POLICY sec.CompanyIsolation
            ADD FILTER PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.' + QUOTENAME(@t) + N',
            ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.' + QUOTENAME(@t) + N' AFTER INSERT,
            ADD BLOCK PREDICATE sec.fn_CompanyAccess(CompanyId) ON dbo.' + QUOTENAME(@t) + N' AFTER UPDATE;';
        EXEC sys.sp_executesql @sql;
        PRINT 'Row-Level Security added to ' + @t;
    END
    FETCH NEXT FROM c INTO @t;
END
CLOSE c; DEALLOCATE c;
GO
IF DATABASE_PRINCIPAL_ID(N'amgh_app_role') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON dbo.tblCompanyMailSettings TO amgh_app_role;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tblNotificationTemplate TO amgh_app_role;   -- delete = back to the built-in text
    GRANT SELECT, INSERT, UPDATE ON dbo.tblNotificationRule TO amgh_app_role;
    GRANT SELECT, INSERT, UPDATE ON dbo.tblNotificationPreference TO amgh_app_role;
    DENY UPDATE ON dbo.tblCompanyMailSettings (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.tblNotificationTemplate (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.tblNotificationRule (CompanyId) TO amgh_app_role;
    DENY UPDATE ON dbo.tblNotificationPreference (CompanyId) TO amgh_app_role;
END
GO
PRINT 'Script 14 completed.';
GO
