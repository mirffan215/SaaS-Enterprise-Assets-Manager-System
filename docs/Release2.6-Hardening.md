# Release 2.6: hardening (security review and load test)

## Deploy (about 10 minutes)

This release changes one stored procedure (physical verification), so the database step runs. There is no new table and no EF Core migration.

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.6.zip` to `C:\AMGH\Release2.6\`, run `Get-ChildItem C:\AMGH\Release2.6 -Recurse | Unblock-File`, and copy `Migrations` from the 2.5 folder.
2. **Build:** `dotnet build AMGH.ITInventory.sln`
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database:** `.\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed`
5. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

**Check:** `https://<server>/health` answers `Healthy`.

## Security review: what was checked

| Area | How it was checked | Result |
| --- | --- | --- |
| Sign-in required | Live requests without a session to 10 protected pages and actions | **1 finding, fixed** (below) |
| CSRF | Form submissions without the anti-forgery token | Rejected (400) everywhere, including sign-in |
| Open redirect | Sign-in with `returnUrl` set to an external site and to tricks (`//evil`, `/\evil`, `https:evil`) | Always stays on the site |
| Cross-site scripting | Script payloads in search, asset list, reports, verification and search suggestions | Always encoded; never executed |
| Path traversal, IDs, methods | `..` in logo, branding and document URLs; unknown and non-numeric IDs; DELETE | Nothing leaked; 404 or 400 |
| Errors | Malformed dates, negative page sizes, SQL-looking sort values | Friendly page, no technical details |
| Headers | CSP, HSTS (1 year), nosniff, frame denial, referrer policy, permissions policy, no-store | All present; no server banner |
| Cookies | Session and anti-forgery cookies | `__Host-` prefix, Secure, HttpOnly, SameSite=Strict |
| Company isolation | Covered by the 79 security tests (Release 2.0+) | Unchanged |
| SQL injection | All database access reviewed | Parameters only; the one dynamic SQL statement uses a fixed list with `QUOTENAME` |
| Uploads and Excel import | Code review | 25 MB limit, file signature checked; Excel entry-count and size limits (zip bombs), no DTDs |
| Sign-in throttling | Code review | 10 attempts per minute per client address, plus account lockout |
| Secrets | Configuration, logs, audit trail | No passwords in configuration; logs show names only; secrets masked as `***` in the audit trail |
| Deployment scripts | PowerShell parser on all 8 scripts; `Deploy-Database.ps1` run against a stand-in SQL client | No syntax errors; guard, auto-detect, name validation and master-key policy behave correctly |

### Findings and fixes

1. **Account pages could be opened without signing in (fixed).** The account controller was marked public as a whole. In ASP.NET Core that overrides `[Authorize]` on the individual actions, so *Change password* and the session keep-alive could be reached anonymously. Nothing could be changed (no user, so no password), but it broke defence in depth. Now only sign-in, the workspace link and *Access denied* are public; everything else requires a session.
2. **The health check reported "Healthy" even with the database down (fixed).** `/health` now tests SQL Server within 5 seconds and answers `Healthy` (200) or `Unhealthy` (503), without revealing any details.
3. **Scanning an asset twice in the same verification cycle failed (fixed).** The database allows one result per asset per cycle, but the procedure always inserted a new one. Scanning again now replaces that cycle's result; every scan stays in the asset's history.
4. **Verification cycle names longer than 20 characters were silently cut short (fixed).** The screen now allows at most 20, matching the database.
5. **The Approval Center count read the whole approval history on every page (fixed).** It now reads only the pending requests, so the cost stays constant as the history grows.
6. **Unattended deployments.** `Deploy-Database.ps1` accepts an optional `-MasterKeyPassword` (a secure string, checked against the password policy). Without it, you are still asked.

## Load test with 100,000 assets

The test runs on a **copy** of the production database with a **separate test site**, so production data is never changed. Run it outside business hours if the copy is on the production SQL Server, because they share CPU and disks.

```powershell
cd C:\AMGH\Release2.6\AMGH.ITInventory\deployment\PowerShell
.\Invoke-LoadTest.ps1 -TestUser it.admin -TrustServerCertificate
```

You are asked for two passwords:

- **The test user's password:** the copy has the same users and passwords as production.
- **The database master key password:** enter the production one. The copy already has its key, so nothing changes.

The script then:

1. **Copies** production with a COPY_ONLY backup and restore, as `AMGH_ITInventory_LoadTest`. Your normal backup chain is not affected.
2. **Prepares the copy** and adds **100,000 assets** with history, about 600,000 rows in total, in 2-6 minutes. Everything generated is marked `LT`. The generator refuses to run on any database whose name doesn't contain `LoadTest`.
3. **Deploys a test site** on `https://localhost:8443` that uses the copy, with **e-mail and reminders switched off**. The copy holds real e-mail settings and addresses, and nobody must be e-mailed about test data. The script stops if the test site's settings don't point to the copy with notifications off.
4. **Runs the test:** **25 users for 10 minutes** with one-second think time, which is about four times a busy working day for this kind of system.
5. **Reports the results.** They are saved in `C:\AMGH\LoadTest\Results\<date-time>\`:
   - `loadtest.txt` and `summary.csv`: response times per page (p50/p90/p95/p99), errors, PASS/FAIL;
   - `sql-performance.txt`: SQL Server's slowest statements, missing-index suggestions and data volumes.

Run it again on the same copy with `-SkipCopy -SkipSite`, and change the load with `-Users` and `-Minutes`. Remove the test site, its application pool and the copy with `.\Invoke-LoadTest.ps1 -Cleanup`.

**Pass criteria** (95% of requests at or below this time; at most 1% errors overall):

| Page | p95 limit | Page | p95 limit |
| --- | --- | --- | --- |
| Asset list (paging, sorting) | 1.5 s | Executive dashboard | 3 s |
| Asset list with search | 1.5 s | Report: asset register | 5 s |
| Asset details | 1.5 s | Report: compliance | 8 s |
| Search suggestions | 0.8 s | Physical verification | 2 s |
| Global search | 1.5 s | Approvals, maintenance | 1.5 s |
| Notifications | 1 s | Health check | 1 s |

### If the load test fails

Send me the whole results folder. Most problems come down to one of three causes, each with a different fix:

- **One page fails while the others pass:** usually one query. `sql-performance.txt` names the statement, and often the index SQL Server would like. I'll review it and supply a script; please don't create suggested indexes blindly.
- **Everything is slow:** check the server itself (CPU, memory, SQL Server's maximum memory setting, disk latency) and whether something else ran at the same time.
- **Errors:** `loadtest.txt` shows the first error for each page, and `C:\AMGH\LoadTest\Logs` has the details.

## Before go-live

See **GoLive-Checklist.md**.
