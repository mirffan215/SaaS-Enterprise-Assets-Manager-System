# Publishing EAMS with only web access to Plesk

This is for hosting where you can only use the Plesk web panel (no remote desktop, no SQL Server tools). You get **one database** and EAMS builds its own tables in it on the first visit. Everything below is done in the browser.

## What works in this mode, and what does not

- **Works:** the whole asset system, with one workspace that can hold **several companies** (the Platform Super Administrator adds companies inside the program; they are kept apart by row-level security), mobile app, scanner, RFID labels, maintenance costs, two-step verification, single sign-on, IP allow-list, document uploads, Company data export (CSV zip), all reports.
- **Not available** (they need a server where the program may create databases and write backups): one separate database per company, the Super Admin page with demo requests and company keys, per-company backup/restore/schedule pages, off-site copy, platform metrics. If you later get a VPS or dedicated server, the move scripts (`docs/Move-to-a-New-Server.md`) bring the data across.
- **Backups here:** Plesk → Websites & Domains → *Backup Manager* (schedule it) and Databases → *Export Dump* of the database. Download a copy regularly.

## What you need from the hosting

1. Windows hosting with **ASP.NET Core 8** (the ".NET 8 Hosting Bundle" / ASP.NET Core Module V2). If you are not sure, ask the provider: "Is the .NET 8 Hosting Bundle installed and can I run an ASP.NET Core 8 site in-process on IIS?"
2. A **Microsoft SQL Server** database with a user that **owns** that database (Plesk creates it that way).
3. The domain `EAMS.acmc.tech` with a Let's Encrypt certificate (Plesk can issue it).

## Steps

### 1. The website
1. *Websites & Domains → Add Domain* (or Add Subdomain): `EAMS.acmc.tech`, hosting type *Website*.
2. *Hosting & DNS → SSL/TLS Certificates → Let's Encrypt*, then tick *Permanent SEO-safe 301 redirect from HTTP to HTTPS*.
3. *Hosting & DNS → Hosting Settings*: if there is an ASP.NET setting, set it to *None / No managed code* (this is an ASP.NET Core site, not classic ASP.NET). Leave the PHP/other options off.

### 2. The database
*Databases → Add Database*: type **Microsoft SQL Server**, name e.g. `eams`, create a database user with a long password. Plesk then shows the database server address; write down **server, database, user, password**. (Server is often `localhost` or `localhost,1433` or a name like `sqlsrv01`; use exactly what Plesk shows. If you cannot connect from the site, use the *connection info* the provider gives for ASP.NET.)

### 3. Upload the program
1. Download `EAMS-plesk-package.zip` (given with this guide).
2. *File Manager* → open `httpdocs` → delete the default files (`index.html`, `iisstart.htm` ...).
3. *Upload* the zip → click it → **Extract**. Files such as `web.config`, `AMGH.ITInventory.Web.dll` and the folders `wwwroot`, `SQLScripts` must now sit directly in `httpdocs`.

### 4. The settings file
1. In `httpdocs` rename `appsettings.Production.json.example` to **`appsettings.Production.json`** (File Manager → right-click → Rename) and open it with *Edit*.
2. Fill in:
   - `AMGHDatabase`: `Server=<server>;Database=<database>;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True`
   - `Setup:Key`: make up a long random text (for example 40 letters and digits). It protects the first-time setup page.
3. Leave the other lines as they are: the documents, keys and logs go to `App_Data` inside the site folder. They are not reachable from the internet.

### 5. First start
1. Open `https://EAMS.acmc.tech`. It leads to **Set up EAMS**. If you see a database error there, correct the connection line in the settings file and reload.
2. Enter the setup key, a **company code** (letters/digits, e.g. `ACMC`), the company name, the administrator's e-mail and a strong password. Press *Set up now* and wait a minute or two.
3. **Write down the "database encryption password"** shown at the end (shown once).
4. In File Manager open `appsettings.Production.json` again and **delete the whole `Setup` section**, then save. The setup page can then never come back.
5. Sign in as `admin` (or with the e-mail) at `https://EAMS.acmc.tech`. More companies: Administration → Companies (Platform Super Administrator).

### 6. Updating later
Upload and **Extract** a newer package over the old one (overwrite). **Keep `appsettings.Production.json` and the `App_Data` folder** (the package does not contain them, so extracting does not remove them). Database changes are applied by the program the first time it starts.

## If something goes wrong

| What you see | Meaning / fix |
|---|---|
| HTTP Error 500.19 or 502.5 | The Hosting Bundle is missing or in-process hosting is off: ask the provider (see "What you need"). |
| Blank "Service unavailable" | The settings file has a typo (JSON). Check commas and quotes; or the database login is wrong. |
| "The database cannot be reached" on the setup page | Server name, user or password in `AMGHDatabase` is wrong. |
| "The database could not be set up ... rights" | The database user must own the database. Create the database user again in Plesk with *full* rights, or ask the provider. If tables were half-made, empty the database (or drop and re-add it) and repeat the setup. |
| Access denied to `App_Data` | File Manager → `App_Data` → *Permissions* → give the site's IIS user (the one Plesk lists, e.g. `IIS AppPool\EAMS.acmc.tech`) *Modify*. |
| Needs a look at the log | File Manager → `App_Data/Logs` → newest file. |

## Moving data you already have

If you only have one company in the old system, its database backup (`ITInv_Tenant_<code>_….bak`) can be loaded into the Plesk database with *Databases → Import Dump* (it accepts a SQL Server backup). The program then recognises the tables as already set up and only upgrades them. Companies of several customers cannot be merged into one database this way: ask first.
