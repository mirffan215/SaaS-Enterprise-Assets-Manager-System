# Release 2.0 go-live: multi-company foundation in production

After this release, AMGH's data lives in company workspace **AMGH (Al Murjan Group)**, isolated by the application
and by SQL Server Row-Level Security. Users see the same data and features as before, plus their company name next
to their user name. Everyone must sign in once more after the upgrade, because sessions from v1.x are not accepted.

The migration was rehearsed successfully on a copy of production on 14 October 2026: migration, reconciliation,
rollback round trip and isolation checks all passed.

## Before the maintenance window

1. Extract `AMGH.ITInventory-Release2.0.zip` to `C:\AMGH\Release2.0\` and unblock the files:
   ```powershell
   Get-ChildItem C:\AMGH\Release2.0 -Recurse | Unblock-File
   ```
2. Copy your `Migrations` folder from the previous release into `src\AMGH.ITInventory.Persistence\`.
3. Build:
   ```powershell
   cd C:\AMGH\Release2.0\AMGH.ITInventory
   dotnet build AMGH.ITInventory.sln
   ```
   **If the build reports any error, stop here and send me the output.** The EF Core data layer cannot be compiled
   in my environment (its packages cannot be downloaded there). Every other project compiled and passed its tests.
4. Schedule about 20 minutes of downtime and tell users they will need to sign in again afterwards.

## In the window (elevated PowerShell on the server)

| # | Step | Command |
| --- | --- | --- |
| 1 | Stop the application, so the v1.x app does not run against the migrated database | `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory` |
| 2 | Full backup (the migration refuses to run without a backup from the last 24 hours) | `sqlcmd -S localhost -E -C -Q "BACKUP DATABASE [AMGH_ITInventory] TO DISK = N'D:\SQLBackup\AMGH_ITInventory_PreRelease2.bak' WITH CHECKSUM, INIT"` |
| 3 | Migrate the database (prints the reconciliation and stops if anything differs) | `cd C:\AMGH\Release2.0\AMGH.ITInventory\deployment\PowerShell`<br>`.\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed -EnableMultiCompany` |
| 4 | Move documents into the AMGH folder (`C:\AMGH\Documents\1\…`) | `.\Move-DocumentsToCompanyFolders.ps1` |
| 5 | Record the schema change for EF Core | `.\New-BaselineMigration.ps1 -Name AddMultiCompany` |
| 6 | Deploy and start the Release 2.0 application (starts the pool and checks the site responds) | `.\Deploy-WebApp.ps1` |

Adjust the backup path in step 2 to your backup drive. Step 3 asks for the same passwords as previous deployments.

## Check after go-live (5 minutes)

- [ ] Sign in as `admin`. The header shows **Al Murjan Group** beside your name.
- [ ] **Assets** shows the same number of assets as before.
- [ ] Open an asset that has documents and download one. This confirms the document move.
- [ ] Print one asset tag or QR label.
- [ ] **Users** lists all your users; **Sign-in history** shows your sign-in.
- [ ] Optional database check (all rows should say `PASS`):
  ```powershell
  sqlcmd -S localhost -E -C -d AMGH_ITInventory -Q "SELECT CheckName, Expected, Actual, Passed FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' ORDER BY MigrationLogId"
  ```

## What changed for administrators

- **Users belong to companies.** "Disable" on the Users page now disables the user's access to *this company*. In
  a later release, a user with access to several companies keeps their access to the others.
- **The `admin` account is also Platform Super Administrator.** The platform screens (company directory, setup
  wizard) arrive in the next release.
- **Documents are stored per company** under `C:\AMGH\Documents\<CompanyId>\`. Keep backing up the whole
  `C:\AMGH\Documents` folder.

## If something goes wrong

- **During steps 3 to 6:**
  1. Restore the backup from step 2.
  2. Move the documents back: `.\Move-DocumentsToCompanyFolders.ps1 -Rollback`.
  3. Redeploy the v1.1 package with its own `Deploy-WebApp.ps1`.
- **Same day, before any second company exists:** `10_MultiCompany_Rollback.sql` restores the v1.1 schema without
  losing records created since go-live. Ask me before using it.
