<#
.SYNOPSIS
    Puts everything needed to move this EAMS installation to another server into ONE folder.

.DESCRIPTION
    Creates <OutputFolder>\EAMS-Move-<date> containing
      databases\  a fresh, verified, compressed backup of the catalog and of every company database
      Keys\       the sign-in and secret-protection keys (the stored company connections cannot be read without them)
      Documents\  every uploaded document
      settings\appsettings.Production.json   the site's settings and secrets (operator key, master-key password, mail settings)
      manifest.json  what is inside, with SHA-256 of every database backup
      Import-FromMove.ps1  the script to run on the new server
      README.txt   the steps
    The folder holds secrets: it is made readable by Administrators and SYSTEM only. Copy it over an encrypted channel and delete it afterwards.

.EXAMPLE
    .\Export-ForMove.ps1
    .\Export-ForMove.ps1 -OutputFolder "D:\EAMS-Move"
.NOTES
    Run in an elevated PowerShell as an account that may back up the databases (SQL Server sysadmin) on the OLD server.
    The site may keep running; for a move without losing data, stop people from working (or put the site offline) before running it.
#>
[CmdletBinding()]
param(
    [string] $OutputFolder = "C:\EAMS-Move",
    [string] $SqlServer = "localhost",
    [string] $CatalogDatabase = "ITInv_Catalog",
    [string] $DataRoot = "C:\EAMSCloud",
    [string] $SitePath = "C:\inetpub\EAMSCloud",
    [string] $AppPoolName = "EAMSCloud"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Sql([string] $Query) {
    $out = & sqlcmd -S $SqlServer -E -b -C -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $($out -join ' ')" }
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script from an elevated PowerShell (Run as administrator)." }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found. Install the SQL Server command-line utilities." }
$settings = Join-Path $SitePath "appsettings.Production.json"
if (-not (Test-Path $settings)) { throw "Settings file not found: $settings (is this the EAMS server? use -SitePath)." }

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$root = Join-Path $OutputFolder "EAMS-Move-$stamp"
$dbDir = Join-Path $root "databases"
New-Item -ItemType Directory -Path $dbDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $root "settings") -Force | Out-Null

# ---------------------------------------------------------------- databases
# SQL Server writes the backups itself, so they go first to the backup folder it may write to, then are copied here.
$work = Join-Path $DataRoot "Backups\move-$stamp"
[void](Invoke-Sql "EXEC master.sys.xp_create_subdir N'$($work.Replace("'", "''"))';")
$databases = Invoke-Sql "SELECT name FROM sys.databases WHERE state_desc = N'ONLINE' AND (name = N'$($CatalogDatabase.Replace("'", "''"))' OR name LIKE N'ITInv[_]Tenant[_]%') ORDER BY name;"
if ($databases.Count -eq 0) { throw "No catalog or company databases found on $SqlServer." }
$manifestDbs = @()
foreach ($db in $databases) {
    if ($db -notmatch '^[A-Za-z0-9_]+$') { Write-Warning "Skipping '$db' (unexpected characters in the name)."; continue }
    $name = "{0}_{1}.bak" -f $db, $stamp
    $file = Join-Path $work $name
    Write-Step "Backing up $db"
    [void](Invoke-Sql "BACKUP DATABASE [$db] TO DISK = N'$($file.Replace("'", "''"))' WITH COMPRESSION, CHECKSUM, INIT, STATS = 100;")
    [void](Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$($file.Replace("'", "''"))' WITH CHECKSUM;")
    Copy-Item -Path $file -Destination (Join-Path $dbDir $name) -Force
    $copy = Join-Path $dbDir $name
    $manifestDbs += [ordered]@{ Database = $db; File = $name; Bytes = (Get-Item $copy).Length; Sha256 = (Get-FileHash $copy -Algorithm SHA256).Hash }
    Write-Host "  OK  $name" -ForegroundColor Green
}
Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- keys, documents, settings
foreach ($pair in @(@("Keys", "Keys"), @("Documents", "Documents"))) {
    $src = Join-Path $DataRoot $pair[0]
    if (Test-Path $src) {
        Write-Step "Copying $($pair[0])"
        & robocopy $src (Join-Path $root $pair[1]) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying $($pair[0]) failed (robocopy exit code $LASTEXITCODE)." }
    }
}
Copy-Item -Path $settings -Destination (Join-Path $root "settings\appsettings.Production.json") -Force

# ---------------------------------------------------------------- manifest, import script, readme
$json = Get-Content $settings -Raw | ConvertFrom-Json
$tenancy = if ($json.PSObject.Properties["Tenancy"]) { $json.Tenancy } else { $null }
$sqlVersion = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(50));") | Select-Object -First 1
$manifest = [ordered]@{
    Created = (Get-Date).ToString("s")
    SourceComputer = $env:COMPUTERNAME
    SourceSqlServer = $SqlServer
    SourceSqlVersion = $sqlVersion
    CatalogDatabase = $CatalogDatabase
    AppPoolName = $AppPoolName
    Databases = $manifestDbs
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $root "manifest.json") -Encoding UTF8
Copy-Item -Path (Join-Path $PSScriptRoot "Import-FromMove.ps1") -Destination $root -Force

@"
EAMS move package - made $(Get-Date -Format "yyyy-MM-dd HH:mm") on $($env:COMPUTERNAME)

THIS FOLDER CONTAINS SECRETS (settings, keys, all company data). Copy it over an encrypted channel, keep it away from other people, delete it when the new site works.

On the NEW server (Windows, IIS, SQL Server, .NET 8 Hosting Bundle and SDK, the repository cloned):
 1. Point the DNS name of the new site at the new server and install the certificate for it in the computer's certificate store
    (Local Computer, Personal). Note its thumbprint.
 2. Install a fresh, EMPTY copy of EAMS (the same version as the old site):
        cd <repository>\deployment\PowerShell
        .\Install-SaaS.ps1 -HostName <new site name> -PortMode Default -TenantMode CompanyCode -NoDemo -CertificateThumbprint <thumbprint> -OpenFirewall
 3. Copy THIS folder to the new server and run, in an elevated PowerShell:
        .\Import-FromMove.ps1
    It stops the site, restores the catalog and every company database, repairs the database access, restores the keys and documents,
    carries the operator key, mail settings and master-key password over, and starts the site again.
 4. Open the new address, sign in to a company and check the data. Then run Set-OperatorAccess.ps1 -NewOperatorKey if the old operator key
    should not live on.

Contents: databases\ (backups with SHA-256 in manifest.json), Keys\, Documents\, settings\appsettings.Production.json, Import-FromMove.ps1.
"@ | Set-Content -Path (Join-Path $root "README.txt") -Encoding UTF8

# only Administrators and SYSTEM
& icacls $root /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" /T /Q | Out-Null

$size = [math]::Round(((Get-ChildItem $root -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host ""
Write-Host "Move package ready: $root  ($size MB, $($manifestDbs.Count) database(s))" -ForegroundColor Green
Write-Host "Copy the whole folder to the new server and follow README.txt." -ForegroundColor Yellow
