# Release 2.1: company workspaces

## What's new

| Feature | Where |
| --- | --- |
| **Company picker** before sign-in (logo and name of each active company; search box when there are many) | Sign-in page |
| **Company-branded sign-in** (logo, colours, notice) and a direct link per company: `https://<server>/w/XYZ` | Sign-in page |
| **Create company** wizard: company, branding, organisation, administrator, configuration, review | **Companies** menu |
| **Company management**: edit details and branding, suspend, reactivate, archive, complete an interrupted setup | **Companies** menu |
| **Platform Super Administrator** (`admin`) can sign in to **any** company, with a banner while working there | Sign-in page, or **Companies** → **Open** |
| **Switch company** (signs you out and in to the other company) | User menu (top right) |
| **Asset categories** can now be added and edited, each with its own asset-number series | **Master data** → **Asset categories** |
| Company colours throughout the application; asset tags show the company's own name, phone and logo | Everywhere |

## Deploy (about 10 minutes; no data is moved)

In an elevated PowerShell on the server:

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.1.zip` to `C:\AMGH\Release2.1\`, run `Get-ChildItem C:\AMGH\Release2.1 -Recurse | Unblock-File`, and copy your `Migrations` folder (from the Release 2.0 folder) into `src\AMGH.ITInventory.Persistence\`.
2. **Build:** `cd C:\AMGH\Release2.1\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`. If it reports an error, stop here and send me the output.
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database** (adds the branding table only; the Release 2.0 scripts re-run safely and skip what is done):
   ```powershell
   cd deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed -EnableMultiCompany
   ```
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddCompanyBranding`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

**Check:** sign in as `admin`. You should see a **Companies** menu, and **Master data** should list **Asset categories**.

## Create a company

1. Go to **Companies** → **Create company** and complete the six steps:
   - **Company:** code (e.g. `XYZ`, fixed afterwards), legal and display name, contact details. The phone number is printed on asset tags.
   - **Branding:** logo (PNG or JPEG, up to 512 KB; SVG is refused for security) and three colours, with a live preview.
   - **Organisation:** optional head-office location and IT department.
   - **Administrator:** a new account (a temporary password is shown **once**) or an existing user.
   - **Configuration:** asset-number prefix (e.g. `XYZ-IT`, fixed afterwards because it is printed on tags), currency, time zone.
   - **Review:** check the details and create the company.
2. The new company starts with a copy of AMGH's lists:
   - asset categories and sub-categories;
   - number series, with its own prefix and starting at 1;
   - classifications, and risk and criticality levels;
   - manufacturers;
   - the 7 roles with their permissions.

   The company can change or add to any of these without affecting AMGH. Assets, employees, vendors and locations are not copied.
3. Give the administrator the temporary password and the company's sign-in link (`/w/XYZ`).

## Platform Super Administrator access

- **Signing in to another company:** `admin` can choose any company on the picker, or use **Companies** → **Open**. Without a membership, `admin` works under the role **Platform Administrator** with all permissions.
- **Visibility:** an orange banner is shown while working in another company. The sign-in is recorded in **that company's** sign-in history ("Platform administrator access"), so the company can see it.
- **Platform events:** creating, editing, suspending and archiving companies are recorded as platform events.

## Rules worth knowing

- **Companies are never deleted.** Suspending a company signs its users out and hides it from the picker. Archiving keeps all its records for audit.
- **AMGH (the template) cannot be suspended or archived**, and you cannot suspend the company you are signed in to.
- **Interrupted setup:** if a setup is interrupted, the company shows **setup incomplete**. Use **Complete setup** to copy the lists again; existing entries are skipped.
- **Remembered company:** the last company is remembered on each computer. **Change company** on the sign-in page shows the picker again.
- **Asset categories:** a new category needs a number series. Pick an existing one, or type a new code (e.g. `PRN`) and assets will be numbered `PREFIX-PRN-00001`.

## Still to come in Release 2 (next delivery)

- Authenticator-app MFA, enforceable per company.
- Membership management across companies (add an existing user to another company from the Users page).
