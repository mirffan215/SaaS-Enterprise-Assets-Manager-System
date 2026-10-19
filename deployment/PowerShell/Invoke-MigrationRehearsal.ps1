<#
.SYNOPSIS
    Rehearses the Release 2.0 multi-company migration on a COPY of the production database.
    Production is only read (a copy-only backup); nothing in it is changed.

.DESCRIPTION
      1. COPY_ONLY backup of the production database (does not disturb your backup chain);
      2. restore it as <Database>_Rehearsal with new file names;
      3. run the Release 2.0 scripts on the copy: 02, 10, 03, 04, 05, 06;
      4. print the reconciliation report (tblMigrationLog);
      5. with -TestRollback: roll back, check the v1.1 schema is back, then migrate again;
      6. run the isolation verification (creates test company DEMO in the copy only);
      7. drop the copy unless -KeepCopy.
    Writes a log next to this script. Exit code 0 = rehearsal passed.

.EXAMPLE
    .\Invoke-MigrationRehearsal.ps1 -TestRollback
    .\Invoke-MigrationRehearsal.ps1 -ServerInstance "localhost\SQL2022" -BackupFolder "D:\SQLBackup" -KeepCopy
#>
[CmdletBinding()]
param(
    [string] $ServerInstance = "localhost",
    [string] $SourceDatabase = "AMGH_ITInventory",
    [string] $BackupFolder,
    [string] $AdminUsername = "admin",
    [switch] $TestRollback,
    [switch] $KeepCopy,
    [switch] $TrustServerCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Join-Path (Split-Path -Parent $PSScriptRoot) "SQLScripts"
$copy      = "${SourceDatabase}_Rehearsal"
$stamp     = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile   = Join-Path $PSScriptRoot "Rehearsal_$stamp.log"

function Write-Log([string] $Message, [string] $Level = "INFO") {
    $line = "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message
    $colour = switch ($Level) { "ERROR" { "Red" } "PASS" { "Green" } "WARN" { "Yellow" } default { "Gray" } }
    Write-Host $line -ForegroundColor $colour
    Add-Content -Path $logFile -Value $line
}

function Invoke-Sql([string] $Database, [string] $File, [string] $Query, [hashtable] $Variables = @{}) {
    $sqlArgs = @("-S", $ServerInstance, "-E", "-b", "-I", "-W", "-s", " | ", "-d", $Database)
    if ($TrustServerCertificate) { $sqlArgs += "-C" }
    if ($File) { $sqlArgs += @("-i", $File) } else { $sqlArgs += @("-Q", $Query) }
    foreach ($k in $Variables.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$Variables[$k], "Process") }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & sqlcmd @sqlArgs 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    $output | ForEach-Object { Add-Content -Path $logFile -Value "    $_" }
    if ($code -ne 0) {
        $output | Select-Object -Last 20 | ForEach-Object { Write-Log "    $_" "ERROR" }
        throw "sqlcmd failed ($(if ($File) { Split-Path -Leaf $File } else { 'query' })), exit code $code. See $logFile"
    }
    return $output
}

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found." }
$vars = @{ DatabaseName = $copy; AllowMultiCompany = "YES"; AdminUsername = $AdminUsername }
$results = [ordered]@{}

