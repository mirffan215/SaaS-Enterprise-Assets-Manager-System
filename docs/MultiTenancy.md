# Database-per-tenant multi-company SaaS

Each customer (tenant) has its own SQL Server database holding the full EAMS schema (several companies, users, roles, assets).
A small **catalog** database lists the tenants. Single-database mode still works: it is the default (`Tenancy:Enabled` = false).

## Turning it on (appsettings / environment)

| Setting | Meaning |
|---|---|
| `Tenancy:Enabled` | `true` switches to database-per-tenant. Needs `ConnectionStrings:TenantCatalog`. |
| `ConnectionStrings:TenantCatalog` | The catalog database (created with its `Tenants` table at start-up when missing; or run `deployment/SQLScripts/Catalog/C01_TenantCatalog.sql`). |
| `Tenancy:BaseDomain` | `example.com`: `acme.example.com` is tenant `acme`. |
| `Tenancy:AllowQueryParameter` | `true` (development only) lets `?tenant=acme` choose the tenant; it is remembered in a cookie. |
| `Tenancy:ServerConnectionString` | Privileged connection (CREATE DATABASE) used **only** to provision tenants. |
| `Tenancy:TenantConnectionTemplate` | Run-time connection string per tenant, `{database}` is replaced (use a least-privilege login). Empty: the provisioning string with the database name replaced. |
| `Tenancy:AppLogin` | Application login granted rights by script 08. Script 08 is skipped when empty. |
| `Tenancy:MasterKeyPassword` | Column-encryption master key password for new tenant databases (random when empty). |
| `Tenancy:DemoEnabled` / `DemoAdminPassword` | Creates the sandbox tenant `demo` (sample data) at first start and shows *Explore Demo Environment* on the sign-in page. Supply the password from a secret store, not from a committed file. |
| `Tenancy:GlobalSignIn` | `false` (default): choose the company first (code page). `true`: sign in with user name or e-mail and password only; the company is found automatically. |
| `Tenancy:AllowSelfRegistration` | `false` (default). `true` adds *Register your company* (page /Register). |
| `Tenancy:ExportRequiresApproval` | `false` (default): administrators export their own data directly. `true`: a second administrator must approve each export. |
| `Tenancy:ShowDemoCredentials` | Off by default. `true` prints the demo user name and password on the demo sign-in page (public sandbox only). |

Tenant resolution order: sub-domain, `?tenant=` (if allowed), `X-Tenant-Code` header, tenant cookie. A signed-in session carries its tenant
and is rejected in any other tenant. Connection strings in the catalog are encrypted (ASP.NET Data Protection, `enc:` prefix).

## Provisioning a tenant

`ITenantProvisioningService.ProvisionTenantAsync(companyCode, companyName, adminEmail, adminPassword)`:
creates `ITInv_Tenant_{companyCode}`, runs scripts 01 to 19 (order of `Deploy-Database.ps1`), seeds roles/permissions, creates the
administrator `admin`, renames the first company to the customer, registers the tenant in the catalog. A failed run drops the database it created.
The scripts are copied to the web output (`SQLScripts/`); the demo data script is `deployment/LoadTest/Generate-DemoData.sql`.

## Installing

See `docs/Install-SaaS-Windows.md` (interactive installer with the default/custom port question).

## Existing databases

Run `deployment/SQLScripts/19_CompanyDataExport.sql` once on every existing (single-database) installation.

## Features added in this release

* Neutral branding (no customer names in the product): generated SVG badge when a company has no logo.
* Demo tenant with destructive actions blocked (`[BlockInDemo]`).
* Company data export: the company administrator creates the export and downloads it straight away (one-time download, AES-256 encrypted `.zip`, audited). Set `Tenancy:ExportRequiresApproval` to `true` to require a second administrator.
* Configuration transfer: XML export/import (schema `CompanyConfiguration.xsd`), diff preview, copy between companies.
* Recycle bin with one-click restore.

## Not covered by the automated tests

The unit tests (`tests/AMGH.ITInventory.UnitTests`) cover script splitting, tenant resolution, the XML schema and diff, the CSV/zip writers
(including a real 7-Zip round trip) and the recycle bin. Running the SQL scripts, provisioning and the web screens against a real SQL Server
needs an integration environment and has not been done here.

## Database upgrades (schema migrations)

Scripts `01`–`19` build a new company. Every later change is a numbered, repeatable script in `deployment/SQLScripts/Migrations` (`0001_Name.sql`, …; see the README there). Each company records what it has applied in `dbo.tblSchemaMigration` (with a checksum).

- New companies get the baseline and then all migrations.
- Existing companies are upgraded when the site starts, before it takes requests (`Tenancy:AutoUpgrade`, default on) and with **Upgrade all companies** on the operator page (it also shows each company's status). A backup is made before a company is changed; one failing company does not stop the others, and the reason is in the log.
- A migration that was edited after it was applied, or a database newer than the program, is reported instead of being ignored.
- After restoring an older backup, restart the site or use **Upgrade all companies** to bring it to the current version.

## Automated tests against a real SQL Server

`tests/AMGH.ITInventory.UnitTests/SqlServerIntegrationTests.cs` runs the real scripts: provisioning, the demo estate, failed-run clean-up, backup/restore (also through the encrypted zip), test restore and migrations. Set `EAMS_TEST_SQL` (a login that may create databases) and `EAMS_TEST_BACKUPS` (a folder both the test and the server can write) to run them; otherwise they are skipped. `.github/workflows/ci.yml` runs them on every push against SQL Server 2022 in a container.

## Platform console (operator page)

`https://<host>/Tenant/Admin` (operator key) is a console for the person running the installation:

- **Companies at a glance:** status, users (active / all), assets, database size, uploaded-document size, last sign-in, last backup (with ✓ for "passed its test restore" and ☁ for "copied off-site"), and a list of **open warnings** per company (suspended, database unreadable, changes pending, no / old / failed backup, off-site copy failed, no schedule, schedule overdue).
- **Disk space** of the document, backup and off-site volumes; a volume under 15% (or 5 GB) free is shown in red.
- **Suspend / Resume:** a suspended company cannot be reached by anyone (signed-in users are cut off on their next click and everybody sees "temporarily unavailable"); nothing is deleted. Use it for non-payment, an incident or maintenance.
- **Upgrade all companies**, **create company / demo**, per-company **Backups**.
- **Recent operator activity:** who did what (sign-in, create, suspend, resume, backup, restore, upgrade) with the time and address, kept in the catalog database (`dbo.TenantEvents`).

The catalog gained a table for that log. `Install-SaaS.ps1 -Update` now applies `Catalog\C01_TenantCatalog.sql` every time (the application's own database login cannot create tables), so run it once after pulling.

### Monitoring

| Address | Answers | Use |
|---|---|---|
| `/health` | 200 / 503 | the site and the catalog are up |
| `/health/backups` | 200 / 503 | every scheduled backup is recent, checked and its last run succeeded |
| `/platform/metrics` | Prometheus text | `Authorization: Bearer <operator key>`; per-company database size, documents, users, assets, pending changes, backup age and check result, warnings, disk space |

Prometheus example:

```yaml
scrape_configs:
  - job_name: eams
    scheme: https
    authorization: { credentials: "<operator key>" }
    static_configs: [{ targets: ["eams.example.com:8443"] }]
```

Useful alerts: `eams_company_last_backup_age_seconds > 129600`, `eams_company_last_backup_checked == 0`, `eams_company_schema_pending > 0`, `eams_company_warnings > 0`, `eams_disk_free_bytes < 5e9`.
