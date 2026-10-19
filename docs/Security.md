# Security and the Web Application (Phase 3)

## Sign-in and sessions

| Control | Behaviour | Setting (appsettings.json "Security") |
|---|---|---|
| Local accounts | Username and password stored in `tblUser`. No Active Directory, Entra ID or Graph. | |
| Password storage | PBKDF2-HMAC-SHA512, 100,000 iterations, random salt. Old hashes are upgraded automatically at sign-in. | |
| Password policy | At least 12 characters; upper, lower, digit and symbol; at least 6 different characters; no username/name; no common words or keyboard patterns. | `PasswordMinimumLength` |
| Password history | The last 5 passwords cannot be reused. | `PasswordHistoryCount` |
| Password expiry | A change is forced after 90 days (0 disables). | `PasswordMaximumAgeDays` |
| First sign-in | Temporary passwords must be changed before anything else can be opened. | |
| Lockout | Locks for 15 minutes after 5 wrong passwords; administrators can unlock early. | `MaxFailedAccessAttempts`, `LockoutMinutes` |
| Rate limit | At most 10 sign-in attempts per minute from one IP address. | |
| Error messages | "Invalid username or password" for unknown users and wrong passwords alike. A disabled account is only revealed after the correct password. | |
| Idle timeout | Signed out after 15 minutes without activity; a dialog warns one minute before. | `SessionIdleTimeoutMinutes` |
| Absolute timeout | Signed out after 8 hours regardless of activity. | `SessionAbsoluteTimeoutHours` |
| Session revocation | Every 5 minutes the session is re-checked. Disabling a user, changing their roles or resetting their password ends their open sessions. | `SecurityStampValidationMinutes` |

## Authorisation

Every page requires sign-in unless explicitly public (sign-in page, error pages, `/health`). Pages are protected by permission codes, for example `[HasPermission(Permissions.Administration.UsersManage)]`. Menus show only what the user's roles allow. Permissions travel in the encrypted session cookie and are refreshed whenever the user's roles change (see Session revocation above).

## Web hardening

- **HTTPS only.** IIS has no HTTP binding, HSTS is enabled for one year, and cookies are `Secure`, `HttpOnly`, `SameSite=Strict` and use the `__Host-` prefix.
- **Anti-forgery tokens** are required on every form post.
- **Content-Security-Policy** allows scripts, styles and fonts only from this server, so there is no inline script and no CDN. Bootstrap, jQuery and the icons are bundled in `wwwroot/lib`.
- **Other headers:** `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and `Permissions-Policy`. The `Server` and `X-Powered-By` headers are removed.
- **Caching:** authenticated pages are sent with `Cache-Control: no-store`.
- **Cookie encryption keys** are stored in `C:\AMGH\Keys` and protected with Windows DPAPI, so sessions survive app-pool recycles. Include this folder in server backups.

## User administration

**Administration > Users and roles** (permission `Users.Manage`):

- **Add a user** with one or more roles. A 16-character temporary password is shown once, and the person must change it at first sign-in.
- **Edit** the name, e-mail, employee link and roles, or disable the account. Administrators cannot disable their own account, and the database always keeps at least one active System Administrator.
- **Unlock** an account or **reset** its password.

**Administration > Sign-in history** (permission `AuditLogs.View`) lists sign-ins, failures, lockouts, password changes and resets, and session ends, with filters.

## Logs

| Where | What |
|---|---|
| `dbo.tblLoginAudit` | Every authentication event (append-only) |
| `dbo.tblAuditLog` | Every data change, including user and role administration (append-only) |
| `C:\AMGH\Logs\amgh-itinventory-<date>.log` | Application log, kept for 90 days. Passwords are never logged. |

## ISO/IEC 27001:2022 mapping (additions in Phase 3)

| Control | Implementation |
|---|---|
| A.5.15 Access control / A.8.3 Information access restriction | Deny-by-default fallback policy; permission-based pages and menus |
| A.5.16 Identity management | Unique usernames; accounts linked to employees; disable instead of delete |
| A.5.17 Authentication information | Hashing, policy, history, expiry, temporary passwords given out in person |
| A.5.18 Access rights | Role assignment by administrators; changes audited and take effect within 5 minutes |
| A.8.5 Secure authentication | Lockout, rate limiting, generic errors, session timeouts. **MFA not yet implemented**; see Roadmap |
| A.8.15 / A.8.16 Logging and monitoring | Login audit, data audit trail, application log, sign-in history screen |
| A.8.24 Use of cryptography | TLS, encrypted cookies (DPAPI-protected keys), salted password hashes |
| A.8.26 Application security requirements | CSP, anti-forgery tokens, secure cookies, security headers |
