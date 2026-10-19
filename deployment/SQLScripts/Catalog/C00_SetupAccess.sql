/* =====================================================================
   First-time setup of the SaaS installation on a SQL Server instance. Idempotent.
   Run by deployment\PowerShell\Install-SaaS.ps1 (sqlcmd, Windows authentication, as a sysadmin).
   Variables: CatalogDatabase, ProvisionerLogin, ProvisionerPassword, AppIdentity (e.g. IIS APPPOOL\EAMSCloud)

   * the catalog database (it lists the customers and holds no customer data);
   * a provisioning login that may create databases and logins, used only by the application when it creates a new customer;
   * the application identity: read/write on the catalog, nothing else here. On each customer database it receives
     least-privilege rights from script 08 (it can read and write, never delete or change the schema).
   ===================================================================== */
SET NOCOUNT ON;
GO
IF DB_ID(N'$(CatalogDatabase)') IS NULL
BEGIN
    CREATE DATABASE [$(CatalogDatabase)];
    PRINT 'Created database $(CatalogDatabase).';
END
GO
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(ProvisionerLogin)')
BEGIN
    CREATE LOGIN [$(ProvisionerLogin)] WITH PASSWORD = N'$(ProvisionerPassword)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [master];
    PRINT 'Created login $(ProvisionerLogin).';
END
ELSE
    PRINT 'Login $(ProvisionerLogin) exists: its password is left unchanged.';
IF IS_SRVROLEMEMBER(N'dbcreator', N'$(ProvisionerLogin)') <> 1 ALTER SERVER ROLE [dbcreator] ADD MEMBER [$(ProvisionerLogin)];
IF IS_SRVROLEMEMBER(N'securityadmin', N'$(ProvisionerLogin)') <> 1 ALTER SERVER ROLE [securityadmin] ADD MEMBER [$(ProvisionerLogin)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppIdentity)')
BEGIN
    CREATE LOGIN [$(AppIdentity)] FROM WINDOWS WITH DEFAULT_DATABASE = [master];
    PRINT 'Created login $(AppIdentity).';
END
GO
USE [$(CatalogDatabase)];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppIdentity)')
    CREATE USER [$(AppIdentity)] FOR LOGIN [$(AppIdentity)];
ALTER ROLE [db_datareader] ADD MEMBER [$(AppIdentity)];
ALTER ROLE [db_datawriter] ADD MEMBER [$(AppIdentity)];
GO
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants
    (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED,
        Code             NVARCHAR(10)   NOT NULL,
        Name             NVARCHAR(200)  NOT NULL,
        ConnectionString NVARCHAR(2000) NOT NULL,
        IsDemo           BIT            NOT NULL CONSTRAINT DF_Tenants_IsDemo DEFAULT (0),
        IsActive         BIT            NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT (1),
        CreatedAtUtc     DATETIME2(3)   NOT NULL CONSTRAINT DF_Tenants_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_Tenants_Code CHECK (Code NOT LIKE N'%[^a-z0-9]%' AND LEN(Code) BETWEEN 2 AND 10)
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_Tenants_Code ON dbo.Tenants (Code);
    PRINT 'Created dbo.Tenants';
END
GO
PRINT 'Setup completed.';
GO
