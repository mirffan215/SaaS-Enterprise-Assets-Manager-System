<#
.SYNOPSIS
    Restores a move package made by Export-ForMove.ps1 onto a NEW, freshly installed EAMS server.

.DESCRIPTION
    Run it on the new server AFTER Install-SaaS.ps1 has installed an empty EAMS there (same program version). It
      1. checks the package (SHA-256 of every database backup) and the new installation,
      2. takes the site offline,
      3. restores the catalog and every company database (replacing the empty ones the installer made),
      4. repairs database access (the application identity and the provisioning login of this server own and open them),
      5. re-opens each database's encryption key with the old master-key password,
      6. restores the keys and the uploaded documents,
      7. carries the operator key, master-key password, mail and alert settings over into this server's settings,
      8. starts the site; it brings every company database up to this program version on its first start (a few minutes).
    Nothing of the package is changed, so it can be run again.

.EXAMPLE
    .\Import-FromMove.ps1
    .\Import-FromMove.ps1 -MoveFolder "D:\EAMS-Move-20261021_101500"
.NOTES
    Elevated PowerShell, SQL Server sysadmin. The new site must use the same SQL Server address as before for the company connections stored in
    the catalog (the usual "localhost"); if the SQL Server moved elsewhere, the stored connections must be recreated: ask for help first.
#>
[CmdletBinding()]
param(
    [string] $MoveFolder = $PSScriptRoot,
    [string] $SqlServer = "localhost",
    [string] $SitePath = "C:\inetpub\EAMSCloud",
    [string] $DataRoot = "C:\EAMSCloud",
    [string] $AppPoolName = "EAMSCloud",
    [switch] $SkipStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }
function Sq([string] $Text) { return $Text.Replace("'", "''") }

function Invoke-Sql([string] $Query, [string] $Database = "master") {
    $out = & sqlcmd -S $SqlServer -E -b -C -d $Database -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $($out -join ' ')" }
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}

function Invoke-SqlFile([string] $Path, [string] $Database, [hashtable] $Variables) {
    foreach ($k in $Variables.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$Variables[$k], "Process") }
    try {
        $out = & sqlcmd -S $SqlServer -E -b -I -C -d $Database -i $Path 2>&1
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed on $(Split-Path -Leaf $Path): $(($out | Select-Object -Last 5) -join ' ')" }
    } finally { foreach ($k in $Variables.Keys) { [Environment]::SetEnvironmentVariable($k, $null, "Process") } }
}

# ---------------------------------------------------------------- checks
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script from an elevated PowerShell (Run as administrator)." }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found." }
Import-Module WebAdministration

$manifestFile = Join-Path $MoveFolder "manifest.json"
if (-not (Test-Path $manifestFile)) { throw "No manifest.json in '$MoveFolder': give -MoveFolder the folder made by Export-ForMove.ps1." }
$manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
$newSettingsFile = Join-Path $SitePath "appsettings.Production.json"
if (-not (Test-Path $newSettingsFile)) { throw "EAMS is not installed here yet ($newSettingsFile is missing). Run Install-SaaS.ps1 first (see README.txt)." }
$oldSettingsFile = Join-Path $MoveFolder "settings\appsettings.Production.json"
if (-not (Test-Path $oldSettingsFile)) { throw "The package has no settings\appsettings.Production.json." }

Write-Step "Checking the package"
foreach ($d in $manifest.Databases) {
    $file = Join-Path $MoveFolder ("databases\" + $d.File)
    if (-not (Test-Path $file)) { throw "Missing database backup: $($d.File)" }
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $d.Sha256) { throw "$($d.File) is damaged (its SHA-256 does not match the manifest). Copy the folder again." }
}
Write-Host "  $($manifest.Databases.Count) database backup(s) are intact." -ForegroundColor Green

$newJson = Get-Content $newSettingsFile -Raw | ConvertFrom-Json
$oldJson = Get-Content $oldSettingsFile -Raw | ConvertFrom-Json
if (-not $oldJson.PSObject.Properties["Tenancy"] -or -not $newJson.PSObject.Properties["Tenancy"]) { throw "A settings file has no Tenancy section: both sites must be EAMS Cloud installations." }
$oldTenancy = $oldJson.Tenancy
$newTenancy = $newJson.Tenancy
$poolIdentity = "IIS APPPOOL\$AppPoolName"
$provisioner = if ("$($newTenancy.ServerConnectionString)" -match "User Id=([^;]+)") { $Matches[1] } else { $null }
$masterKeyPassword = "$($oldTenancy.MasterKeyPassword)"

