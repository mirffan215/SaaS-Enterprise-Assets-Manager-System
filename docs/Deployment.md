# Deployment Guide

## 1. Prerequisites

| Item | Requirement |
|---|---|
| OS | Windows Server 2019 or later |
| Database | SQL Server 2022 (Standard or Enterprise; Developer for dev/test) |
| Tools | `sqlcmd` (SQL Server command-line utilities), Windows PowerShell 5.1+ |
| Rights | `sysadmin` on the SQL instance for the deploying account |
| .NET (later phases) | .NET 8 Hosting Bundle on the IIS server |

## 2. Deploy

1. Copy the `deployment` folder to the database server (e.g. `C:\AMGH\deployment`).
2. Decide the application identity. For IIS on the same server, use the application-pool identity, e.g. `IIS APPPOOL\AMGHITInventory`. **The app pool must exist before deployment**, because SQL Server can only create a login for an existing Windows identity:

```powershell
Install-WindowsFeature Web-Server, Web-Asp-Net45, Web-Mgmt-Console
Import-Module WebAdministration
New-WebAppPool -Name "AMGHITInventory"
Set-ItemProperty IIS:\AppPools\AMGHITInventory -Name managedRuntimeVersion -Value ""
```

   If IIS runs on a different server, use a dedicated low-privilege service account such as `DOMAIN\svc_itinventory` or a local account instead.
3. Run from an elevated PowerShell session:

```powershell
cd C:\AMGH\deployment\PowerShell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
.\Deploy-Database.ps1 -ServerInstance "localhost" `
    -DatabaseName "AMGH_ITInventory" `
    -AppLogin "IIS APPPOOL\AMGHITInventory" `
    -AdminUsername "admin" `
    -AdminEmail "it.admin@amgh.local" `
    -TrustServerCertificate
```

You will be prompted for two secrets, which are never written to disk or the log:

- **Database master key password.** It protects the encryption key for software-license keys. Store it in the IT password vault.
- **Initial administrator password.** It is hashed on the deployment machine; SQL Server only receives the hash. The admin must change it at first sign-in.

Use `-TrustServerCertificate` only while the instance uses its self-signed certificate. Install a proper TLS certificate on SQL Server for production.

A log file `Deploy-Database_<timestamp>.log` is written next to the script.

### Re-running / upgrading

All scripts are idempotent: tables, indexes and constraints are created only if missing; views, functions, procedures and triggers use `CREATE OR ALTER`; seed data inserts only missing rows and never overwrites administrator changes. Use `-SkipSeed` to re-apply code objects without touching seed data.

### Manual deployment (SSMS)

Open `SQLScripts\00_DeployAll.sql`, enable **Query > SQLCMD Mode**, set the variables at the top, and execute. Generate the admin hash with `PowerShell\New-PasswordHash.ps1`. Never put a plain-text password in the script, and remove the master key password from the file afterwards.

## 3. Encryption key backup (mandatory)

Without these backups, encrypted license keys are unrecoverable after a server rebuild. Store the files and passwords separately, both offline (Business Continuity Policy).

```sql
USE AMGH_ITInventory;
BACKUP MASTER KEY TO FILE = N'D:\SecureBackup\AMGH_ITInventory_DMK.key'
    ENCRYPTION BY PASSWORD = N'<backup-password-1>';

BACKUP CERTIFICATE AMGH_DataProtection_Cert
    TO FILE = N'D:\SecureBackup\AMGH_DataProtection_Cert.cer'
    WITH PRIVATE KEY (
        FILE = N'D:\SecureBackup\AMGH_DataProtection_Cert.pvk',
        ENCRYPTION BY PASSWORD = N'<backup-password-2>');
```

## 4. Post-deployment verification

```sql
USE AMGH_ITInventory;
-- 41 tables, 9 views, 7 functions, 21 procedures, 16 triggers
SELECT type_desc, COUNT(*) FROM sys.objects
WHERE is_ms_shipped = 0 AND type IN ('U','V','FN','IF','P','TR') GROUP BY type_desc;

-- Number generation (rolls back, so no number is consumed)
BEGIN TRAN;
DECLARE @n NVARCHAR(30);
EXEC dbo.usp_AssetNumber_Generate N'LAP', @n OUTPUT;
SELECT @n AS NextLaptopNumber;   -- AMGH-IT-LAP-00001 on a fresh database
ROLLBACK;

