# New installation on a Windows server (side by side)

For a first-time installation of the SaaS edition on a Windows server with IIS and SQL Server 2022. It creates a **new, separate**
installation (own IIS site, application pool, folders, databases, SQL logins and cookie names) and never changes one that is already
running. If a name or folder it would use already exists it stops before changing anything.

## Before you start

| Need | Notes |
|---|---|
| Windows Server 2019+ with IIS | `Install-WindowsFeature Web-Server, Web-Mgmt-Console` (add *Application Initialization* for the background e-mail sender) |
| .NET 8 Hosting Bundle | then `iisreset` |
| .NET 8 SDK | installed with Visual Studio 2022 |
| SQL Server 2022 | **mixed mode** (SQL logins allowed); `sqlcmd` installed; you run the installer as a sysadmin |
| The code | `git clone` + `git checkout claude/multi-tenant-saas` (or the merged `main`) |
| A certificate | optional: without one a self-signed certificate is created (testing only) |

## Install

Open **Windows PowerShell as administrator**:

```powershell
cd <repo>\deployment\PowerShell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
.\Install-SaaS.ps1
```

It asks, in this order:

1. **Host name** people will type (default: the server name).
2. **Port**: `[1] Default HTTPS port (443)` or `[2] Custom port` (it then asks for the number). It checks that nothing else uses the port
   and that no other IIS site uses it for the same host name; if it does, it offers a custom port.
3. **How people reach their company**: a *company-code page* (one address, people type their code), or *one sub-domain per company*
   (needs wildcard DNS and a wildcard certificate).
4. **Demo company** with sample data (and its administrator password), and whether to show the demo login on the sign-in page.
5. A summary, then **Continue?**

Everything can also be given as parameters (for example `-PortMode Custom -Port 8443 -HostName eams.contoso.local -CreateDemo`);
see `Get-Help .\Install-SaaS.ps1 -Full`. Run it on a test server first.

## What it creates

* IIS site `EAMSCloud` and pool `EAMSCloud` (HTTPS only), files in `C:\inetpub\EAMSCloud`.
* Data in `C:\EAMSCloud\Logs`, `Keys`, `Documents` (nothing shared with another installation).
* SQL Server: catalog database `ITInv_Catalog`; login `eams_provisioner` (create databases only, random password); the pool identity
  with read/write on the catalog only. Customer databases `ITInv_Tenant_<code>` are created by the application itself; on each of them the
  pool identity gets the least-privilege rights of script 08 (read and write, no delete, no schema changes).
* `appsettings.Production.json` with generated secrets, readable only by Administrators and the pool. The master-key password is printed once:
  **write it down** (it is needed to restore a customer database backup), and back up the `Keys` folder.

## After it finishes

* Open `https://<host>[:port]`. With the company-code page you land on *Find your company*; with the demo enabled use *Explore Demo Environment*
  (the demo company is created about a minute after the first start; company code `demo`, user `admin`).
* Health: `https://<host>[:port]/health`. Logs: `C:\EAMSCloud\Logs`.

## Updating later

`.\Install-SaaS.ps1 -Update -HostName <same host> -Port <same port> -PortMode Custom|Default` publishes a new version into the same site and keeps the
settings, databases and certificate. Existing customer databases need new SQL scripts applied by hand when a release adds one.

## Removing a test installation

Remove the IIS site and pool, delete `C:\inetpub\EAMSCloud` and `C:\EAMSCloud`, drop `ITInv_Catalog`, the `ITInv_Tenant_*` databases and the
`eams_provisioner` login. Nothing of the other installation is touched.

## Not tested yet

The installer was syntax-checked and its helper functions tested, but it has not been run on a Windows server with IIS and SQL Server.
Expect to fix small things on the first run and tell me the error text.
