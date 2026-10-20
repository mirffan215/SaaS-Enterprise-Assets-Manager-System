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

## Back up and restore inside the workspace (Administration, Backup and restore)

The workspace's **Platform Super Administrator** (the `admin` created with the company) sees **Administration, Backup and restore**: *Back up this workspace now*,
and a list of backups with a **Restore** button each (type the workspace code to confirm; a safety backup is taken first; everyone signs in again afterwards).
It covers the whole workspace: all its companies, users, assets and settings. Restore is disabled in the demo.

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

## Zip download, upload-restore and schedules (Administration → Backup and restore)

The Platform Super Administrator of a company can:

- **Back up now** – creates a backup of that company's database in the server backup folder.
- **Download as zip** – choose a password (12+ characters, at least three of upper/lower/digits/symbols); the backup is delivered as an AES-256 encrypted zip (opens in 7-Zip/WinZip). Keep the password: it cannot be recovered.
- **Restore from an uploaded zip** – upload the zip and enter its password. The file must be a backup of *this* company's database; a safety backup of the current data is taken first, then the data is replaced.
- **Schedule** – Off, Daily (time of day) or Weekly (day + time), server local time, with "keep the last N scheduled backups" (only files ending `_auto` are pruned; manual backups are never deleted). A run missed while the server was down is made once it is back up. The schedule is stored beside the company's documents, so a restore never rewinds it.

Permissions: the application pool identity needs Modify rights on the backup folder (the installer and `Set-OperatorAccess.ps1 -SetupBackups` set this). Uploads up to 2 GB are allowed on `admin/backup/upload` (`web.config`).

## Proving a backup works ("Checked")

A backup nobody has ever restored is a hope, not a backup. **Check now** (Administration → Backup and restore) restores the file under a temporary name, runs `DBCC CHECKDB` and a sanity query, drops the copy and shows the result and time next to the file (the result is kept in `<backup>.bak.verify`). Your live data is never touched. It needs free disk space for a second copy of the database for a few minutes. Scheduled backups are checked automatically right after they are made, and a failed check is written to the log as an error.
