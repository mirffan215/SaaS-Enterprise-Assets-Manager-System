<#
.SYNOPSIS
    Release 2.6 load test with 100,000 assets - on a COPY of the database and a separate test site.

.DESCRIPTION
    1. Copies the production database to AMGH_ITInventory_LoadTest (COPY_ONLY backup + restore; production is not changed).
    2. Creates the test application pool, brings the copy up to date and grants the test pool access (Deploy-Database.ps1).
    3. Generates the load-test data in the copy (deployment\LoadTest\Generate-LoadData.sql).
    4. Deploys a separate test site (default port 8443) that uses the copy. Its notification sender is OFF:
       the copy contains real e-mail settings and real people's addresses.
    5. Runs the load-test tool (tools\AMGH.LoadTest) and SQL Server's performance report.
    Results: C:\AMGH\LoadTest\Results\<date-time>\  (summary.csv, loadtest.txt, sql-performance.txt, log).

    Run outside business hours if the copy is on the production SQL Server: the test competes for the same CPU and disks.
    Remove everything afterwards with -Cleanup.

.EXAMPLE
    .\Invoke-LoadTest.ps1 -TestUser it.admin -TrustServerCertificate
.EXAMPLE
    .\Invoke-LoadTest.ps1 -TestUser it.admin -SkipCopy -SkipSite -Users 50 -Minutes 15    # run again on the same copy
.EXAMPLE
    .\Invoke-LoadTest.ps1 -Cleanup
