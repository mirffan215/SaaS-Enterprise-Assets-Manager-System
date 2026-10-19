# Asset Register and Master Data (Phase 4)

## Getting started: order of set-up

1. **Master data > Locations, Buildings, Floors, Rooms.** Rename the seeded "Head Office" and add your sites.
2. **Master data > Departments.** Add your departments; IT is seeded.
3. **Master data > Employees.** Add the people who will hold or own assets.
4. **Master data > Vendors.** Add your suppliers. Manufacturers are pre-seeded; add any that are missing.
5. **Assets.** Add assets one by one, or import them from Excel.

Codes (e.g. `HQ`, `IT`, `DELL`) are stored in upper case and are what the Excel import uses. Records that are in use cannot be deleted; untick **Active** instead, and they disappear from drop-downs while old records still show them.

## The asset form

| Tab | Contents | Who sees it |
|---|---|---|
| General | Name, category, sub-category, status, manufacturer, model, serial, legacy tag | Everyone with Assets.View |
| Technical | Hostname, IP, MAC, OS, CPU, RAM, storage, firmware, IMEI, phone | Everyone |
| Placement | Department, location → building → floor → room (each list filters the next) | Everyone |
| Procurement | Vendor, PO, invoice, date, cost, currency, useful life, salvage value; book value on the details page | **Assets.ViewFinancials** only |
| Information security | Owner, classification, criticality (RTO/RPO), risk, encryption/endpoint/MDM/backup flags, review date | **Assets.ViewSecurityDetails** only |
| Lifecycle | In service, end of support, end of life, next audit due, notes | Everyone |

Users without the financial or security permission never see those values on screen, in exports or in import templates, and cannot overwrite them, even with a crafted form post.

**Rules enforced**

- **Asset numbers** (`AMGH-IT-LAP-00001`) are assigned on save, from the category, and never reused. The category cannot change after creation.
- **New assets** start as In stock, Reserved, Under maintenance or Retired. *Assigned* is set only by the assignment workflow (Phase 6), so every hand-over is recorded.
- **Assigned, Pending disposal and Disposed** statuses are locked on the form. Disposed assets are read-only.
- **Uniqueness:** a serial number must be unique per manufacturer, and a hostname must be unique overall.
- **Field formats:** MAC addresses are normalised to `3C:52:82:1A:2B:3C`; IPv4 needs four numbers; IMEI has 14 to 16 digits.
- **Sensitive data** requires a classification. Assets that store sensitive data but are not encrypted are highlighted on the details page.
- **Deleting** is for mistaken entries only. It is blocked for assigned assets and assets in disposal, and is a soft delete: the number stays reserved and the history is kept.
- **Concurrent edits:** if two people edit the same asset, the second save is refused with "changed by another user".

## Search, filters and export

- **Search** matches asset number, name, serial, hostname, model, legacy tag, IP, and the custodian's name or employee number.
- **Filters:** category, status, department and location, plus "More filters" for manufacturer, classification, criticality, available to assign, and include disposed.
- **Export to Excel** (permission Assets.Export) exports the current filtered list, up to 50,000 rows. Each export is recorded in the audit log with the filter and row count (ISO 27001 A.8.12).

## Excel import

1. **Download the template.** Assets > Import > *Download the template*. The second sheet lists every valid code.
2. **Fill the template.** Delete the example row and fill one row per asset, up to 1,000 per file. Dates can be Excel dates or `yyyy-mm-dd`; Yes/No columns take `Y` or `N`.
3. **Upload the file.** Every row is checked: required columns, codes, location hierarchy, formats, duplicate serials and hostnames (within the file and against the register), and all form rules. Problems are listed with their Excel row numbers.
4. **Confirm.** This appears only when there are no errors. All assets are then created together in one transaction, so either all rows are imported or none.

Custodians are not imported. Assign assets after import so each hand-over and acknowledgement is recorded.

## Attachments

On the asset's **Documents** tab (permissions Documents.View / Upload / Delete) you can attach invoices, hand-over forms, warranty and disposal certificates, photos, and similar files.

- **Allowed files:** PDF, PNG, JPG, DOCX, XLSX, MSG, TXT and CSV, up to 25 MB each.
- **Type check:** each file's real content must match its extension, so a renamed executable is refused.
- **Storage:** files are kept under `C:\AMGH\Documents` with random names and a SHA-256 hash, outside the web site. They always download as attachments.
- **Removing** an attachment hides it from the asset but keeps the file for the audit trail.

## History

The **History** tab shows every recorded event for the asset: creation, changes to key fields (status, location, custodian, classification, security flags, cost), attachments, and later assignment, movement, audit and disposal events. It is written by the database itself and cannot be edited.
