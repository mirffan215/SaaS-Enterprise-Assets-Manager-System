# Release 2.14: import all master data from Excel; asset import up to 10,000 rows

Includes Release 2.13 (document numbering, company save fix) and everything before it. **No database change** in this release.

## Deploy (about 10 minutes)

**If you have not deployed 2.13 yet**, follow the 2.13 guide (`docs/Release2.13-DocumentNumbering.md`) but use the 2.14 folder: it includes the database backup, script 18, the check query and the migration `AddDocumentNumbering`. Then you are done.

**If 2.13 is already deployed:**

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.14.zip` to `C:\AMGH\Release2.14\`, unblock the files, and copy `Migrations` from the 2.13 folder:
   ```powershell
   Get-ChildItem C:\AMGH\Release2.14 -Recurse | Unblock-File
   Copy-Item -Recurse -Force C:\AMGH\Release2.13\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\Migrations `
             C:\AMGH\Release2.14\AMGH.ITInventory\src\AMGH.ITInventory.Persistence\
   ```
2. **Build:** `cd C:\AMGH\Release2.14\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`
3. **Deploy:**
   ```powershell
   cd deployment\PowerShell
   .\Deploy-WebApp.ps1
   ```
   No database script and no new migration: the data model is unchanged.

## Importing master data

**Master data → Import / export (Excel).** This needs the *Manage master data* permission (administrators). Other users don't see the button, and the page refuses them.

1. **Download the workbook.** It has an *Instructions* sheet and one sheet per list:
   - Manufacturers and Vendors;
   - Locations, Buildings, Floors and Rooms;
   - Asset categories and Asset sub-categories;
   - Departments and Employees.

   It contains your current data; with no data yet, it is an empty template.
2. **Edit it in Excel** and save it as `.xlsx`.
3. **Upload it.** The **preview** shows, per sheet, how many rows are new, changed, unchanged or have errors, and lists every problem by sheet and row. **Nothing is saved yet.**
4. **Confirm** with *Import N change(s)*. Everything is saved together, or nothing.

### Rules

| Rule | Detail |
| --- | --- |
| Matching | By **code**; employees by **employee number**. Buildings are matched within their location, floors within their building, rooms within their floor, sub-categories within their category. |
| Create or update | Unknown codes create a record; known codes update it. **Nothing is ever deleted.** |
| Missing column | Leaves that field unchanged. You can import a sheet with only *Employee number* and *Job title*. |
| Empty cell | Clears the field. Exceptions: an empty **Yes/No** cell keeps its value, so a record is never deactivated by accident, and an empty reference the export could not write (e.g. to a deleted department) is left alone. |
| References | Written as codes: an employee's *Department code*, a building's *Location code*, a manager as an *employee number*. They may point to rows further down the file, e.g. a line manager listed after the employee, or a department head on the Employees sheet. |
| Values | Yes/No: Yes, No, True, False, 1, 0. Dates: Excel dates or `yyyy-MM-dd`. Choice lists accept spaces: *On leave*, *Server room*. |
| Number series | An asset category's *Number series* is a series code; a code that doesn't exist yet creates the series. |
| Same rules as the screens | Required fields, lengths, unique codes and e-mail addresses, approval date for approved suppliers, termination date for terminated employees, and so on. |
| Limits | 10,000 rows per sheet; files up to 25 MB. Sheets you leave out are not touched. |
| Audit trail | Every created or changed record is recorded as if edited on screen. Unchanged rows are not saved, so they add no audit entries. |

**Tip for the first data load:** fill the sheets top to bottom as they appear: locations before buildings, departments before employees. The import handles the order for you, but it's easier to check.

## Asset import: 10,000 rows per file

**Assets → Import from Excel** now accepts up to **10,000 rows per file** (was 1,000). All rows are still saved together, or none.

Assets get their numbers one by one, so a 10,000-row file takes about half a minute to a minute to save. Meanwhile, anyone else creating an asset waits for their number. Schedule very large imports outside busy hours.

## Verified

- **Application tests:** 302 pass (23 new for the import). They cover:
  - export, and the location → building → floor → room chain;
  - forward references for line managers and department heads;
  - Excel dates and choice lists with spaces, and a new number series;
  - partial sheets, unchanged re-imports, and a full export → re-import round trip with no changes;
  - a broken workbook rejected with every problem listed and nothing saved, and forged or expired confirmations refused;
  - the empty *Active* cell, and the 10,000-row limits.
- **Real rollback in tests:** the test database now rolls back, so the preview's dry run is genuinely tested.
- **Security tests:** 116 pass. The web project builds under the product's strict rules (Debug and Release).
- **Real browser:**
  - the workbook downloaded from the app and edited with a different Excel library;
  - uploaded, previewed with the right counts, confirmed, and the results checked on the master data screens;
  - a technician (no master data permission) is refused the page and the download.
