# Release 2.13: document numbering and ERP references

Includes Releases 2.10 to 2.12 and all 2.9 fixes. It also includes the **fix for saving companies** ("Company has no RowVersion column").

## Deploy (about 15 minutes)

1. **Back up** the database first, as before an upgrade (see the backup commands from the 2.9 upgrade). Script 18 numbers all existing documents.
2. **Prepare the files.** Extract `AMGH.ITInventory-Release2.13.zip` to `C:\AMGH\Release2.13\`, unblock the files, and copy `Migrations` from the **2.12** folder:
   ```powershell
   Get-ChildItem C:\AMGH\Release2.13 -Recurse | Unblock-File
   Copy-Item -Recurse -Force C:\AMGH\Release2.12\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\Migrations `
             C:\AMGH\Release2.13\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\
   ```
3. **Build:** `cd C:\AMGH\Release2.13\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`
4. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
5. **Update the database** from the **2.13** folder. In the output, look for lines like *"tblAssetMovement: numbered 12 existing document(s)"* and *"Script 18 completed."*
   ```powershell
   cd C:\AMGH\Release2.13\AMGH.ITInventory\deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed
   ```
6. **Check that every document got a number.** Every line should show `0`:
   ```powershell
   sqlcmd -S localhost -E -C -d AMGH_ITInventory -Q "SET NOCOUNT ON; SELECT 'Movements' AS Documents, COUNT(*) AS WithoutNumber FROM dbo.tblAssetMovement WHERE DocumentNumber IS NULL UNION ALL SELECT 'Maintenance', COUNT(*) FROM dbo.tblAssetMaintenance WHERE DocumentNumber IS NULL UNION ALL SELECT 'Disposals', COUNT(*) FROM dbo.tblAssetDisposal WHERE DocumentNumber IS NULL UNION ALL SELECT 'Assignments', COUNT(*) FROM dbo.tblAssetAssignment WHERE DocumentNumber IS NULL UNION ALL SELECT 'Verifications', COUNT(*) FROM dbo.tblAssetAudit WHERE DocumentNumber IS NULL UNION ALL SELECT 'Warranties', COUNT(*) FROM dbo.tblWarranty WHERE DocumentNumber IS NULL"
   ```
7. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddDocumentNumbering`
8. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

## Fix: saving companies

Saving a company always failed with an error page. The log showed *"Company has no RowVersion column"*: the check that detects two people editing the same record only accepted some record types. It now accepts any record that has a version column. The check against simultaneous edits still works: if someone else changed the company meanwhile, you are asked to reload.

## Document numbers

Every transaction now gets a number, like an SAP document number:

| Document | Number (continuous) | Number (yearly) |
| --- | --- | --- |
| Asset movement | `AMGH-MOV-000123` | `AMGH-MOV-2026-000123` |
| Maintenance / repair job | `AMGH-MNT-000045` | `AMGH-MNT-FY27-000045` |
| Disposal | `AMGH-DSP-000012` | … |
| Assignment (hand-over) | `AMGH-ASG-000088` | … |
| Physical verification | `AMGH-VER-000301` | … |
| Warranty | `AMGH-WRN-000017` | … |

- **Per company:** each company has its own sequences, with its company code in front.
- **Given when created,** in the same transaction, so a failed save never uses up a number. Rejected and cancelled documents keep their number, so the sequence has no gaps.
- **Never changes.** Re-scanning an asset in the same verification cycle updates that verification but keeps its number.
- **Existing documents** are numbered by script 18, in the order they were created.

### Numbering per company (Company setup → Configuration → Document numbering)

| Setting | Options |
| --- | --- |
| Numbering | **Continuous** (default) or **Yearly**: restarts at 1 each fiscal year, as SAP does |
| Fiscal year starts | Any month. With a start other than January, the year shown is the year the fiscal year **ends** in: with a July start, July 2026 to June 2027 is `2027` / `FY27`. |
| Year shown as | `2026` or `FY26` |
| Digits | 5 to 8 (numbers never get cut; past the digits they simply grow) |

The page shows a live example. Changing the setting affects **new** documents only; numbers already issued stay valid and searchable. Switching back to continuous carries on where the continuous sequence stopped.

### Where numbers appear

- **Lists:** a *Document* column in Movements, Maintenance, Disposals, Warranties and Verification exceptions. In the Approval Center, on each request.
- **Asset page:** on assignments and in the Lifecycle tab.
- **Reports:** a *Document no.* column (the last column) in the Movement, Maintenance, Disposal, Warranty and Assignment reports.
- **E-mails:** approval request and decision e-mails show the number. If you customised those templates, add `{{DocumentNumber}}` where you want it.
- **Printed hand-over form:** prints the assignment number (older forms keep their `HO-…` reference).

### Document pages

Click a number to open its page: all details, the approval steps (movements and disposals) and the ERP reference. A short link opens any document by its number: `https://<server>/d/AMGH-MOV-000123`. That's useful in e-mails or from an ERP.

### ERP reference

Each document has an optional **ERP reference**, for the matching document in SAP or another ERP (e.g. the asset retirement posting for a disposal). Enter it on the document page:

- **Who:** anyone who may manage that kind of document. That means approving moves, managing maintenance, completing disposals, assigning, performing verifications or managing warranties.
- **Searchable:** it is searchable like the document number.
- **No mix-ups:** it cannot be one of this system's own document numbers, so search never mixes the two.

### Search

The search box now also finds:

- **documents** by number, ERP reference, maintenance ticket, change-request number, destruction certificate or warranty contract;
- **assets and licences** by purchase order and invoice number.

Typing or scanning an **exact document number** opens the document. You only find document types your role may view; other types are not searched at all. As always, only your current company is searched.

## Verified

- **Application tests:** 279 pass. 33 are new:
  - formats, fiscal years starting in July, both year formats and the digits;
  - numbering at creation for all six types, unique and without gaps;
  - yearly restart, and switching back to continuous;
  - re-scan keeps the number;
  - document pages, the financial field hidden without permission, ERP rules;
  - search by number, ticket, ERP reference and PO, with permissions;
  - the report column.
- **Security tests:** 116 pass (4 new: numbering defaults, invalid settings refused, settings saved).
- **Real browser:** 10 checks.
  - The number in the list and the document page; saving the ERP reference; search by exact number, by ERP reference and by short link.
  - The company setup section with its live example.
  - This check found that the live example didn't run on the company edit page; it was fixed before packaging.
- **Builds and mappings:** the web project builds under the strict rules (Debug and Release); data-model mapping checks are clean (953 references).
- **Not tested here:** the SQL of script 18 (there is no SQL Server in the build environment). It follows the patterns of the earlier scripts, and step 6 above confirms the result on your server.