#>
[CmdletBinding()]
param(
    [string] $ServerInstance = "localhost",
    [string] $SourceDatabase = "AMGH_ITInventory",
    [string] $CopyDatabase = "AMGH_ITInventory_LoadTest",
    [string] $CompanyCode = "AMGH",
    [int]    $AssetCount = 100000,
    [string] $TestSiteName = "AMGHITInventory-LoadTest",
    [int]    $TestPort = 8443,
    [string] $CertificateThumbprint,
    [string] $TestUser,
    [int]    $Users = 25,
    [double] $Minutes = 10,
    [string] $BackupFolder,
    [switch] $SkipCopy,
    [switch] $SkipSite,
    [switch] $Cleanup,
    [switch] $TrustServerCertificate
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($CopyDatabase -notmatch 'LoadTest' -or $CopyDatabase -notmatch '^[A-Za-z0-9_]+$') { throw "The copy's name must contain 'LoadTest' (letters, digits, '_')." }
if ($CopyDatabase -eq $SourceDatabase) { throw "The copy must not be the production database." }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $here)
$work = "C:\AMGH\LoadTest"
$results = Join-Path $work ("Results\" + (Get-Date -Format "yyyyMMdd-HHmm"))
New-Item -ItemType Directory -Force -Path $results | Out-Null
$logFile = Join-Path $results "invoke-loadtest.log"
$sitePath = "C:\inetpub\$TestSiteName"

function Write-Log([string] $Message, [string] $Level = "INFO") {
    $line = "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message
    $colour = switch ($Level) { "ERROR" { "Red" } "PASS" { "Green" } "WARN" { "Yellow" } default { "Gray" } }
    Write-Host $line -ForegroundColor $colour
    Add-Content -Path $logFile -Value $line
}
function Invoke-Sql([string] $Database, [string] $File, [string] $Query, [hashtable] $Variables = @{}) {
    $sqlArgs = @("-S", $ServerInstance, "-E", "-b", "-I", "-W", "-d", $Database)
    if ($TrustServerCertificate) { $sqlArgs += "-C" }
    if ($File) { $sqlArgs += @("-i", $File) } else { $sqlArgs += @("-Q", $Query) }
    foreach ($k in $Variables.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$Variables[$k], "Process") }
    $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    try { $output = & sqlcmd @sqlArgs 2>&1 | ForEach-Object { "$_" }; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    $output | ForEach-Object { Add-Content -Path $logFile -Value "    $_" }
    if ($code -ne 0) {
        $output | Select-Object -Last 15 | ForEach-Object { Write-Log "    $_" "ERROR" }
        throw "sqlcmd failed ($(if ($File) { Split-Path -Leaf $File } else { 'query' })), exit code $code. See $logFile"
    }
    return $output
}

Import-Module WebAdministration
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found." }

# ---------------------------------------------------------------- cleanup
if ($Cleanup) {
    Write-Log "Removing the test site, its application pool and the copy $CopyDatabase"
    if (Get-Website -Name $TestSiteName) { Remove-Website -Name $TestSiteName }
    if (Test-Path "IIS:\AppPools\$TestSiteName") { Remove-WebAppPool -Name $TestSiteName }
    if (Test-Path $sitePath) { Remove-Item $sitePath -Recurse -Force }
    Invoke-Sql "master" -Query "IF DB_ID(N'$CopyDatabase') IS NOT NULL BEGIN ALTER DATABASE [$CopyDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$CopyDatabase]; END;
        IF SUSER_ID(N'IIS APPPOOL\$TestSiteName') IS NOT NULL DROP LOGIN [IIS APPPOOL\$TestSiteName];" | Out-Null
    Write-Log "Done. Results are kept in $work\Results." "PASS"
    return
}
if (-not $TestUser) { throw "-TestUser is required: a user of company $CompanyCode (the copy has the same users and passwords as production)." }
$securePw = Read-Host "Password of $TestUser (used only for the test site)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePw)
try { $plainPw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

# ---------------------------------------------------------------- 1-3. copy, prepare, generate
if (-not (Test-Path "IIS:\AppPools\$TestSiteName")) { New-WebAppPool -Name $TestSiteName | Out-Null }  # the SQL login needs the pool to exist
if (-not $SkipCopy) {
    if (-not $BackupFolder) {
        $BackupFolder = (Invoke-Sql "master" -Query "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(400));" | Select-Object -Last 1).Trim()
    }
    $bak = Join-Path $BackupFolder "$SourceDatabase-loadtest.bak"
    Write-Log "Backing up $SourceDatabase (COPY_ONLY: the normal backup chain is not affected)"
    Invoke-Sql "master" -Query "BACKUP DATABASE [$SourceDatabase] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT;" | Out-Null
    $files = Invoke-Sql "master" -Query "SET NOCOUNT ON; SELECT 'FILE|' + name + '|' + physical_name FROM [$SourceDatabase].sys.database_files;" | Where-Object { $_ -like "FILE|*" }
    $moves = foreach ($f in $files) {
        $p = $f.Split('|'); $dir = Split-Path -Parent $p[2]; $ext = [IO.Path]::GetExtension($p[2])
        "MOVE N'$($p[1])' TO N'$(Join-Path $dir "$CopyDatabase`_$($p[1])$ext")'"
    }
    Write-Log "Restoring as $CopyDatabase"
    Invoke-Sql "master" -Query "IF DB_ID(N'$CopyDatabase') IS NOT NULL BEGIN ALTER DATABASE [$CopyDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$CopyDatabase]; END;
        RESTORE DATABASE [$CopyDatabase] FROM DISK = N'$bak' WITH $($moves -join ', '), RECOVERY, CHECKSUM;" | Out-Null
    Remove-Item $bak -Force -ErrorAction SilentlyContinue

    Write-Log "Bringing the copy up to date and granting the test pool access"
    Write-Log "Deploy-Database will ask for the database master key password: enter the production one (the copy already has its key, so it is not changed)." "WARN"
    $deployArgs = @{ ServerInstance = $ServerInstance; DatabaseName = $CopyDatabase; AppLogin = "IIS APPPOOL\$TestSiteName"; AdminEmail = "loadtest@example.invalid"; SkipSeed = $true }
    if ($TrustServerCertificate) { $deployArgs.TrustServerCertificate = $true }
    & (Join-Path $here "Deploy-Database.ps1") @deployArgs

    Write-Log "Generating $AssetCount assets in company $CompanyCode (a few minutes)"
    Invoke-Sql $CopyDatabase -File (Join-Path $root "deployment\LoadTest\Generate-LoadData.sql") -Variables @{ DatabaseName = $CopyDatabase; CompanyCode = $CompanyCode; AssetCount = $AssetCount } |
        Where-Object { $_ -match '^(Generating|Organisation|Assets|Assignments|Warranties|Maintenance|Movements|Verifications|Audit|Notifications|Done)' } |
        ForEach-Object { Write-Log "  $_" }
}

# ---------------------------------------------------------------- 4. test site (settings written BEFORE the first start)
$url = "https://localhost:$TestPort"
if (-not $SkipSite) {
    New-Item -ItemType Directory -Force -Path $sitePath, "$work\Documents", "$work\Keys", "$work\Logs" | Out-Null
    $settings = [ordered]@{
        ConnectionStrings = @{ AMGHDatabase = "Server=$ServerInstance;Database=$CopyDatabase;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Application Name=AMGH.ITInventory.LoadTest" }
        Notifications = @{ DisableWorker = $true }      # never e-mail real people from the copy
        Storage = @{ RootPath = "$work\Documents" }
        DataProtection = @{ KeysPath = "$work\Keys" }
        Serilog = @{ WriteTo = @(@{ Args = @{ path = "$work\Logs\amgh-loadtest-.log" } }) }
    }
    $settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $sitePath "appsettings.Production.json") -Encoding UTF8
    Write-Log "Deploying the test site $TestSiteName on port $TestPort (database: $CopyDatabase, notifications off)"
    $webArgs = @{ SiteName = $TestSiteName; AppPoolName = $TestSiteName; SitePath = $sitePath; Port = $TestPort; HostName = "localhost"
                  LogsPath = "$work\Logs"; KeysPath = "$work\Keys"; DocumentsPath = "$work\Documents" }
    if ($CertificateThumbprint) { $webArgs.CertificateThumbprint = $CertificateThumbprint }
    & (Join-Path $here "Deploy-WebApp.ps1") @webArgs
    $prod = Get-Content (Join-Path $sitePath "appsettings.Production.json") -Raw
    if ($prod -notmatch [regex]::Escape($CopyDatabase) -or $prod -notmatch '"DisableWorker":\s*true') { throw "Safety check failed: the test site's settings no longer point to the copy with notifications off." }
}
for ($i = 0; $i -lt 30; $i++) {
    try { if ((Invoke-WebRequest "$url/health" -UseBasicParsing -TimeoutSec 10).Content -eq "Healthy") { break } } catch { }
    Start-Sleep -Seconds 2
}

# ---------------------------------------------------------------- 5. load test + SQL report
$since = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss")
Write-Log "Running the load test: $Users users for $Minutes minutes against $url"
$env:AMGH_LOADTEST_PASSWORD = $plainPw
$exit = 1
try {
    Push-Location (Join-Path $root "tools\AMGH.LoadTest")
    & dotnet run -c Release -- --url $url --company $CompanyCode --user $TestUser --users $Users --minutes $Minutes --insecure --out (Join-Path $results "summary.csv") |
        Tee-Object -FilePath (Join-Path $results "loadtest.txt")
    $exit = $LASTEXITCODE
} finally { Pop-Location; Remove-Item Env:AMGH_LOADTEST_PASSWORD -ErrorAction SilentlyContinue; $plainPw = $null }

Write-Log "Collecting SQL Server's performance report"
Invoke-Sql $CopyDatabase -File (Join-Path $root "deployment\LoadTest\Report-SqlPerformance.sql") -Variables @{ DatabaseName = $CopyDatabase; SinceUtc = $since } |
    Set-Content (Join-Path $results "sql-performance.txt")
if ($exit -eq 0) { Write-Log "LOAD TEST PASSED. Results: $results" "PASS" }
else { Write-Log "LOAD TEST FAILED (see loadtest.txt and sql-performance.txt in $results). Send the folder to the developer." "WARN" }
Write-Log "When finished: .\Invoke-LoadTest.ps1 -Cleanup"
