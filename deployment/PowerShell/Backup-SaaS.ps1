<#
.SYNOPSIS
    Backs up the SaaS installation: the catalog database, every customer database, the encryption keys, the uploaded documents and the site itself (programs and settings).

.DESCRIPTION
    * One compressed, checksummed full backup per database (ITInv_Catalog and every ITInv_Tenant_<code>), each verified afterwards.
    * The Keys folder (sign-in and secret-protection keys) as a dated copy and the Documents folder as a rolling mirror.
    * The site folder (the program files and appsettings.Production.json with the site's secrets) as a dated zip, readable only by Administrators and SYSTEM.
    * Old database backups are deleted after -KeepDays days (the newest backup of each database is always kept).
    A customer database can be restored on its own; the others are not touched.

    The database backups are written by SQL Server itself, so the folder must be writable by the SQL Server service. By default the
    instance's own backup folder is used (subfolder EAMSCloud). COPY THE BACKUP FOLDER OFF THIS SERVER regularly: a backup on the same
    disk does not protect against losing the server.

    Restoring a customer database on another server also needs the database master-key password that Install-SaaS.ps1 printed.

.EXAMPLE
    .\Backup-SaaS.ps1
    .\Backup-SaaS.ps1 -BackupRoot "D:\Backups\EAMS" -KeepDays 30
    .\Backup-SaaS.ps1 -RegisterTask -TaskTime 02:30      # nightly, asks for the account to run as
.NOTES
    Run as an account that may back up the databases (SQL Server sysadmin, or db_backupoperator on each database) and can read the data folder.
#>
[CmdletBinding()]
param(
    [string] $SqlServer = "localhost",
    [string] $CatalogDatabase = "ITInv_Catalog",
    [string] $DataRoot = "C:\EAMSCloud",
    [string] $SitePath = "C:\inetpub\EAMSCloud",
    [string] $BackupRoot,
    [int]    $KeepDays = 14,
    [switch] $SkipFiles,
    [switch] $SkipSite,
    [switch] $RegisterTask,
    [string] $TaskTime = "02:00"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Sql([string] $Query) {
    $out = & sqlcmd -S $SqlServer -E -b -C -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $($out -join ' ')" }
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found. Install the SQL Server command-line utilities." }

# ---------------------------------------------------------------- nightly task
if ($RegisterTask) {
    $cred = Get-Credential -Message "Account the nightly backup runs as (must be allowed to back up the databases)"
    $argument = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -SqlServer `"$SqlServer`" -CatalogDatabase `"$CatalogDatabase`" -DataRoot `"$DataRoot`" -SitePath `"$SitePath`" -KeepDays $KeepDays"
    if ($BackupRoot) { $argument += " -BackupRoot `"$BackupRoot`"" }
    $action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $argument
    $trigger = New-ScheduledTaskTrigger -Daily -At $TaskTime
    Register-ScheduledTask -TaskName "EAMS Cloud backup" -Action $action -Trigger $trigger -User $cred.UserName -Password $cred.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
    Write-Host "Scheduled: every day at $TaskTime as $($cred.UserName). Test it with: Start-ScheduledTask -TaskName 'EAMS Cloud backup'" -ForegroundColor Green
    return
}

# ---------------------------------------------------------------- where the backups go
if (-not $BackupRoot -and (Test-Path (Join-Path $DataRoot "Backups"))) { $BackupRoot = Join-Path $DataRoot "Backups" }   # the folder the operator page uses
if (-not $BackupRoot) {
    $default = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(400));") | Select-Object -First 1
    if (-not $default -or $default -eq "NULL") { throw "Give -BackupRoot: a folder the SQL Server service can write to." }
    $BackupRoot = Join-Path $default "EAMSCloud"
}
Write-Step "Backup folder: $BackupRoot"
[void](Invoke-Sql "EXEC master.sys.xp_create_subdir N'$($BackupRoot.Replace("'", "''"))';")

# ---------------------------------------------------------------- databases
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$databases = Invoke-Sql "SELECT name FROM sys.databases WHERE state_desc = N'ONLINE' AND (name = N'$($CatalogDatabase.Replace("'", "''"))' OR name LIKE N'ITInv[_]Tenant[_]%') ORDER BY name;"
if ($databases.Count -eq 0) { throw "No catalog or customer databases found on $SqlServer." }

$failed = @()
foreach ($db in $databases) {
    if ($db -notmatch '^[A-Za-z0-9_]+$') { Write-Warning "Skipping '$db' (unexpected characters in the name)."; continue }
    $file = Join-Path $BackupRoot ("{0}_{1}.bak" -f $db, $stamp)
    $escaped = $file.Replace("'", "''")
    Write-Step "Backing up $db"
    try {
        [void](Invoke-Sql "BACKUP DATABASE [$db] TO DISK = N'$escaped' WITH COMPRESSION, CHECKSUM, INIT, STATS = 100;")
        [void](Invoke-Sql "RESTORE VERIFYONLY FROM DISK = N'$escaped' WITH CHECKSUM;")
        Write-Host "  OK  $file" -ForegroundColor Green
    } catch {
        $failed += $db
        Write-Host "  FAILED  $db : $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------- keys and documents
if (-not $SkipFiles) {
    $filesRoot = Join-Path $BackupRoot "files"
    $keys = Join-Path $DataRoot "Keys"; $docs = Join-Path $DataRoot "Documents"
    if (Test-Path $keys) {
        Write-Step "Copying the Keys folder"
        & robocopy $keys (Join-Path $filesRoot "Keys_$stamp") /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { $failed += "Keys"; Write-Host "  FAILED copying Keys" -ForegroundColor Red }
    }
    if (Test-Path $docs) {
        Write-Step "Mirroring the Documents folder"
        & robocopy $docs (Join-Path $filesRoot "Documents") /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { $failed += "Documents"; Write-Host "  FAILED mirroring Documents" -ForegroundColor Red }
    }
}

# ---------------------------------------------------------------- the site itself
if (-not $SkipSite -and (Test-Path $SitePath)) {
    Write-Step "Zipping the site folder ($SitePath)"
    try {
        $filesRoot = Join-Path $BackupRoot "files"
        if (-not (Test-Path $filesRoot)) { New-Item -ItemType Directory -Path $filesRoot | Out-Null }
        $zip = Join-Path $filesRoot "Site_$stamp.zip"
        # the running site keeps its program files open: copy them with robocopy first (it can read them), then zip the copy
        $temp = Join-Path ([IO.Path]::GetTempPath()) "eams-site-$stamp"
        & robocopy $SitePath $temp /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD logs /XF app_offline.htm | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "copying the site folder failed (robocopy exit code $LASTEXITCODE)" }
        try { Compress-Archive -Path (Join-Path $temp "*") -DestinationPath $zip -CompressionLevel Optimal -Force }
        finally { Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue }
        # the settings file holds the site's secrets: only Administrators and SYSTEM may read the archive
        & icacls $zip /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" | Out-Null
        Write-Host "  OK  $zip" -ForegroundColor Green
    } catch {
        $failed += "Site"
        Write-Host "  FAILED zipping the site: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------- retention (only when everything worked)
if ($failed.Count -eq 0) {
    $limit = (Get-Date).AddDays(-$KeepDays)
    foreach ($db in $databases) {
        $mine = @(Get-ChildItem -Path $BackupRoot -Filter "${db}_*.bak" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        $mine | Select-Object -Skip 1 | Where-Object { $_.LastWriteTime -lt $limit } | Remove-Item -Force
    }
    $filesDir = Join-Path $BackupRoot "files"
    if (Test-Path $filesDir) {
        $keysCopies = @(Get-ChildItem -Path $filesDir -Directory -Filter "Keys_*" | Sort-Object Name -Descending)
        $keysCopies | Select-Object -Skip 1 | Where-Object { $_.LastWriteTime -lt $limit } | Remove-Item -Recurse -Force
        $siteZips = @(Get-ChildItem -Path $filesDir -File -Filter "Site_*.zip" | Sort-Object Name -Descending)
        $siteZips | Select-Object -Skip 1 | Where-Object { $_.LastWriteTime -lt $limit } | Remove-Item -Force
    }
}

Write-Host ""
if ($failed.Count -gt 0) { Write-Host "Finished WITH ERRORS: $($failed -join ', '). Old backups were kept." -ForegroundColor Red; exit 1 }
Write-Host "Backup finished: $($databases.Count) database(s) in $BackupRoot" -ForegroundColor Green
Write-Host "Copy this folder to another machine or storage; keep the master-key password from the installation with it." -ForegroundColor Yellow
