# Release 2.5: notifications and e-mail

## Deploy (about 10 minutes)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.5.zip` to `C:\AMGH\Release2.5\`, run `Get-ChildItem C:\AMGH\Release2.5 -Recurse | Unblock-File`, and copy the `Migrations` folder from `C:\AMGH\Release2.4\...\AMGH.ITInventory.Persistence\` into the same place in 2.5.
2. **Build:** `cd C:\AMGH\Release2.5\AMGH.ITInventory; dotnet build AMGH.ITInventory.sln`. If it reports an error, stop here and send me the output.
3. **Stop the application:** `Import-Module WebAdministration; Stop-WebAppPool -Name AMGHITInventory`
4. **Update the database.** Script 14 adds the e-mail settings, templates, rules and preferences tables, all company-isolated:
   ```powershell
   cd deployment\PowerShell
   .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" -AdminEmail "it.admin@amgh.local" -TrustServerCertificate -SkipSeed
   ```
5. **Record the change for EF Core:** `.\New-BaselineMigration.ps1 -Name AddNotifications`
6. **Deploy and start the application:** `.\Deploy-WebApp.ps1`

**Background sender:** `Deploy-WebApp.ps1` now keeps the application pool always running (no idle shut-down) and preloads the site, so reminders and queued e-mails go out even when nobody is using the application. If it warns that preload could not be enabled, install the IIS feature *Application Initialization* (Server Manager → Web Server → Application Development) and run `Deploy-WebApp.ps1` again.

## Set up e-mail (per company)

**Administration → E-mail and notifications → E-mail sending.** Choose the method, save, then use **Test connection** and **Send test**. Sending stays **off** until you switch it on; until then notifications still appear in the application, and e-mails wait in the queue.

| Method | Server / port | What to prepare in Microsoft 365 |
| --- | --- | --- |
| **Exchange Online connector relay** (recommended) | `<your-domain>.mail.protection.outlook.com` / 25 / STARTTLS | Exchange admin center → Mail flow → Connectors → *Add a connector* from **Your organization's email server** to **Office 365**, identified by the **public IP address** of the application server. Add that IP to your SPF record. The sender address must use one of your accepted domains. No password is stored. |
| **SMTP AUTH (password)** | `smtp.office365.com` / 587 / STARTTLS | A licensed mailbox with SMTP AUTH enabled (`Set-CASMailbox <mailbox> -SmtpClientAuthenticationDisabled $false`). **Microsoft disables basic SMTP AUTH by default from end of December 2026**: prefer the connector or OAuth. |
| **SMTP AUTH with OAuth 2.0** | `smtp.office365.com` / 587 / STARTTLS | ① Entra ID → App registrations → *New registration*; create a client secret. ② API permissions → *Office 365 Exchange Online* → Application permission **SMTP.SendAsApp** → *Grant admin consent*. ③ In Exchange Online PowerShell: `New-ServicePrincipal -AppId <application id> -ObjectId <enterprise application object id>`, then `Add-MailboxPermission -Identity it-assets@… -User <service principal id> -AccessRights FullAccess`. ④ Enable SMTP AUTH on that mailbox (see the row above). Enter the mailbox, tenant ID, application ID and client secret. The server needs outbound HTTPS to `login.microsoftonline.com`. |
| **High Volume Email** | `smtp-hve.office365.com` / 587 / STARTTLS | Create an HVE account in the Exchange admin center. It delivers to **internal recipients only**. |
| **Other SMTP server or relay** | your relay | On-premises Exchange or another relay. Sign-in is optional. If the relay uses a **private certificate**, paste its SHA-256 fingerprint under *Internal relay with a private certificate*: exactly that certificate is then accepted. |

**Security:**

- **Secrets:** the password or client secret is encrypted with ASP.NET Data Protection (keys protected by Windows DPAPI on the server). It is never shown again and is masked in the audit log.
- **Encryption:** TLS 1.2 or later is required. A password is never sent without encryption.
- **Certificates:** they are always validated. The only exception is a pinned internal relay certificate.

Also set the **application address** (e.g. `https://inventory.amgh.local`). Links in e-mails point there.

## What people receive

| Notification | When | Who (default) |
| --- | --- | --- |
| Warranty expiry | 90, 60, 30, 7 days before, and on the day | IT Asset Managers |
| Software licence expiry | 60, 30, 7 days before, on the day, and at the licence's own reminder period | IT Asset Managers and the licence owner |
| Maintenance due | 1 day before and on the day; **overdue jobs every week** | IT Technicians and IT Asset Managers |
| Physical verification due | Weekly summary of assets due within 30 days and overdue | IT Asset Managers |
| Asset assigned / returned | Immediately | The person (by e-mail, even without an account) and IT Asset Managers |
| Approval required | When a movement or disposal is requested | Everyone who may approve it (never the requester) |
| Request decided | When it is approved or rejected | The requester |

Under **Notifications**, a company can switch each kind on or off, choose in-app and/or e-mail, change the days, and choose the recipient roles. **Check reminders now** runs the daily check immediately. Reminders are checked every day from 06:00 (server time). A reminder is never sent twice, even if the check runs again.

## Templates

**Templates** lets you change the subject and message of every notification:

- **Placeholders:** values such as `{{AssetNumber}}` and `{{EndDate}}` are filled in automatically, and are made safe automatically.
- **Layout:** the company header (name and logo), the *Open in IT Inventory* button and the footer are added automatically.
- **Preview:** shows the saved version with sample values. **Send test** e-mails it to you. **Reset** returns to the built-in wording.
- **Safety:** scripts, frames, forms and event handlers are refused.

## Outgoing mail

**Outgoing mail** lists waiting, sent, failed and cancelled messages, with the error for each failure:

- **Temporary failures** (server busy, network) are retried after 1, 5, 15, 60 and 240 minutes.
- **Permanent failures** (e.g. unknown recipient) are not retried.
- **Send again** re-queues a failed message, and **Cancel** stops a waiting one.

## For every user

- **The bell** (top right) shows unread notifications; click one to open the asset or page it is about.
- **Notifications** in the menu lists everything. There, each person can untick the kinds they don't want **by e-mail**; in-app notifications always remain.
