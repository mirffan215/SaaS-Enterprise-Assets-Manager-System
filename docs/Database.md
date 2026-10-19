# Database Design — AMGH_ITInventory

## Conventions

- Tables use the `tbl` prefix with singular names. Primary keys are `<Entity>Id` (`INT`/`BIGINT IDENTITY`). Junction tables (`tblUserRole`, `tblRolePermission`) use composite keys.
- Constraint names are predictable: `PK_`, `FK_<table>_<column>`, `UX_` (unique), `IX_`, `CK_`, `DF_`.
- Every business table carries the audit columns `CreatedBy`, `CreatedDate`, `ModifiedBy`, `ModifiedDate`, `DeletedBy`, `DeletedDate`, `IsDeleted` and `RowVersion`. All dates are UTC.
- Log tables (`tblAuditLog`, `tblLoginAudit`, `tblAssetHistory`, `tblQrLabelPrintLog`, `tblUserPasswordHistory`) are append-only and have no soft-delete columns.
- Enumerations are stored as readable text (`NVARCHAR(30)`) and restricted by `CHECK` constraints. They match the C# enums in `Domain/Enums` one-to-one.
- `02_Tables.sql` and the C# entities are generated from one schema definition, so column names, types and nullability always match.

## Tables (41)

**Master data (18):** tblAssetCategory, tblAssetSubCategory, tblManufacturer, tblVendor, tblDepartment, tblEmployee, tblLocation, tblBuilding, tblFloor, tblRoom, tblClassification, tblRiskLevel, tblCriticality, tblAssetStatus, tblCurrency, tblDisposalMethod, tblWarrantyStatus, tblAssetNumberSequence.

**Security (8):** tblUser, tblRole, tblPermission, tblRolePermission, tblUserRole, plus three tables added for local authentication and audit: tblUserPasswordHistory, tblLoginAudit, tblAuditLog.

**Transactions (15):** tblAsset, tblAssetAssignment, tblAssetMovement, tblAssetMaintenance, tblAssetAudit, tblAssetDisposal, tblWarranty, tblSoftwareLicense, tblAssetQRCode, tblAssetDocument, tblAssetHistory, tblNotification, tblNotificationQueue, plus tblSoftwareLicenseAllocation (seat tracking) and tblQrLabelPrintLog (label history).

## Asset numbering

Format: `AMGH-IT-<CODE>-<NNNNN>`, for example `AMGH-IT-LAP-00001` or `AMGH-IT-LIC-00001`.

- `dbo.usp_AssetNumber_Generate` increments `tblAssetNumberSequence.LastNumber` atomically under a row lock, so concurrent users never receive the same number.
- **Numbers are never reused.** Trigger `trg_tblAssetNumberSequence_Protect` rejects any decrease of `LastNumber`, and changes to the code or prefix once numbers have been issued. Assets and licenses cannot be hard-deleted (`INSTEAD OF DELETE` triggers plus `DENY DELETE`), so issued numbers stay in the register permanently. `AssetNumber` and `LicenseNumber` cannot be edited.
- If generation runs inside a transaction that later rolls back, that number was never issued. It may be issued to the next asset; this is not reuse.
- Numbers wider than the padding are not truncated (`AMGH-IT-PC-100000`).

## Lifecycle procedures

| Procedure | Rules enforced |
|---|---|
| usp_Asset_Assign / usp_Asset_Return | Asset status must be assignable; one current assignment per asset (also a filtered unique index); employee must not be terminated |
| usp_AssetAssignment_Acknowledge | Custodian acceptance of the Acceptable Use terms |
| usp_AssetMovement_Request / Approve / Reject / Complete | Change-managed moves; location hierarchy validated; **requester ≠ approver** |
| usp_AssetAudit_Record | One record per asset per audit cycle; "Missing" sets status MISSING; next audit date rolled forward |
| usp_AssetDisposal_Request / Approve / Reject / Complete | Asset must be returned first; **requester ≠ approver**; sanitisation evidence and certificate required by method; licence seats released |
| usp_AssetQrCode_Save / usp_QrLabel_LogPrint | Versioned QR codes; every print and reprint logged with a reason |
| usp_SoftwareLicense_SetKey / GetKey | AES-256 encryption; every key reveal written to the audit log |
| usp_Asset_Search | Paged multi-filter search with whitelisted sorting; LIKE wildcards in user input are escaped |
| usp_Dashboard_GetData | KPIs and breakdowns in one round trip |
| usp_Warranty_RefreshStatuses / usp_Notification_QueueExpiryAlerts | Daily jobs; e-mail digests de-duplicated per user per day |

Procedures raise errors 50001–50004 and triggers raise 51001–51043. Both ranges are listed in `Shared/Errors/DatabaseErrorNumbers.cs` so the application can show them as friendly messages.

## ISO/IEC 27001:2022 Annex A mapping

| Control | How the database supports it |
|---|---|
| A.5.9 Inventory of information and other associated assets | Complete asset register; accountable owner (`AssetOwnerEmployeeId`) separate from the custodian |
| A.5.10 Acceptable use | Assignment acknowledgement fields |
| A.5.11 Return of assets | Return workflow; open assignments visible in `vw_CurrentAssignments` |
| A.5.12 / A.5.13 Classification and labelling | `tblClassification` on assets and documents; QR labels |
| A.5.15 / A.5.18 / A.8.2 Access control and privileged access | RBAC tables; protected system roles; at least one active administrator always kept |
| A.5.17 Authentication information | Salted PBKDF2 hashes, password history, lockout, forced change |
| A.5.19–A.5.22 Supplier relationships | Vendor approval and security assessment dates |
| A.5.30 ICT readiness for business continuity | Criticality levels with RTO and RPO |
| A.7.10 Storage media | Storage category; disposal sanitisation records |
| A.7.14 Secure disposal or re-use of equipment | Disposal workflow with sanitisation method, verifier and certificate (NIST SP 800-88) |
| A.8.1 User endpoint devices | Disk encryption, endpoint protection and MDM flags per asset |
| A.8.15 Logging | Append-only audit, login and asset history logs |
| A.8.24 Use of cryptography | Column encryption for license keys; certificate backup procedure |
| A.8.32 Change management | Movement requests linked to change request numbers, with separate approval |

## Notes for the persistence layer (Phase 2)

- Tables with triggers need `ToTable(t => t.HasTrigger(...))` in EF Core so EF does not use `OUTPUT` clauses that SQL Server rejects on such tables.
- The SQL scripts are the source of truth for the schema. EF Core migrations start from a baseline migration that matches them, and later schema changes ship as both a migration and an idempotent script.
- The application sets `CreatedBy`/`ModifiedBy` to the signed-in username. Triggers use these values as `PerformedBy` in the asset history.
