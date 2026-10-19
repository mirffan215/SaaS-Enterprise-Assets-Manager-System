# Release 2.8: Enterprise Asset Management System (EAMS) and company configuration

## Deploy (about 10 minutes)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.8.zip` to `C:\AMGH\Release2.8\`, run `Get-ChildItem C:\AMGH\Release2.8 -Recurse | Unblock-File`, and copy `Migrations` from the 2.7 folder.
2. **Build:** `dotnet build AMGH.ITInventory.sln`
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** Script 15 only **adds** columns; no existing data is changed:
   `.\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed`
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddCompanyProfile`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

## Issue 3: new name

- **The application** is now **Enterprise Asset Management System (EAMS)**, owned by **Al Murjan Group Holding**, with the tagline **Track • Manage • Maintain • Optimize**.
- **Where it changed:**
  - sign-in page, browser titles, sidebar, footer (with the version);
  - workspace picker, hand-over forms;
  - built-in e-mail templates and the e-mail layout;
  - Word reports (footer), printed/PDF reports, and the Excel "About this report" sheet.
- **What did not change, on purpose:** tables, columns, routes, assembly names, the IIS site name and folder paths. Existing links, bookmarks, QR labels and integrations keep working.
- **Your own wording stays:** e-mail templates a company has customised are its own text and are not changed. Review them under *E-mail and notifications → Templates*.

## Issue 2: company configuration

**Platform → Companies → (company)** now has these fields. Fields marked *(new)* come from script 15.

| Section | Fields |
| --- | --- |
| Basic information | Company code (fixed after creation), name (English), **name (Arabic)** *(new)*, **parent company** *(new)*, **company type** *(new)*, industry/sector, commercial registration (CR) number, **VAT number** *(new)*, country, **region** *(new)*, city, address, e-mail, phone, website |
| Branding | Logo (**upload, or load from an https address** *(new)*), primary colour, **secondary colour** (formerly "header colour"), accent colour, sign-in tagline |
| System settings | Default currency, time zone, **date format** *(new)*, **notifications enabled** *(new)*, **asset audit enabled** *(new)* |

How the new settings behave:

- **Parent company:** a company cannot be its own parent, and loops are refused (A under B under A).
- **Time zone and date format** now apply to all dates shown to that company's users. Before this release, every company saw the server's time zone. Date entry fields stay year-month-day, as browsers require.
- **Notifications enabled:** when off, no notifications or reminders are created for that company.
- **Asset audit enabled:** when off, no verification reminders are sent, and *Physical verification* is hidden from the menu.
- **Logo from an address:**
  - **What's accepted:** an https address on port 443 pointing to a PNG or JPEG of at most 512 KB.
  - **How it's stored:** downloaded once and stored like an upload. Pages never load it from the other site.
  - **What's refused:** internal or reserved network addresses, redirects, and anything slower than 10 seconds.
- **Asset numbering format and default categories:** Release 2.9. **Asset approval workflow:** *Approval stages*, inside each company.

### The original error (reference `00-c45b39ff…`)

- **Not yet confirmed:** the root cause, because it needs the log entry for that reference.
- **Already fixed in 2.7:** every company save now shows a clear message instead of "Something went wrong" (missing length checks; concurrency, duplicate, constraint and company-context errors).
- **To find the original cause,** run this on the server and send me the output:
  `Select-String -Path "C:\AMGH\Logs\amgh-itinventory-*.log" -Pattern "c45b39ff79d44e6892ffc4e66a199a28" -Context 2,40`

## Verified

- **Test suites:** 79 security tests, 216 application tests and 39 security probes pass.
- **Every main page** loads.
- **The name** shows on the sign-in page, titles, sidebar and footer. No "IT Inventory" remains on any page.
- **Validation messages** (instead of errors) for an invalid VAT number, a company as its own parent, and an over-long Arabic name.
- **All new fields** save and reload. The date format shows on the sign-in history (`2026-10-16 11:53:02`), and the audit switch hides the menu item.
- **Logo from an address:** internal, `http`, other-port and private-IP addresses are refused, and a public https PNG is stored and served.

## Next: Release 2.8.1

Company-based roles and user access (Issue 5): roles list and add/edit with a permission matrix, assigning a user to several companies with different roles, an access review, and immediate effect when access changes.
