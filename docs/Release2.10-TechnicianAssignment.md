# Release 2.10: technician assignment for maintenance and repairs

Includes all Release 2.9 fixes (Fix 1-3).

## Deploy (about 10 minutes)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.10.zip` to `C:\AMGH\Release2.10\`, unblock the files, and copy `Migrations` from the 2.9 folder:
   ```powershell
   Get-ChildItem C:\AMGH\Release2.10 -Recurse | Unblock-File
   Copy-Item -Recurse -Force C:\AMGH\Release2.9\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\Migrations `
             C:\AMGH\Release2.10\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\
   ```
2. **Build:** `cd C:\AMGH\Release2.10\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** Script 17 only **adds** one column (the assigned technician), its foreign key and an index. Existing jobs are unchanged.
   ```powershell
   cd C:\AMGH\Release2.10\AMGH.ITInventory\deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed
   ```
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddTechnicianAssignment`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

## What's new

### Choosing the technician

**Log maintenance or repair** (asset → Actions) has a **Technician** list:

- **Who is listed:** users of your company who may manage maintenance, through their role. With the standard roles, that's IT Technicians, IT Asset Managers and System Administrators; a custom role with *Manage maintenance* also counts.
- **Who isn't:** people whose access to the company is suspended, inactive accounts, and users of other companies.
- **Default:** if you are one of those technicians yourself, you are pre-selected; change it if needed.
- **External technician or company:** still a free-text field, for someone without an account (e.g. the vendor's engineer). It can be used together with an assigned technician.

### My jobs

**Asset lifecycle → Maintenance and repairs** has a **My jobs** tab, showing the open jobs assigned to you. The **Technician** column shows the assigned person, any external technician ("with …"), and the ticket number.

### Reassigning

On an open job, **Update → Assign** gives it to another technician, or "(not assigned)". Completed and cancelled jobs keep their technician.

### Report

The **Maintenance Report** column *Technician / vendor* shows the assigned technician first, then the external technician and the vendor.

### No permission changes

Assigning needs *Manage maintenance*, as scheduling already did. Seeing jobs needs *View maintenance*.

## Not in this release

- **E-mail to the technician** when a job is assigned to them (a new notification kind).
- **Technician workload** (open jobs per technician) on the dashboard.
