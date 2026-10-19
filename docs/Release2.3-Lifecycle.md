# Release 2.3: asset lifecycle

## Deploy (about 10 minutes)

In an elevated PowerShell on the server:

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.3.zip` to `C:\AMGH\Release2.3\`, run `Get-ChildItem C:\AMGH\Release2.3 -Recurse | Unblock-File`, and copy your `Migrations` folder (from the Release 2.2 folder) into `src\AMGH.ITInventory.Persistence\`.
2. **Build:** `cd C:\AMGH\Release2.3\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`. If it reports an error, stop here and send me the output.
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** This adds the approval-stage tables; the lifecycle tables and workflow procedures already exist:
   ```powershell
   cd deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed -EnableMultiCompany
   ```
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddApprovals`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

**Check:** the menu now has **Lifecycle** and **Approvals**, and every asset page has an **Actions** button and a **Lifecycle** tab.

## What's new

Everything starts from an asset's page. **Actions** offers *Request move*, *Log maintenance / repair*, *Add warranty*, *Verify now* and *Request disposal*. The **Lifecycle** tab shows the asset's warranty, maintenance, movements, licence seats and disposal.

### Movements

1. **Request:** choose the destination (location, building, floor, room), the type (relocation, transfer, repair, return, storage) and a reason.
2. **Approve:** another person approves it in the **Approval Center**.
3. **Complete:** when the asset has physically arrived, someone with *Complete movements* clicks **Mark as moved** under **Lifecycle → Movements**. Only then does the asset's location change.

**Rules (enforced by the database):** one open move per asset, a destination that is consistent (the room belongs to the floor, the floor to the building), no moving disposed assets, and the requester can never approve their own request.

### Maintenance and repairs

1. **Schedule** preventive or corrective (repair) work, with technician or vendor and ticket number.
2. **Start** the job. Optionally *take the asset out of service*, which sets it to Under Maintenance.
3. **Complete** it with what was done, cost (with currency), downtime and the next maintenance date; or **cancel** it with a reason.

On completion, an asset taken out of service returns to **Assigned** if it still has a custodian, otherwise to **In Stock**. Overdue jobs are highlighted. Open repairs feed the dashboard's *Open repairs* card.

### Physical verification

**Lifecycle → Physical verification:**

- **Cycle:** the cycle defaults to the current quarter (e.g. `2026-Q4`); you can name your own.
- **Scanning:** scan the asset label with a USB or Bluetooth barcode scanner (it types into the box), or press **Camera** on a phone or tablet (Chrome or Edge on Android). Both QR labels and barcode asset tags work, and you can also type the number.
- **Checking:** the screen shows where the asset should be and who should have it. Press **Verified as expected**, or record a finding: missing, damaged, wrong location, wrong custodian, or label missing.
- **Effects:** each result sets the next verification due date (12 months), and *Missing* changes the asset's status to Missing.
- **Progress:** checked, verified and findings, plus the list of assets not yet checked, sorted by location.
- **Exceptions:** findings stay open until someone records the corrective action.

The camera is allowed on this page only (browser Permissions-Policy); every other page still blocks it.

### Disposal

1. **Request disposal** from the asset: method and reason. Return the asset from its custodian first. The asset becomes **Pending Disposal**.
2. **Approve** in the **Approval Center**. Rejecting puts the asset back In Stock.
3. **Record disposal** under **Lifecycle → Disposals**, with the evidence the method requires. The database enforces this:

| Method | Required evidence |
| --- | --- |
| Physical destruction, certified e-waste recycling | data sanitisation (method, date, verified by) **and** certificate number |
| Resale, donation, vendor trade-in, lease return | data sanitisation (method, date, verified by) |
| Write-off (lost or stolen) | none (no device to sanitise) |

Completing a disposal marks the asset **Disposed**, releases its licence seats and retires its QR code. Then attach the certificate under the asset's **Documents**.

### Approval Center and approval stages

- **Approval Center:** **Approvals** lists movement and disposal requests waiting for *you* (with a count in the menu), and the other open requests with the reason you can't decide them. Approve with optional remarks, or reject with a reason. Every decision is recorded permanently.
- **Approval stages:** these are optional and set per company under **Approvals → Approval stages** (System Administrator). Without stages, one approval is enough. With stages, each stage must be approved in order by someone holding that stage's role. For example, disposal: stage 1 *IT Asset Manager*, stage 2 *System Administrator*. Nobody can approve their own request or two stages of the same request. Stages can't be changed while requests of that type are waiting.

### Warranties

Add warranties and support contracts from the asset: type, provider, contract number or service tag, start and end, coverage, cost and terms. The status (**Active**, **Expiring** within 90 days, **Expired**) is calculated automatically. **Void** keeps the record for audit but stops it counting as cover. **Lifecycle → Warranties** lists all warranties, filtered by status. The dashboard's warranty cards and charts now fill in.

### Software licences

**Lifecycle → Software licences** shows a list of licences and lets you add them:

- **Details:** number generated automatically (e.g. `AMGH-IT-LIC-00001`), type, publisher, vendor, seats, dates, cost, owner and renewal reminder.
- **Licence key:** stored **encrypted** in SQL Server and never shown in lists. **Reveal key** shows it once, needs *View licence keys*, and records who revealed it and when.
- **Seats:** allocate a seat to an asset (type or scan its number) or to a person, and release it again. Over-allocation is refused, both by the screen and by the database.
- **Compliance status:** *Compliant*, *Renewal due*, *Expired*, *Over-allocated* or *Inactive*.

### New reports

Under **Reports**:

- **Maintenance Report:** jobs, technicians, downtime and cost.
- **Asset Movement Report:** from, to, requested and approved by.
- **Disposal Report:** method, approvals, sanitisation and certificate. It flags completed disposals without recorded sanitisation, and is classified Confidential.
- **Software Licence Compliance:** seats bought, in use and available; expiry and compliance.

## Who can do what (standard roles)

| Action | Roles |
| --- | --- |
| Request moves | System Administrator, IT Asset Manager, IT Technician, Department Manager |
| Approve moves | System Administrator, IT Asset Manager |
| Mark moves completed | System Administrator, IT Asset Manager, IT Technician |
| Maintenance and verification | System Administrator, IT Asset Manager, IT Technician |
| Request disposals | System Administrator, IT Asset Manager, IT Technician |
| Approve disposals | System Administrator, IT Asset Manager, Information Security Officer |
| Record completed disposals | System Administrator, IT Asset Manager |
| Manage warranties, licences, reveal keys | System Administrator, IT Asset Manager |

## Not in this release

- **E-mail reminders** (warranty and licence expiry, overdue maintenance and verification) and scheduled reports: Release R7 (notifications).
- **Approval of high-value assignments** and other sensitive changes: the approval engine currently covers movements and disposals.
