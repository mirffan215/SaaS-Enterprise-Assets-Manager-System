# Release 2.2: executive dashboard and reports

## Deploy (about 10 minutes)

In an elevated PowerShell on the server:

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.2.zip` to `C:\AMGH\Release2.2\`, run `Get-ChildItem C:\AMGH\Release2.2 -Recurse | Unblock-File`, and copy your `Migrations` folder (from the Release 2.1 folder) into `src\AMGH.ITInventory.Persistence\`.
2. **Build:** `cd C:\AMGH\Release2.2\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`. If it reports an error, stop here and send me the output.
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** This adds two tables, report history and saved reports, both company-isolated:
   ```powershell
   cd deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed -EnableMultiCompany
   ```
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddReports`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

**Check:** sign in. You land on the **Dashboard**, and a **Reports** menu appears.

## Dashboard

The dashboard opens after sign-in for everyone with the *View dashboard* permission. **Overview** (`/?overview=true`) still shows the previous page.

**12 KPI cards.** Hover over a card to see exactly what it counts. Click a card to open the asset list showing those assets, with a "Showing: …" bar to clear the filter. Each card and its list use the same definition, so the numbers always agree.

| Card | Counts |
| --- | --- |
| Total assets | All assets except disposed |
| Assigned / Unassigned / Under maintenance / Disposed / Missing or stolen | By status. Each asset has one status, so nothing is counted twice |
| Open repairs | Corrective maintenance scheduled or in progress |
| Missing QR code | Active assets for which no QR label has been generated |
| Warranty expired / expiring | Latest warranty ended / ends within 90 days |
| High-risk assets | Risk level High or Critical |
| Critical assets | Business criticality Critical |

**Charts.** Click a slice or bar to open the matching assets.

- **Breakdowns:** category, status, department, building, risk level.
- **Trends:** monthly growth over the last 12 months, warranty expiry over the next 12 months, asset age.
- **Values (financial permission only):** purchase value by category, and the top 10 departments by value. Values are shown in the company's base currency only; assets bought in other currencies are listed as excluded rather than mixed in.

**Assets requiring attention:** missing or stolen assets, critical assets out of warranty, sensitive data on unencrypted disks (security permission only), and overdue physical verification.

**Filters:** acquisition date range, department, building, category and status. Use **Refresh** to reload, and **Print / PDF** to print or save as PDF.

> **Warranty data:** warranty cards, charts and the warranty report show zero until warranties are recorded. The warranty screens arrive with the lifecycle release (R4).

## Reports

**Reports** in the menu opens the reports centre. All reports cover your company only.

| Report | Contents | Date filter |
| --- | --- | --- |
| Asset Register | Every asset: category, model, serial, status, department, location, custodian, purchase date and value, warranty end, classification, criticality, risk | Purchase date |
| Assignment Report | Issue and return dates, Current / Overdue / Returned, signed acknowledgement | Assignment date |
| Warranty Report | Provider, contract, start, end, status, days remaining | Warranty end |
| Risk Assessment Report | Criticality (with RTO/RPO), risk level, classification, encryption, endpoint protection, owner, security-review due date and status | Last security review |
| Compliance Report | Assets with issues: no QR code, no location or department, no serial number, not classified, warranty expired, verification overdue, missing or stolen, sensitive data on an unencrypted disk | Registration date |
| Audit Report *(audit permission)* | Every recorded change: who, when, what, old and new values | Event date (default: last 30 days) |

**On every report:**

- **Filters:** dates, category, department, location, building, status, and free-text search across all columns.
- **Columns:** tick the ones you want.
- **Layout:** sort by any column (ascending or descending), group by a column (group headings with counts), and page through the results (50 to 500 rows per page).
- **Exports:**
  - **Excel:** formatted, with frozen headers, filters and real dates, plus an "About this report" sheet listing the filters.
  - **CSV:** UTF-8, so Arabic text opens correctly in Excel.
  - **Word:** company logo, title, filters, summary and a table whose header repeats on every page; footer with the classification and "Page X of Y"; landscape for wide reports.
  - **Print / PDF:** an A4 print page. Choose *Save as PDF* in the print dialog; Chrome or Edge gives page numbers and a footer on every page.
- **Saved reports:** save the current filters, columns, sort and grouping under a name, for yourself or shared with colleagues in your company.
- **Generation history:** every export and print is recorded (who, when, which filters) under **Reports** → **Generation history**. The history cannot be edited or deleted.

**Permissions** (as set up in the standard roles):

- **Seeing reports:** *View reports* (System Administrator, IT Asset Manager, Information Security Officer, Auditor, Department Manager).
- **Exporting and printing:** *Export reports* (all of those except Department Manager).
- **Restricted columns:** purchase values need *View financials*, and encryption and protection columns need *View security details*. They are removed from the screen and from every export for users without the permission.

## Not in this release

- **Maintenance, Movement, Disposal reports** and an **Executive Summary**: these need the lifecycle data (R4).
- **Scheduled reports by e-mail:** this needs the notification release (R7).
- **Department-only view for Department Managers:** today they see the whole company's figures. Restricting them to their own department is part of the R2 rest items.
