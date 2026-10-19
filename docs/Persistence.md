# Persistence Layer (Phase 2)

## Projects

| Project | Contents | NuGet packages |
|---|---|---|
| AMGH.ITInventory.Application | Abstractions used by future services: `IRepository<T>`, `IUnitOfWork`, `IAssetNumberGenerator`, `IAssetLifecycleProcedures`, `ILicenseKeyStore`, request records | none |
| AMGH.ITInventory.Persistence | `AMGHDbContext`, 41 entity configurations, auditing, repositories, unit of work, stored-procedure gateway, DI registration, design-time factory | EF Core SqlServer 8.0.10, EF Core Design 8.0.10, Configuration.Abstractions 8.0.0 |
| AMGH.ITInventory.Shared (additions) | `ICurrentUserService`, `IDateTimeProvider`, application exceptions | none |

## How saving works

`AMGHDbContext.SaveChanges[Async]` does the following in one database transaction:

1. **Captures the audit trail.** For each changed entity it records the action (Create/Update/Delete/Restore), old and new values, and the changed columns. It skips the log tables themselves. Sensitive columns (password hash, security stamp, encrypted license key, QR image) are recorded as changed but their values are written as `***`.
2. **Stamps the audit columns.** It sets `CreatedBy`/`CreatedDate` on insert and `ModifiedBy`/`ModifiedDate` on update, using the current user and the UTC clock. `CreatedBy`/`CreatedDate` can never be overwritten.
3. **Converts deletes into soft deletes.** It sets `IsDeleted`, `DeletedBy` and `DeletedDate`. The application's database login is denied `DELETE` anyway.
4. **Saves, then writes the audit rows.** The audit rows are written after the data, so that new IDENTITY keys are known, and both are committed together.

## Query filters

- **Business records are filtered automatically.** Assets, assignments, licenses, users and similar records get `WHERE IsDeleted = 0` without any extra code. Use `QueryIncludingDeleted()` for audit or restore screens.
- **The 18 master/lookup tables are not filtered,** so an old record still shows the name of a retired category or vendor. Pickers and dropdowns must call `.NotDeleted()` (and filter on `IsActive` where the table has it).

## Concurrency

Every business table has a `ROWVERSION`. Edit forms carry it in a hidden field. The service calls `repository.SetOriginalRowVersion(entity, rowVersionFromForm)` before saving. If another user saved the record in the meantime, the save fails with `ConcurrencyConflictException` ("This record was changed by another user...").

## Errors

`DatabaseExceptionTranslator` converts SQL Server errors into application exceptions:

| SQL error | Exception | Shown to user |
|---|---|---|
| 2601 / 2627 duplicate key | `DuplicateValueException` | Friendly message per index, e.g. "Another asset already uses this hostname." |
| 547 check / foreign key | `DataIntegrityException` | Generic rule message |
| 50001 | `NotFoundException` | Procedure message |
| 50002–51999 (procedures and triggers) | `BusinessRuleException` | Procedure or trigger message, e.g. "A disposal cannot be approved by the person who requested it." |
| ROWVERSION mismatch | `ConcurrencyConflictException` | Reload prompt |

Unknown errors are not translated. Phase 3 logs them with Serilog and shows a generic error page.

## Stored procedures

`IAssetLifecycleProcedures` wraps the lifecycle procedures: assign, return, acknowledge, movement request/approve/reject/complete, audit, disposal request/approve/reject/complete, QR save and label print.

- **Actor:** the acting user is always taken from `ICurrentUserService`. Callers cannot pass a different name, so segregation-of-duties checks in SQL cannot be bypassed.
- **Parameters:** all values are typed `SqlParameter`s; procedure names are compile-time constants.
- **Transactions:** calls join the current EF transaction. Use `IUnitOfWork.ExecuteInTransactionAsync` to combine number generation with an insert, so a failed insert never consumes an asset number.

## Migrations

The SQL scripts are the source of truth. Run `deployment\PowerShell\New-BaselineMigration.ps1` once. It creates an **empty** `Baseline` migration plus the model snapshot, records it in `dbo.__EFMigrationsHistory`, and checks that the EF model matches the database model.

Future schema changes follow three steps:

1. Change the entity and configuration.
2. Run `dotnet ef migrations add <Name>`.
3. Add the same change as an idempotent script in `deployment\SQLScripts`. Production is always deployed with the SQL scripts.