try {
    # ------------------------------------------------------------ 1-2 copy
    if (-not $BackupFolder) {
        $BackupFolder = (Invoke-Sql "master" -Query "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(400));" |
            Where-Object { $_ -match '\\' } | Select-Object -First 1).Trim()
    }
    $bak = Join-Path $BackupFolder "${SourceDatabase}_PreRelease2_$stamp.bak"
    Write-Log "Copy-only backup of $SourceDatabase to $bak"
    Invoke-Sql "master" -Query "BACKUP DATABASE [$SourceDatabase] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT;" | Out-Null

    Write-Log "Restoring as $copy"
    # Logical file names and folders come from the source database itself (same files as in the backup).
    $fileList = Invoke-Sql "master" -Query "SET NOCOUNT ON; SELECT 'FILE|' + name + '|' + physical_name FROM [$SourceDatabase].sys.database_files;"
    $moves = @()
    foreach ($line in $fileList | Where-Object { $_ -like 'FILE|*' }) {
        $parts = $line.Trim().Split('|')
        $logical = $parts[1]; $physical = $parts[2]
        $dir = Split-Path -Parent $physical
        $ext = [IO.Path]::GetExtension($physical)
        $moves += "MOVE N'$logical' TO N'$(Join-Path $dir "${copy}_$logical$ext")'"
    }
    if ($moves.Count -eq 0) { throw "Could not read the file list of the backup." }
    Invoke-Sql "master" -Query "IF DB_ID(N'$copy') IS NOT NULL BEGIN ALTER DATABASE [$copy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$copy]; END; RESTORE DATABASE [$copy] FROM DISK = N'$bak' WITH $($moves -join ', '), RECOVERY, CHECKSUM;" | Out-Null
    Write-Log "Copy restored."

    # ------------------------------------------------------------ 3 migrate the copy
    $forward = @("02_Tables.sql", "10_MultiCompany.sql", "03_Functions.sql", "04_Views.sql", "05_StoredProcedures.sql", "06_Triggers.sql")
    $sw = [Diagnostics.Stopwatch]::StartNew()
    foreach ($s in $forward) { Write-Log "Running $s on the copy"; Invoke-Sql $copy -File (Join-Path $scriptDir $s) -Variables $vars | Out-Null }
    $sw.Stop()
    $results["Migration ran"] = "PASS ({0:N0} s)" -f $sw.Elapsed.TotalSeconds

    # ------------------------------------------------------------ 4 reconciliation report
    $report = Invoke-Sql $copy -Query "SET NOCOUNT ON; SELECT CheckName, Expected, Actual, CASE Passed WHEN 1 THEN 'PASS' WHEN 0 THEN 'FAIL' ELSE '' END FROM dbo.tblMigrationLog WHERE Migration = N'MC-1' ORDER BY MigrationLogId;"
    Write-Log "Reconciliation report:"
    $report | ForEach-Object { Write-Log "    $_" }
    $results["Reconciliation"] = if ($report -match '\| FAIL') { "FAIL" } else { "PASS" }

    # ------------------------------------------------------------ 5 rollback round trip
    if ($TestRollback) {
        Write-Log "Testing the rollback on the copy"
        Invoke-Sql $copy -File (Join-Path $scriptDir "10_MultiCompany_Rollback.sql") -Variables $vars | Out-Null
        $left = Invoke-Sql $copy -Query "SET NOCOUNT ON; SELECT 'LEFT|' + CAST((SELECT COUNT(*) FROM sys.columns WHERE name = N'CompanyId') + (SELECT COUNT(*) FROM sys.security_policies) + CASE WHEN OBJECT_ID(N'dbo.tblCompany') IS NULL THEN 0 ELSE 1 END AS VARCHAR(10));"
        $results["Rollback restores v1.1 schema"] = if (($left -join '') -match 'LEFT\|0') { "PASS" } else { "FAIL" }
        Write-Log "Migrating the copy again after the rollback"
        foreach ($s in $forward) { Invoke-Sql $copy -File (Join-Path $scriptDir $s) -Variables $vars | Out-Null }
        $results["Migration after rollback"] = "PASS"
    }

    # ------------------------------------------------------------ 6 isolation verification
    Write-Log "Isolation verification (test company DEMO, copy only)"
    $verify = Invoke-Sql $copy -File (Join-Path $scriptDir "10_MultiCompany_Verify.sql") -Variables $vars
    $verify | Where-Object { $_ -match 'PASS|FAIL' } | ForEach-Object { Write-Log "    $_" }
    $results["Isolation verification"] = "PASS"
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $results["Stopped"] = "FAIL: $($_.Exception.Message)"
}
finally {
    if (-not $KeepCopy -and (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        try {
            Invoke-Sql "master" -Query "IF DB_ID(N'$copy') IS NOT NULL BEGIN ALTER DATABASE [$copy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$copy]; END" | Out-Null
            Write-Log "Rehearsal copy dropped (use -KeepCopy to inspect it)."
        } catch { Write-Log "Could not drop ${copy}: $($_.Exception.Message)" "WARN" }
    }
    foreach ($k in @("DatabaseName", "AllowMultiCompany", "AdminUsername")) { [Environment]::SetEnvironmentVariable($k, $null, "Process") }
}

Write-Host ""
Write-Log "================ REHEARSAL SUMMARY ================"
foreach ($k in $results.Keys) {
    $level = if ($results[$k] -like "FAIL*") { "ERROR" } else { "PASS" }
    Write-Log ("{0,-32} {1}" -f $k, $results[$k]) $level
}
Write-Log "Log: $logFile"
if ($results.Values | Where-Object { $_ -like "FAIL*" }) { exit 1 } else { exit 0 }
