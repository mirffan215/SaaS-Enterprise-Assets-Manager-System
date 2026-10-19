/* =====================================================================
   AMGH Inventory Management System v1.0
   Script 01 - Database creation and encryption infrastructure
   Run in SQLCMD mode (sqlcmd, Deploy-Database.ps1, or SSMS > Query > SQLCMD Mode).
   Variables: DatabaseName, MasterKeyPassword
   ===================================================================== */
SET NOCOUNT ON;
GO
IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    CREATE DATABASE [$(DatabaseName)];
    PRINT 'Database $(DatabaseName) created.';
END
ELSE
    PRINT 'Database $(DatabaseName) already exists - skipping creation.';
GO
ALTER DATABASE [$(DatabaseName)] SET RECOVERY FULL;
ALTER DATABASE [$(DatabaseName)] SET PAGE_VERIFY CHECKSUM;
ALTER DATABASE [$(DatabaseName)] SET AUTO_UPDATE_STATISTICS_ASYNC ON;
GO
-- Readers do not block writers (dashboard queries alongside concurrent edits)
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'$(DatabaseName)') = 0
    ALTER DATABASE [$(DatabaseName)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
USE [$(DatabaseName)];
GO
/* Column-level encryption for secrets (software license keys).
   The certificate and master key MUST be backed up after first deployment
   (see docs/Deployment.md, section "Encryption key backup"). */
IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##')
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = N'$(MasterKeyPassword)';
GO
IF NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'AMGH_DataProtection_Cert')
    CREATE CERTIFICATE AMGH_DataProtection_Cert
        WITH SUBJECT = N'AMGH IT Inventory - column encryption',
             EXPIRY_DATE = '20361231';
GO
IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = N'AMGH_DataProtection_Key')
    CREATE SYMMETRIC KEY AMGH_DataProtection_Key
        WITH ALGORITHM = AES_256
        ENCRYPTION BY CERTIFICATE AMGH_DataProtection_Cert;
GO
PRINT 'Script 01 completed.';
GO
