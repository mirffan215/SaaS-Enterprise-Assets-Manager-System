# Release 2.0, step 1: multi-company database migration (rehearsal)

This step changes **nothing in production**. It migrates a *copy* of the production database, checks that the
copy reconciles, tests the rollback, and proves company isolation. Production is migrated later, together with
the Release 2.0 application (step 2). The current v1.x application cannot read data after the migration.

## What the migration does

1. It creates the company tables (`tblCompany`, `tblCompanyUser`, `tblMigrationLog`) and company 1, **AMGH**
   (Al Murjan Group Holding, asset prefix `AMGH-IT`).
2. It adds `CompanyId` to 34 company-owned tables and assigns every existing row to AMGH. No row is deleted and
   no asset is renumbered.
3. It makes uniqueness per company. Two companies may both have a department "IT" or a vendor "DELL"; asset and
   licence numbers stay unique overall, because they contain the company code.
4. It links company tables with composite foreign keys: an asset can only point to a room, employee or vendor of
   its own company.
5. It moves every user into an AMGH membership with their current roles. The deployment `admin` account becomes
   Platform Super Administrator.
6. It reconciles the result: row counts per table, an asset-number checksum, no row without a company, and every
   user with a membership. If any check differs, the script **stops before** Row-Level Security is enabled.
7. It switches on SQL Server Row-Level Security: each connection only sees and writes rows of the company the
   application sets for it.

The run is idempotent. You can run it again safely; finished steps are skipped.

## Rehearse (about 10 minutes)

On the SQL Server machine, in an elevated PowerShell, from the extracted release folder:

```powershell
cd deployment\PowerShell
Unblock-File *.ps1
.\Invoke-MigrationRehearsal.ps1 -TestRollback
```

Add `-ServerInstance "server\instance"` if SQL Server is not the default local instance, `-BackupFolder "D:\SQLBackup"`
to choose where the copy-only backup is written, and `-KeepCopy` to inspect `AMGH_ITInventory_Rehearsal` afterwards.

The script performs these steps:

1. It takes a **copy-only** backup of `AMGH_ITInventory`. Your normal backup chain is not affected, and this file
   is also a valid pre-migration backup.
2. It restores the backup as `AMGH_ITInventory_Rehearsal`.
3. It migrates the copy (scripts 02, 10, 03, 04, 05, 06) and prints the reconciliation report.
4. It rolls the copy back to v1.1, checks that nothing is left, and migrates it again.
5. It runs the isolation checks, which create a test company **DEMO** in the copy only:

| Check | Expected |
| --- | --- |
| Same department code in two companies | allowed |
| AMGH session reading DEMO rows | none visible |
| DEMO session reading AMGH assets, roles, audit log | none visible |
| Inserting a row for another company | blocked by the database |
| Updating another company's rows | 0 rows affected |
| Moving a row to another company | blocked |
| Linking to another company's department | rejected (foreign key) |
| Own-company insert | takes the session's company automatically |
| Application account with no company set | sees no rows at all |
| Changing the session's company after it is set | refused |

6. It drops the copy and prints a summary. **Exit code 0 means the rehearsal passed.** The full log is saved as
   `deployment\PowerShell\Rehearsal_<date>.log`.

Please send me the summary lines, and the log if anything fails. A failure leaves production untouched.

## Rollback (for reference)

- **Before other companies exist:** `10_MultiCompany_Rollback.sql` restores the v1.1 schema without touching any
  data rows. The rehearsal tests this path.
- **After other companies exist:** restore the pre-migration backup and redeploy the v1.1 web application. The
  rollback script refuses to run in this case, because it would merge companies.

## Step 2 (application) and production go-live

See **Release2-GoLive.md**.

## Step 2 contents (for reference)

The Release 2.0 application: it sets the company on every connection, signs users in to a company membership,
adds the EF Core company filter, keeps documents in per-company folders, and includes automated isolation tests.
Production is migrated with `Deploy-Database.ps1 -EnableMultiCompany` in the same maintenance window as that
application deployment. Without `-EnableMultiCompany`, the deployment script refuses to run these scripts.
