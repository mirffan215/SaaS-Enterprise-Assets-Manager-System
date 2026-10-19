# Release 2.11: sign in with user name and password, straight to your company

Includes **Release 2.10** (technician assignment) and all Release 2.9 fixes. If you haven't deployed 2.10 yet, deploy this release instead; the steps are the same.

## Deploy (about 10 minutes)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.11.zip` to `C:\AMGH\Release2.11\`, unblock the files, and copy `Migrations` from the folder of the release you deployed last (2.9 or 2.10):
   ```powershell
   Get-ChildItem C:\AMGH\Release2.11 -Recurse | Unblock-File
   Copy-Item -Recurse -Force C:\AMGH\Release2.9\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\Migrations `
             C:\AMGH\Release2.11\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\
   ```
2. **Build:** `cd C:\AMGH\Release2.11\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database** (script 17 from Release 2.10; Release 2.11 itself has no database change). Run it from the **2.11** folder:
   ```powershell
   cd C:\AMGH\Release2.11\AMGH.ITInventory\deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed
   ```
5. **Record the change for EF Core** (skip if you already did it for 2.10): `.\New-BaselineMigration.ps1 -Name AddTechnicianAssignment`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

## How signing in works now

| Situation | What the person sees |
| --- | --- |
| First visit on a computer (several companies exist) | **User name and password only**: they go straight to their own company |
| Person with one company | Their company |
| Person with several companies | Their **default company**, plus a note naming the others; **Switch company** in the profile menu changes it |
| Next visit on the same computer | The sign-in page of the company used last time ("Change company" to pick another) |
| Company link, e.g. `https://<server>/w/ABC` | That company's sign-in page, as before |
| "Choose a company instead" | The company picker, as before (it now also has a link back to direct sign-in) |

**Default company:** set per person under **Administration → Users and roles** (the person's company access, *default*). Without a default, the first of their companies by code is used.

## Security (unchanged rules)

- **Password first:** the company is chosen only after the password is verified, using the person's own active access. Nobody can reach a company they have no access to.
- **Nothing revealed:** a wrong password gives the same answer as an unknown user name, so it never tells whether the account exists or which company it belongs to.
- **Disabled access:** a person whose access was switched off in every company is told the account is disabled. A person with no access to any active company is told so and asked to contact the administrator.
- **Logging:** every sign-in is still recorded in the company signed in to, and failures without a company are recorded at platform level.

## Verified

- **Security tests:** 108 pass (7 new for direct sign-in: default company, changed default, no default, access switched off, wrong password, unknown user, and both wording paths of a suspended company).
- **Application tests:** 246 pass, including the 10 for technician assignment (Release 2.10).
- **Web run:** plain sign-in page, wrong password, sign-in to the default company with the note about the other company, remembered company on the next visit, and the picker with its new link.
