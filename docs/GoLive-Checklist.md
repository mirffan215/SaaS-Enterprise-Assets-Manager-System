# AMGH IT Inventory: go-live checklist

Tick each item, and record who checked it and when. Items marked **(ISMS)** produce evidence for ISO 27001; keep the records with the ISMS documentation.

## 1. Server and network

- [ ] Windows Server patched; SQL Server 2022 with its latest cumulative update.
- [ ] The site uses a certificate from the **company's certificate authority** (not the self-signed test certificate): `Deploy-WebApp.ps1 -CertificateThumbprint <thumbprint>`.
- [ ] Reachable only from the internal network or VPN, not from the internet. **(ISMS A.8.20)**
- [ ] **Firewall:** port 443 inbound from user networks. Outbound: port 25 or 587 to the mail service, and 443 to `login.microsoftonline.com` (only for OAuth e-mail).
- [ ] **IIS:** the application pool is set to *AlwaysRunning* with idle time-out 0, and the site has *Preload Enabled* (done by `Deploy-WebApp.ps1`; if it warned, install *Application Initialization*).
- [ ] **SQL Server:** maximum server memory is set to leave at least 4 GB for Windows, and the database files are on a disk with at least 50% free space.

## 2. Backups and recovery (ISMS A.8.13)

- [ ] **Database:** full backup every night and transaction-log backups every 15 minutes (recovery model FULL), kept 35 days, with a copy off the server.
- [ ] **Files:** daily backup of `C:\AMGH\Documents` (attachments), and of **`C:\AMGH\Keys`**. Without the keys folder, all users are signed out and **stored e-mail secrets can no longer be read** after a server rebuild.
- [ ] **Passwords in the vault:** the **database master key password** (licence keys are encrypted with it; it's needed to restore on another server) and the first administrator's password.
- [ ] **Restore test:** restore last night's backup to a test database and open it with a test site, e.g. `Invoke-LoadTest.ps1 -SkipCopy` on a restored copy named `…LoadTest`. Record the date and the time it took. **(ISMS evidence)**

## 3. Accounts and access (ISMS A.5.15-5.18, A.8.2)

- [ ] **Administrator account:** the initial administrator's password has been changed, and the account belongs to a named person.
- [ ] **Platform administrators:** at most two named people.
- [ ] **Least privilege:** every user has the smallest role that lets them work, and role memberships are checked with each company's owner.
- [ ] **Approval stages:** configured where the policy asks for separation of duties (e.g. disposals: IT Asset Manager, then System Administrator).
- [ ] **Leavers:** a process to deactivate their membership the same day.
- [ ] **Known gap: multi-factor authentication** is not delivered yet (planned R2 item). Until then, the site is internal-only and protected by the password policy (12+ characters, history, 90-day expiry), lockout after 5 failures, and sign-in throttling. Record this as an **accepted risk** in the risk register with a target date. **(ISMS)**

## 4. E-mail and notifications

- [ ] For each company: **E-mail and notifications** set up, *Test connection* passes, and a test message is received.
- [ ] The **application address** is set, and the links in e-mails open the right page.
- [ ] Notification kinds, days and recipient roles agreed with each company. Templates reviewed (wording, Arabic where needed).
- [ ] **Outgoing mail** shows no failed messages after the first day.

## 5. Data

- [ ] Master data loaded and checked in every company: locations down to rooms, departments, employees with e-mail addresses, categories with number series, vendors and manufacturers.
- [ ] Imported assets reconciled with the source (number of assets per category and location). **(ISMS A.5.9 inventory evidence)**
- [ ] QR labels or asset tags printed and attached; a sample of 20 scanned with a phone and opening the right asset.
- [ ] Warranties and licences entered for critical assets.

## 6. Testing and sign-off

- [ ] User acceptance test per module (assets, labels, assignments, lifecycle, approvals, reports, notifications), signed by each company's owner.
- [ ] **Load test passed** (`Invoke-LoadTest.ps1`); results folder kept. **(ISMS evidence)**
- [ ] The security review in `Release2.6-Hardening.md` read and accepted by the Information Security Officer.

## 7. Monitoring (ISMS A.8.15, A.8.16)

- [ ] **Health check:** `https://<server>/health` is checked every minute by the monitoring tool; an alert if not `Healthy` for 3 minutes.
- [ ] **Log errors:** an alert on `[ERR]` lines in `C:\AMGH\Logs\amgh-itinventory-*.log` (90 days are kept), and the logs collected into the SIEM if there is one.
- [ ] **Disk space:** an alert below 15% free on the database, log and document disks.
- [ ] **Weekly checks:** *Outgoing mail* failures, *Verification exceptions*, the *Report history*, and sign-ins from unusual accounts (*Sign-in history*).

## 8. Go-live day and rollback

- [ ] **Before deploying:** take a full backup and keep the previous release folder (e.g. `C:\AMGH\Release2.5`).
- [ ] **Deploy** with the release guide's steps, then check `/health`, sign in, open an asset, and print one label.
- [ ] **Rollback (within the same day):** stop the pool, restore the backup taken before deploying, then run `Deploy-WebApp.ps1` from the previous release folder.
- [ ] **Hypercare for the first 2 weeks:** daily check of logs, outgoing mail and user questions; a named contact for problems.