# the company connections stored in the catalog name a SQL Server address: it must still be right here
$oldServer = if ("$($oldTenancy.TenantConnectionTemplate)" -match "Server=([^;]+)") { $Matches[1] } else { $null }
$newServer = if ("$($newTenancy.TenantConnectionTemplate)" -match "Server=([^;]+)") { $Matches[1] } else { $null }
if ($oldServer -and $newServer -and $oldServer -ne $newServer) {
    Write-Warning "The old site reached SQL Server as '$oldServer', this one as '$newServer'. The company connections stored in the catalog still say '$oldServer'. Stop here unless that name also works on this server."
    if ((Read-Host "Continue anyway? (yes/no)") -ne "yes") { return }
}
if (-not $masterKeyPassword) { Write-Warning "The old settings have no Tenancy:MasterKeyPassword: encrypted fields (software licence keys) may not be readable." }

# ---------------------------------------------------------------- site offline
Write-Step "Taking the site offline"
$offline = Join-Path $SitePath "app_offline.htm"
Set-Content -Path $offline -Value "<html><body><h1>The site is being moved</h1><p>Please try again in a few minutes.</p></body></html>"
if (Test-Path "IIS:\AppPools\$AppPoolName") { try { Stop-WebAppPool -Name $AppPoolName } catch { Write-Verbose $_.Exception.Message } }
Start-Sleep -Seconds 3

