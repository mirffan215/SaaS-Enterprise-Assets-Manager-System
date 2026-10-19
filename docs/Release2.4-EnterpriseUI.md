# Release 2.4: enterprise user interface

## Deploy (about 5 minutes; no database change)

This release changes only the application. There are no database scripts to run and no EF Core migration to record.

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.4.zip` to `C:\AMGH\Release2.4\`, run `Get-ChildItem C:\AMGH\Release2.4 -Recurse | Unblock-File`, and copy your `Migrations` folder (from the Release 2.3 folder) into `src\AMGH.ITInventory.Persistence\`.
2. **Build:** `cd C:\AMGH\Release2.4\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`
3. **Deploy:** `cd deployment\PowerShell; .\Deploy-WebApp.ps1` (it takes the site offline briefly, then starts it again).

**Also in this package:** `Deploy-Database.ps1` now recognises a database that is already multi-company and no longer needs `-EnableMultiCompany` for it. A database that was never migrated still requires the switch.

## Please check after deploying

The new layout could not be viewed in a real browser in my test environment; everything else was tested automatically. Please take two minutes in **Chrome or Edge**:

- [ ] **Sidebar:** the menu on the left shows your sections, and the current page is highlighted. The **«** button at the bottom collapses it to icons, and the choice is remembered.
- [ ] **Phone or tablet:** the **☰** button at the top left opens the menu.
- [ ] **Theme:** profile menu (your initials, top right) → **Dark**. Pages, tables and dashboard charts switch to dark. **Auto** follows Windows.
- [ ] **Search:** press **/** and type part of an asset name or a serial number; suggestions appear. Scanning a label into the box opens the asset.

If anything looks wrong, send me a screenshot and I'll correct it.

## What's new

### Sidebar navigation

The menu follows the sections of the brief. Only what you may open is shown, so different roles see different menus:

| Section | Items |
| --- | --- |
| Overview | Executive dashboard, Approval Center (with the number waiting for you) |
| Asset management | Asset register, Add new asset, Import from Excel, Asset categories |
| Asset lifecycle | Movements, Maintenance and repairs, Physical verification, Warranties, Software licences, Disposals |
| Organization | Employees, Departments, Locations and buildings, Vendors and manufacturers, All master data |
| Governance | Audit report, Compliance report, Risk assessment, Sign-in history |
| Reporting and analytics | Reports centre, Report history |
| Administration | Users and roles, Approval stages |
| Platform (Super Administrator) | Company directory, Create company |

### Top bar and profile menu

- **Global search** (see below) and the company you are working in.
- **Profile menu** (your initials): name, user name, company and roles; theme (Light, Dark, Auto); Change password; Switch company; Sign out.

### Breadcrumbs

Every page shows where you are, for example *Home › Asset management › Asset register › AMGH-IT-LAP-00001*.

### Global search

- **Opening it:** press **/** or **Ctrl+K** anywhere, or click the search box.
- **What it searches:** assets (number, name, serial number, hostname, IP and MAC address, custodian), employees, departments, locations, vendors and software licences, in your company only. It is not case-sensitive.
- **Suggestions:** they appear while you type. Use the **↑ ↓** keys and **Enter** to open one, or press **Enter** for the full results page.
- **Opening an asset directly:** typing or scanning an **asset number or label** opens the asset straight away.
- **Permissions:** you only get results you may open (for example, people without the master-data permission don't see employees).

### Asset register: columns and saved views

- **Columns:** choose which columns to show. Asset number and name are always shown. The options are category, status, assigned to, department, location, serial number, hostname, manufacturer and model, classification, risk level, criticality, purchase date, warranty end and next verification. Your choice is remembered on that computer.
- **Views:** save the current filters, sort and columns under a name, for yourself or **shared** with colleagues in your company. Pick a view to reopen it, and delete your own views from the same menu. Both menus stay available even when a filter returns no assets.

### Dark mode

Light, dark, or automatic (follows Windows or the phone setting), chosen per person and remembered per browser. Dashboard charts switch colours with the theme. Company branding colours still apply to the sidebar and buttons, while text colours are adjusted automatically for readability on dark backgrounds.

### Other improvements

- **Inactivity warning** and automatic sign-out work as before.
- **Accessibility:** "Skip to content" for keyboard users, the current page marked for screen readers, and reduced animation for users who prefer it.
- **Version:** the footer shows the company and application version.
