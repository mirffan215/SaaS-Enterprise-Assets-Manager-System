# Moving EAMS to another server (for example to go live on https://EAMS.acmc.tech)

Two scripts in `deployment\PowerShell` move everything: the databases (catalog and every company), the keys, the documents and the operator settings.

## 1. On the OLD server

```powershell
cd C:\SaaS-Enterprise-Assets-Manager-System\deployment\PowerShell
.\Export-ForMove.ps1 -OutputFolder "D:\EAMS-Move"
```
This creates `D:\EAMS-Move\EAMS-Move-<date>`: `databases\`, `Keys\`, `Documents\`, `settings\`, `manifest.json`, `Import-FromMove.ps1`, `README.txt`. It holds secrets: only Administrators and SYSTEM can open it. Copy the folder to the new server over an encrypted channel (not e-mail, not a public share).

## 2. Prepare the NEW server

- Windows Server with IIS, SQL Server (mixed mode), the .NET 8 SDK and Hosting Bundle, `git`, and the repository cloned (same version as the old site).
- DNS: `EAMS.acmc.tech` points at the new server.
- A certificate for `EAMS.acmc.tech` from a public CA (for example Let's Encrypt / win-acme) in the computer's certificate store (Local Computer, Personal). Phones and browsers only trust that kind; it also makes the mobile app installable. Get its thumbprint: `Get-ChildItem Cert:\LocalMachine\My | ft Subject, Thumbprint, NotAfter`.
- Nothing else on port 443 for that host name.

## 3. Install an empty EAMS there (default ports)

```powershell
cd <repository>\deployment\PowerShell
.\Install-SaaS.ps1 -HostName EAMS.acmc.tech -PortMode Default -TenantMode CompanyCode -NoDemo -CertificateThumbprint <thumbprint> -OpenFirewall
```
`-PortMode Default` means port 443 (https). People type only `https://EAMS.acmc.tech` and then their company code. (Use `-TenantMode Subdomain -BaseDomain EAMS.acmc.tech` instead only if you have a wildcard certificate and DNS for `*.EAMS.acmc.tech`: each company then gets `code.EAMS.acmc.tech`.)

## 4. Restore the data

```powershell
cd D:\EAMS-Move\EAMS-Move-<date>        # the copied folder
.\Import-FromMove.ps1
```
It verifies the backups, takes the site offline, restores the catalog and every company, repairs database access for this server, re-opens each company's encryption key with the old master-key password, restores `Keys` and `Documents`, copies the operator key, master-key password and mail settings into the new settings, and starts the site. The first start upgrades every company database to the installed version: wait a few minutes.

## 5. Check, then switch

- Open `https://EAMS.acmc.tech`, enter a company code, sign in, open an asset and a document.
- Super Admin: `https://EAMS.acmc.tech/Tenant/Admin` with the **same operator key as before** (run `Set-OperatorAccess.ps1 -NewOperatorKey` if the old one must not live on).
- Set the address for e-mails: `.\Set-OperatorAccess.ps1 -PublicBaseUrl "https://EAMS.acmc.tech"`, and the off-site backup target (`-OffsiteFolder` / `-OffsiteBlobUrl`) if you use one.
- Schedule backups on the new server: `.\Backup-SaaS.ps1 -RegisterTask -TaskTime 02:00`.
- Keep the old server untouched (read-only) for a few days; the move package is a copy and can be imported again.

## Notes

- People who were signed in must sign in again (the cookies belong to the old address).
- The QR labels already printed contain the **old address**. They keep working while the old server answers; for the new address, set `Labels:BaseUrl` before printing new ones. Re-print old ones only if the old address goes away.
- Single sign-on: register the new redirect address `https://EAMS.acmc.tech/Account/SsoCallback` with your identity provider.
- If the new SQL Server is reached under a different name than before (not `localhost`), the stored company connections need recreating: do not import before asking.