try {
    # ------------------------------------------------------------ restore
    $dataDir = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(400));") | Select-Object -First 1
    $logDir  = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(400));") | Select-Object -First 1
    if (-not $dataDir -or $dataDir -eq "NULL") { throw "SQL Server's default data folder is not known." }
    if (-not $logDir -or $logDir -eq "NULL") { $logDir = $dataDir }
    $newestPerDatabase = $manifest.Databases | Group-Object Database | ForEach-Object { $_.Group | Select-Object -Last 1 }

    foreach ($d in $newestPerDatabase) {
        $db = $d.Database
        if ($db -notmatch '^[A-Za-z0-9_]+$') { throw "Unexpected database name '$db'." }
        $file = Join-Path $MoveFolder ("databases\" + $d.File)
        Write-Step "Restoring $db"
        $moves = @()
        $index = 0
        # RESTORE FILELISTONLY: LogicalName|PhysicalName|Type|...  (Type D = data, L = log)
        $list = & sqlcmd -S $SqlServer -E -b -C -h -1 -W -s "|" -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$(Sq $file)';" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Cannot read the backup $($d.File): $($list -join ' ')" }
        foreach ($row in $list) {
            $cols = "$row".Split("|")
            if ($cols.Count -lt 3 -or $cols[2].Trim() -notin @("D", "L", "S", "F")) { continue }
            $type = $cols[2].Trim()
            $target = if ($type -eq "L") { Join-Path $logDir "${db}_$index.ldf" } elseif ($type -eq "D") { Join-Path $dataDir "${db}_$index.mdf" } else { Join-Path $dataDir "${db}_$index" }
            $moves += "MOVE N'$(Sq $cols[0].Trim())' TO N'$(Sq $target)'"
            $index++
        }
        if ($moves.Count -eq 0) { throw "The backup $($d.File) lists no files." }
        [void](Invoke-Sql "IF DB_ID(N'$db') IS NOT NULL ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;")
        [void](Invoke-Sql "RESTORE DATABASE [$db] FROM DISK = N'$(Sq $file)' WITH REPLACE, RECOVERY, CHECKSUM, $($moves -join ', ');")
        [void](Invoke-Sql "ALTER DATABASE [$db] SET MULTI_USER;")
        Write-Host "  OK  $db" -ForegroundColor Green

        # --------------------------------------------------------- repair access in the restored database
        # the application identity exists here as a NEW login: connect the database's user to it, or create the user (script 08 / catalog roles)
        $hasUser = (Invoke-Sql "SELECT COUNT(*) FROM sys.database_principals WHERE name = N'$(Sq $poolIdentity)';" $db) | Select-Object -First 1
        if ([int]$hasUser -gt 0) {
            [void](Invoke-Sql "ALTER USER [$poolIdentity] WITH LOGIN = [$poolIdentity];" $db)
        }
        elseif ($db -like "ITInv_Tenant_*") {
            $script08 = Join-Path $SitePath "SQLScripts\08_DatabaseSecurity.sql"
            if (-not (Test-Path $script08)) { throw "$script08 was not found: is EAMS installed here (Install-SaaS.ps1)?" }
            Invoke-SqlFile $script08 $db @{ DatabaseName = $db; AppLogin = $poolIdentity }
        }
        else {
            [void](Invoke-Sql "CREATE USER [$poolIdentity] FOR LOGIN [$poolIdentity]; ALTER ROLE db_datareader ADD MEMBER [$poolIdentity]; ALTER ROLE db_datawriter ADD MEMBER [$poolIdentity];" $db)
        }
        if ($db -like "ITInv_Tenant_*") {
            # the provisioning login of THIS server must own the company databases (it backs them up and upgrades them)
            if ($provisioner) { [void](Invoke-Sql "ALTER AUTHORIZATION ON DATABASE::[$db] TO [$provisioner];") }
            # the database master key is protected by the OLD server's key: open it with the master-key password and protect it with this server's
            if ($masterKeyPassword) {
                $hasKey = (Invoke-Sql "SELECT COUNT(*) FROM sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##';" $db) | Select-Object -First 1
                if ([int]$hasKey -gt 0) {
                    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("eams-mk-" + [Guid]::NewGuid().ToString("N") + ".sql")
                    Set-Content -Path $tmp -Value "OPEN MASTER KEY DECRYPTION BY PASSWORD = N'`$(MasterKeyPassword)';`r`nALTER MASTER KEY ADD ENCRYPTION BY SERVICE MASTER KEY;`r`nCLOSE MASTER KEY;"
                    try { Invoke-SqlFile $tmp $db @{ MasterKeyPassword = $masterKeyPassword } }
                    catch { Write-Warning "${db}: the encryption key could not be re-opened with the old master-key password ($($_.Exception.Message))" }
                    finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
                }
            }
        }
    }

    # ------------------------------------------------------------ keys and documents
    Write-Step "Restoring the keys and documents"
    foreach ($name in @("Keys", "Documents")) {
        $src = Join-Path $MoveFolder $name
        if (Test-Path $src) {
            $dst = Join-Path $DataRoot $name
            New-Item -ItemType Directory -Path $dst -Force | Out-Null
            & robocopy $src $dst /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null   # adds to what is there: the new installation's own keys stay
            if ($LASTEXITCODE -ge 8) { throw "Copying $name failed (robocopy exit code $LASTEXITCODE)." }
            & icacls $dst /grant "${poolIdentity}:(OI)(CI)M" /T /Q | Out-Null
        }
    }

    # ------------------------------------------------------------ settings
    Write-Step "Carrying the operator settings over"
    $carry = @("MasterKeyPassword", "OperatorKey", "DemoAdminPassword", "DemoEnabled", "OperatorEmail", "Smtp", "AlertWebhookUrl", "OffsitePassword", "AutoUpgrade", "ExportRequiresApproval")
    foreach ($name in $carry) {
        if ($oldTenancy -and $oldTenancy.PSObject.Properties[$name]) { $newTenancy | Add-Member -NotePropertyName $name -NotePropertyValue $oldTenancy.$name -Force }
    }
    [IO.File]::WriteAllText($newSettingsFile, ($newJson | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    & icacls $newSettingsFile /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" "${poolIdentity}:R" | Out-Null
    foreach ($name in @("OffsiteFolder", "OffsiteBlobUrl", "PublicBaseUrl")) {
        if ($oldTenancy -and $oldTenancy.PSObject.Properties[$name] -and "$($oldTenancy.$name)") { Write-Host "  Not copied (set it for this server with Set-OperatorAccess.ps1 if needed): $name = $($oldTenancy.$name)" -ForegroundColor Yellow }
    }
}
finally {
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- start
if (-not $SkipStart) {
    Write-Step "Starting the site (it upgrades the company databases to this program version first: allow a few minutes)"
    Start-WebAppPool -Name $AppPoolName
}
Write-Host ""
Write-Host "Move complete. Check: open the new address, sign in to a company, open an asset and a document." -ForegroundColor Green
Write-Host "Companies restored: $(($newestPerDatabase | Where-Object { $_.Database -like 'ITInv_Tenant_*' } | ForEach-Object { $_.Database.Substring(13) }) -join ', ')" -ForegroundColor Green
Write-Host "If the sign-in page shows an error for a company, look at the newest file in $DataRoot\Logs and send it to support." -ForegroundColor Yellow
