# Release 2.9: asset categories, numbering and fixed-asset fields (Issue 4)

## Fixes (package `AMGH.ITInventory-Release2.9-Fix3.zip`)

- **Build error:** three database mapping files (asset, company, number series) had their new fields inserted in the wrong place, so `dotnet build` failed with `CS1026 ) expected` and `CS1513 } expected`. Corrected; the fields are unchanged.
- **Asset and licence numbers of other companies and non-IT categories:** the database still only accepted numbers starting with `AMGH-IT-` (and licences with `AMGH-IT-LIC-`). That refused, for example, `AMGH-FA-00001` and every asset of another company. Script 16 now replaces both rules with a company-neutral one: capital letters, digits and the series' separator (`-` `/` `.` `_` or none), 3-30 characters; licence numbers must contain `LIC`. Existing numbers are unaffected.
- **Error 500 after sign-in** ("Invalid object name 'dbo.Notifications'"): since Release 2.5, the mapping that tells the application the in-app notifications live in `tblNotification` was missing (a new file with the same name had replaced it), so every page that shows the notification bell failed. Restored; the e-mail settings mappings now have their own file. No database change.
- **Build error CS8604** in the company wizard (`PlatformController.cs`): an unnecessary null check made the compiler treat the form data as possibly empty. Corrected.

## Deploy (about 10 minutes)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.9.zip` to `C:\AMGH\Release2.9\`, unblock the files, and copy `Migrations` from the 2.8.1 folder.
2. **Build:** `dotnet build AMGH.ITInventory.sln`
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** Script 16 only **adds** columns and one table, and runs automatically right after script 10:
   `.\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed`
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddAssetConfiguration`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

Nothing existing changes. Asset numbers already issued, existing categories and their series stay exactly as they are; for example `AMGH-IT-LAP-00001` remains valid and its series continues.

## Asset categories

**Administration → Asset categories** lists each category with:

- its number series and the **next number** it will issue;
- its number of sub-categories and assets;
- its status.

Edit a category or its sub-categories from there; categories and sub-categories can be added, changed, activated and deactivated.

**Add standard categories** offers the 17 categories for any industry:

| Code | Category | Sub-categories |
| --- | --- | --- |
| IT | IT Equipment | — |
| FA | Furniture & Fixtures | Office Desk, Office Chair, Meeting Table, Cabinet, Sofa, Workstation, Reception Furniture |
| VEH | Vehicles & Fleet | Sedan, SUV, Pickup, Bus, Truck, Forklift, Motorcycle |
| ELE | Electrical & Electronic Equipment | — |
| MACH | Machinery & Production Equipment | Production Machine, Generator, Compressor, Pump, Packaging Equipment |
| TOOL | Tools & Instruments | — |
| BLD | Buildings & Facilities | — |
| LAND | Land & Property | — (no useful life: land is not depreciated) |
| OFF | Office Equipment | — |
| TEL | Telecommunication Equipment | — |
| SEC | Security & Surveillance Equipment | — |
| MED | Medical & Laboratory Equipment | — |
| HVAC | HVAC & Mechanical Systems | — |
| WH | Warehouse & Material Handling | — |
| CON | Construction Equipment | — |
| HOSP | Hospitality Equipment | — |
| OTH | Other Assets | — |

- **Templates, not fixed categories:** they're shared starting points with no company data. The ones you choose are copied into **your company only**, each with its own number series (`AMGH-FA-00001`, `AMGH-VEH-00001`; for company ABC, `ABC-FA-00001`).
- **Safe to repeat:** categories you already have are skipped, and existing categories are never changed.
- **Fully configurable afterwards:** rename, adjust or deactivate them like any other category.

## Asset numbering

**Administration → Asset numbering** shows every number series of your company, with:

- the prefix, separator and number of digits;
- the numbers issued so far, and the **next number** in full;
- the categories that use it.

| You can | Rule |
| --- | --- |
| Change prefix, separator (`-` `/` `.` `_` or none) and digits | Applies to **new** assets only; issued numbers never change, so history is kept when a company code changes. A number may be at most 30 characters. |
| Deactivate a series | Not while active categories use it |
| Start the next number elsewhere (administrators) | **Only forward**, and never onto a number already in use (imported assets included). Sequences are never reset. |

- **Concurrency:** numbers are allocated by SQL Server in a locked, atomic step, so many people can register assets at the same time.
- **Uniqueness** is guaranteed by a database constraint.
- **Independence:** every company's series are independent.
- **Identity:** the asset's internal ID stays separate from its human-readable number.

## Fixed-asset fields

The asset form now also has these fields.

| Group | Fields | Who sees and edits them |
| --- | --- | --- |
| Accounting (Purchase tab) | Capitalisation date, depreciation method (straight line, declining balance, units of production, none), fixed-asset group, **D365 fixed asset ID** (optional, unique within the company), **book value (recorded)** with its "as of" date | People with *View financial details* only. Others neither see them nor change them when saving. |
| General | Condition (new, good, fair, poor, damaged), maintenance required | Everyone who can see the asset |
| Placement | Cost centre | Everyone who can see the asset |

The other fields in your list already existed:

- **On the asset:** description, vendor, invoice number, purchase order, acquisition cost and currency, useful life, residual (salvage) value, branch/location down to room, department, custodian, risk rating.
- **On the asset page, from the lifecycle records** rather than as duplicate fields: warranty dates, last and next maintenance, and disposal date and reason.

**About book value:**

- **No depreciation calculation:** the system does not calculate depreciation for accounting. That needs the accounting book, method, conventions and approvals your finance team defines.
- **The recorded book value** is entered or imported from finance (D365) with its date, and it is the official figure.
- **The estimate:** the existing straight-line figure is now shown as "Estimated value (straight-line)", explained as for information only. It's hidden when the depreciation method is "none".

## Verified

- **Tests:** 236 application tests (20 new), 101 security tests and 39 security probes pass, and all deployment scripts parse. The new tests cover standard categories (own series, safe to repeat, existing data untouched), the numbering rules (format, limits, no going back, no landing on an imported number), and the fixed-asset fields (validation, D365-ID uniqueness, and financial privacy when viewing and saving).
- **In the running application:**
  - four standard categories added with series and sub-categories;
  - a separator change shown in the next number;
  - a bad "next number" refused;
  - a vehicle registered with all nine new fields, which display correctly.

## Next: Release 2.9.1, roles limited to categories

Script 16 already creates `tblRoleCategory`, company-isolated, where a role can only be linked to its own company's categories. The restriction itself comes in 2.9.1:

- **How it will work:** in the data layer, the same central place as company isolation, so that every list, search, report, dashboard, export, label and QR lookup respects it.
- **How it will be verified:** that layer can't be compiled or run in my environment, so 2.9.1 comes with an automated verification script you run on your server against the real database. It proves the restriction holds on every path before anyone relies on it.
