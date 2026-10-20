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

## Signing in

By default people **choose their company first**: they type the company code once (the browser remembers it, so later visits and sign-outs go straight to
that company's sign-in), or use their company's own address (a sub-domain, or the sign-in link the operator page lists).

Optional, off by default: set `Tenancy:GlobalSignIn` to `true` and the first page asks only for **user name or e-mail and password**; the site looks for the person
in the company databases and takes them to their company (if the same details work in two companies they are asked for the code). It searches every company,
so it is switched off automatically beyond 100 companies.

## Registering companies

* **Operator:** `/Tenant/Admin`, protected by the operator key (below). Always available while a key is set.
* **Self-registration (optional, off by default):** set `Tenancy:AllowSelfRegistration` to `true` and a *Register your company* button appears on the
  sign-in page (`/Register`: company code, name, administrator e-mail and password; three registrations per address and hour).
  Turn it on only for a service that should take sign-ups.

## Creating companies as the operator (customers)

Open `https://<host>[:port]/Tenant/Admin` and enter the **operator key** (printed at the end of the install, also in `appsettings.Production.json` as
`Tenancy:OperatorKey`), then a company code (2-10 letters or digits), the company name and the first administrator's e-mail and password.
The company gets its own database (this takes a minute or two). The administrator signs in as `admin` at the company-code page. *Show companies*
lists what exists. The page is hidden while no operator key is set, is rate-limited, and customers never see it.

For an installation made before this existed, add a key once (PowerShell as administrator):

```powershell
$f = "C:\inetpub\EAMSCloud\appsettings.Production.json"
$key = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
$j = Get-Content $f -Raw | ConvertFrom-Json
$j.Tenancy | Add-Member -NotePropertyName OperatorKey -NotePropertyValue $key -Force
$j | ConvertTo-Json -Depth 8 | Set-Content $f -Encoding UTF8
Restart-WebAppPool EAMSCloud
$key   # write it down
```

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
