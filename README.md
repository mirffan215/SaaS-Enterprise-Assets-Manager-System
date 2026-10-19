# AMGH Inventory Management System — v1.0

IT asset lifecycle management (procurement → assignment → movement → maintenance → audit → disposal) for AMGH, designed to support the AMGH ISMS (ISO/IEC 27001:2022) and deployed on a local Windows Server / IIS / SQL Server 2022 environment.

## Security model (local, self-contained)

Authentication and authorisation are handled entirely inside the application and its database: no Active Directory, Entra ID / Azure, or Microsoft Graph dependency.

| Control | Implementation |
|---|---|
| Identity | Local user accounts in `tblUser` |
| Password storage | PBKDF2-HMAC-SHA512, 100,000 iterations, random salt (ASP.NET Core Identity v3 format) |
| Password policy | Minimum 12 characters with upper, lower, digit and symbol; history in `tblUserPasswordHistory` blocks reuse; forced change at first sign-in |
| Brute-force protection | `AccessFailedCount` + `LockoutEndUtc` |
| Session invalidation | `SecurityStamp` rotated on password/role change or account disable |
| Authorisation | RBAC: 7 roles, 45 permissions (`tblRole`, `tblPermission`, `tblRolePermission`, `tblUserRole`) |
| Logging | Append-only `tblAuditLog`, `tblLoginAudit`, `tblAssetHistory` (UPDATE/DELETE blocked by triggers and DB permissions) |
| Encryption | License keys encrypted at column level (AES-256, certificate-protected); HTTPS-only transport |
| Least privilege | App identity cannot delete rows or alter the `dbo` schema |

The account and session tables are designed so that MFA (e.g. TOTP authenticator apps, which work fully offline) can be added later without schema rework. See `docs/Roadmap.md`.

## Repository layout

```
AMGH.ITInventory.sln
Directory.Build.props          shared build settings (net8.0, nullable, warnings-as-errors)
src/
  AMGH.ITInventory.Domain      41 entities, 20 enums, value objects, constants
  AMGH.ITInventory.Shared      permissions, roles, Result types, DB error numbers, exceptions
  AMGH.ITInventory.Application use cases: assets, import/export, attachments, master data, lookups
  AMGH.ITInventory.Infrastructure Excel (.xlsx) reader/writer, QR encoder, local document storage
  AMGH.ITInventory.Persistence EF Core 8: DbContext, mappings, audit trail, repositories
  AMGH.ITInventory.Security    local sign-in, password policy, lockout, RBAC authorisation
  AMGH.ITInventory.Web         ASP.NET Core 8 MVC site (IIS), user administration
deployment/
  SQLScripts/                  00-08 deployment scripts (idempotent)
  PowerShell/                  Deploy-Database.ps1, New-PasswordHash.ps1, New-BaselineMigration.ps1, Deploy-WebApp.ps1
docs/
  Database.md                  schema, numbering, integrity rules, ISO 27001 mapping
  Deployment.md                step-by-step local deployment and key backup
  Persistence.md               data access, auditing, soft delete, errors, migrations
  Security.md                  sign-in, sessions, authorisation, hardening, ISO mapping
  Assets.md                    asset register, Excel import/export, attachments, master data
  LabelsAndAssignments.md      QR labels, label printers, assign / return / hand-over
  Roadmap.md                   delivery phases and status
```

Projects for the later phases (Persistence, Application, Security, Web, etc.) are added to the solution as each phase is delivered.

## Quick start (database)

```powershell
cd deployment\PowerShell
.\Deploy-Database.ps1 -ServerInstance "localhost" `
    -AppLogin "IIS APPPOOL\AMGHITInventory" `
    -AdminEmail "it.admin@amgh.local" -TrustServerCertificate
```

Full instructions: `docs/Deployment.md`.

## Build

```powershell
dotnet build AMGH.ITInventory.sln -c Release
```
