# Backup and restore

## What must be backed up

| What | Where | Why |
|---|---|---|
| `ITInv_Catalog` | SQL Server | the list of customers |
| every `ITInv_Tenant_<code>` | SQL Server | each customer's data |
| `Keys` folder | `C:\EAMSCloud\Keys` | without it, stored secrets and sessions cannot be read after a rebuild |
| `Documents` folder | `C:\EAMSCloud\Documents` | uploaded attachments |
| the master-key password | printed by the installer, also in `appsettings.Production.json` | restoring a customer database on another server |

## Back up now

```powershell
cd <repo>\deployment\PowerShell
.\Backup-SaaS.ps1                              # every database, keys and documents; old backups pruned after 14 days
.\Backup-SaaS.ps1 -BackupRoot "D:\Backups\EAMS" -KeepDays 30
.\Backup-SaaS.ps1 -RegisterTask -TaskTime 02:30   # every night; asks for the account to run as
```

Each backup is compressed, checksummed and verified. Copy the backup folder off the server regularly (another machine or storage).

## Restore one customer

On the same server (the database exists): in SSMS or with sqlcmd, with the application stopped for that customer or during a quiet time:

```sql
RESTORE DATABASE [ITInv_Tenant_acme] FROM DISK = N'<path>\ITInv_Tenant_acme_<date>.bak' WITH REPLACE, RECOVERY;
```

On a new server: restore the database the same way, then open the master key once so the license-key encryption works:

```sql
USE [ITInv_Tenant_acme];
OPEN MASTER KEY DECRYPTION BY PASSWORD = N'<master-key password from the installation>';
ALTER MASTER KEY ADD ENCRYPTION BY SERVICE MASTER KEY;
```

Restore the `Keys` folder before starting the site, add the customer to `ITInv_Catalog.dbo.Tenants` if the catalog was lost (or restore the catalog backup), and re-run `Install-SaaS.ps1` on the new server for the site itself.

## Back up and restore from the operator page (buttons)

Open `https://<host>[:port]/Tenant/Admin`, enter the operator key, then click **backups** next to a company:

* **Back up this company now**: a full, compressed, verified backup into the backup folder (`C:\EAMSCloud\Backups` by default).
* The list shows that company's backups (made here or by `Backup-SaaS.ps1`). **Restore** replaces the company's current data with the chosen backup.
  You must type the company code to confirm. Before anything is replaced the backup is verified and a **safety backup of the current state** is taken
  (so a wrong restore can itself be undone); the company is taken offline for the restore, so people using it are signed out of its database for a moment.
* Only a company's own backups can be restored into it. The company's data comes back exactly as it was at backup time, including users and settings.

Set-up for an installation made before this existed (the installer does it for new ones): `.\Set-OperatorAccess.ps1 -SetupBackups`.
The company data export (a zip of CSV files) is for reading and moving data, not for restoring.

## Not covered

Backups run on a schedule you control; there is no "Backup" button in the web application. A restore test (restore a copy under another name and sign in) is worth doing once.