-- Seed checks
SELECT RoleName, COUNT(*) AS Permissions FROM dbo.vw_UserEffectivePermissions GROUP BY RoleName;
SELECT * FROM dbo.vw_DashboardKpi;
```

## 5. Backups and maintenance

- The database uses the FULL recovery model. Schedule full backups (daily), differential backups (every 6 hours) and log backups (every 15 to 30 minutes) to meet the RPO in `tblCriticality`.
- Run a weekly index and statistics maintenance job, using Ola Hallengren's scripts or a maintenance plan.
- Periodically restore a backup to a test instance and run the verification queries above.

## 6. Web application (Phase 3)

### One-time prerequisites on the server

1. **Install the .NET 8 Hosting Bundle** from https://dotnet.microsoft.com/download/dotnet/8.0 (under "ASP.NET Core Runtime 8.0.x", choose "Hosting Bundle"). Then run `iisreset`.
2. **Install the .NET 8 SDK** on the machine that runs the deployment script. It needs internet access to download NuGet packages.
3. **Check that IIS and the `AMGHITInventory` app pool exist.** Both were created in Phase 1, step 3; the script also creates the app pool if it is missing.

### Deploy

From an elevated PowerShell:

```powershell
cd C:\AMGH\Phase3\AMGH.ITInventory\deployment\PowerShell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
.\Deploy-WebApp.ps1
```

By default the site is published to `C:\inetpub\AMGHITInventory` at `https://<server-name>`. Without a certificate thumbprint, the script creates a self-signed certificate and trusts it on the server itself. Other PCs will show a certificate warning until you use a certificate from your internal CA:

```powershell
.\Deploy-WebApp.ps1 -HostName "itinventory.amgh.local" -CertificateThumbprint "<thumbprint from the Local Computer certificate store>"
```

Re-run the same command to deploy new versions.

### Server-specific settings

Settings that differ on the server (another SQL instance, time zone, session timeout) go in `C:\inetpub\AMGHITInventory\appsettings.Production.json`. The deployment script never overwrites this file. For example:

```json
{
  "ConnectionStrings": {
    "AMGHDatabase": "Server=localhost\\SQL2022;Database=AMGH_ITInventory;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Application Name=AMGH.ITInventory"
  }
}
```

### First sign-in

1. Browse to `https://<server-name>` and sign in as `admin` with the initial password set during database deployment.
2. Set a new password when prompted.
3. Open **Administration > Users and roles** and create accounts for your team.

### Troubleshooting

| Symptom | Check |
|---|---|
| HTTP Error 500.19 or 500.30 | Hosting Bundle installed? Run `iisreset` after installing it |
| HTTP Error 500.30 with "Login failed for user 'IIS APPPOOL\AMGHITInventory'" | Re-run `Deploy-Database.ps1 -SkipSeed` with the same `-AppLogin` |
| "Access to the path C:\\AMGH\\Documents is denied" | Re-run `Deploy-WebApp.ps1` (it grants the app pool Modify on the documents folder) |
| Signed out after every app-pool recycle | The app pool needs write access to `C:\AMGH\Keys` (the script grants it) |
| Details of any error | `C:\AMGH\Logs\amgh-itinventory-<date>.log` |

## 7. Phase 4 additions

- **Document storage.** Asset attachments are stored in `C:\AMGH\Documents`; the deployment script creates the folder and grants access. Include it in server backups together with the database, because each database record points to a file. To use another folder or drive, set `"Storage": { "RootPath": "D:\\AMGHDocuments" }` in `appsettings.Production.json` and re-run `Deploy-WebApp.ps1 -DocumentsPath "D:\AMGHDocuments"`.
- **No database changes in Phase 4.** Only the web application is redeployed.

## 8. Release 1.1 (asset tags): database update

This release adds one column (`tblQrLabelPrintLog.LabelDesign`) and updates one procedure.

1. Apply the database update. Existing data is kept, and a default value fills the new column for old rows:
   ```powershell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed
   ```
2. Record the change for EF Core, after copying your `Migrations` folder into this release:
   ```powershell
   .\New-BaselineMigration.ps1 -Name AddLabelDesign
   ```
3. Deploy the web application with `.\Deploy-WebApp.ps1`.
