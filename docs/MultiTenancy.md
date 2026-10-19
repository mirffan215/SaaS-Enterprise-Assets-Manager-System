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
* Company data export: request, second administrator approves in the Approval Center, one-time token, AES-256 encrypted `.zip`.
* Configuration transfer: XML export/import (schema `CompanyConfiguration.xsd`), diff preview, copy between companies.
* Recycle bin with one-click restore.

## Not covered by the automated tests

The unit tests (`tests/AMGH.ITInventory.UnitTests`) cover script splitting, tenant resolution, the XML schema and diff, the CSV/zip writers
(including a real 7-Zip round trip) and the recycle bin. Running the SQL scripts, provisioning and the web screens against a real SQL Server
needs an integration environment and has not been done here.
