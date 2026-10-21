<#
.SYNOPSIS
    Deploys (or upgrades in place) the AMGH IT Inventory database on a local SQL Server 2022 instance.

.DESCRIPTION
    Runs deployment\SQLScripts\01..08 in order with sqlcmd, aborting on the first error.
    All scripts are idempotent, so the script can be re-run safely to apply new versions.

    Prompts (never echoed, never written to disk):
      * initial administrator password  -> hashed here (PBKDF2-HMAC-SHA512, 100,000 iterations,
        ASP.NET Core Identity v3 format) so the plain text never reaches SQL Server
      * database master key password    -> protects the column-encryption key

.EXAMPLE
    .\Deploy-Database.ps1 -ServerInstance "localhost" -AppLogin "IIS APPPOOL\AMGHITInventory" `
        -AdminEmail "it.admin@amgh.local"

.NOTES
    Requires: Windows PowerShell 5.1+ (.NET Framework 4.7.2+), sqlcmd, sysadmin rights on the instance.
#>
[CmdletBinding()]
param(
    [string] $ServerInstance = "localhost",
    [string] $DatabaseName = "AMGH_ITInventory",
    [Parameter(Mandatory = $true)] [string] $AppLogin,
    [string] $AdminUsername = "admin",
    [Parameter(Mandatory = $true)] [string] $AdminEmail,
    [string] $AdminDisplayName = "System Administrator",
    [switch] $TrustServerCertificate,
    [switch] $SkipSeed,
    # Release 2.0: the multi-company migration (script 10). Required, so it is always a deliberate step;
    # rehearse first with Invoke-MigrationRehearsal.ps1 and deploy the Release 2.0 web application with it.
    [switch] $EnableMultiCompany,
    # optional, for unattended runs; otherwise you are asked (only used when the database has no master key yet)
    [SecureString] $MasterKeyPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Join-Path (Split-Path -Parent $PSScriptRoot) "SQLScripts"
$logFile   = Join-Path $PSScriptRoot ("Deploy-Database_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Log([string] $Message, [string] $Level = "INFO") {
    $line = "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message
    Write-Host $line -ForegroundColor $(if ($Level -eq "ERROR") { "Red" } elseif ($Level -eq "WARN") { "Yellow" } else { "Gray" })
    Add-Content -Path $logFile -Value $line
}

. (Join-Path $PSScriptRoot "PasswordFunctions.ps1")

<# All scripting variables are passed to sqlcmd as process environment variables
   (sqlcmd reads scripting variables from the environment). This avoids command-line
   quoting problems with values containing spaces or backslashes (e.g. IIS APPPOOL\Name)
   and keeps secrets off the command line and out of the log. #>
function Invoke-SqlScript([string] $File, [hashtable] $Variables) {
    $sqlArgs = @("-S", $ServerInstance, "-E", "-b", "-I", "-i", $File)
    if ($TrustServerCertificate) { $sqlArgs += "-C" }
    foreach ($k in $Variables.Keys) {
        [Environment]::SetEnvironmentVariable($k, [string]$Variables[$k], "Process")
    }
    Write-Log "Running $(Split-Path -Leaf $File)"
    # Windows PowerShell 5.1 turns any stderr text from a native program into an error record,
    # which "Stop" would treat as fatal. Success/failure is decided by sqlcmd's exit code (-b).
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & sqlcmd @sqlArgs 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $output | ForEach-Object { Add-Content -Path $logFile -Value "    $_" }
    if ($code -ne 0) {
        $output | Select-Object -Last 15 | ForEach-Object { Write-Log "    $_" "ERROR" }
        throw "sqlcmd failed on $(Split-Path -Leaf $File) (exit code $code). See $logFile"
    }
}

# ---------------------------------------------------------------- preflight
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "sqlcmd was not found. Install the SQL Server command-line utilities and retry."
}
if ($AdminEmail -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw "AdminEmail is not a valid e-mail address." }
if ($DatabaseName -notmatch '^[A-Za-z0-9_]{1,100}$') { throw "DatabaseName may contain letters, digits and '_' only." }
if ($AdminUsername -notmatch '^[A-Za-z0-9._-]{3,100}$') { throw "AdminUsername may contain letters, digits, '.', '_' and '-' (3-100 chars)." }

Write-Log "AMGH IT Inventory database deployment -> $ServerInstance / $DatabaseName"

# A database that is already multi-company (deployed with Release 2.0 or later) needs no switch: the migration
# only re-checks itself there. A v1.x database still requires the deliberate -EnableMultiCompany.
if (-not $EnableMultiCompany) {
    $probe = @("-S", $ServerInstance, "-E", "-b", "-h", "-1", "-W", "-Q",
        "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$DatabaseName') IS NOT NULL AND OBJECT_ID(N'[$DatabaseName].dbo.tblCompany', N'U') IS NOT NULL THEN 1 ELSE 0 END")
    if ($TrustServerCertificate) { $probe += "-C" }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $answer = (& sqlcmd @probe 2>$null | Out-String).Trim() } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -eq 0 -and $answer -eq "1") {
        Write-Log "Database $DatabaseName is already multi-company: continuing (no -EnableMultiCompany switch needed)."
        $EnableMultiCompany = $true
    }
}
if (-not $EnableMultiCompany) {
    throw ("This database has not been migrated to multi-company yet (or could not be checked). Rehearse first with " +
           "Invoke-MigrationRehearsal.ps1, then run this script with -EnableMultiCompany together with the Release 2.x web " +
           "application. The v1.x web application cannot read data after the migration.")
}

if ($MasterKeyPassword) {
    $masterKeyPlain = ConvertTo-PlainText $MasterKeyPassword
    $problems = @(Test-PasswordPolicy $masterKeyPlain)
    if ($problems.Count -gt 0) { throw ("Master key password must contain: " + ($problems -join ", ")) }
} else {
    $masterKeyPlain = Read-ValidatedPassword "Database master key password (store it in the password vault)"
}
$vars = @{
    DatabaseName      = $DatabaseName
    MasterKeyPassword = $masterKeyPlain
    AppLogin          = $AppLogin
    AdminUsername     = $AdminUsername
    AdminEmail        = $AdminEmail
    AdminDisplayName  = $AdminDisplayName
    AdminPasswordHash = ""
    AllowMultiCompany = "YES"
}

if (-not $SkipSeed) {
    $adminPassword = Read-ValidatedPassword "Initial password for '$AdminUsername' (must be changed at first sign-in)"
    $vars.AdminPasswordHash = New-IdentityV3PasswordHash $adminPassword
    Remove-Variable adminPassword
}

# Script 10 (multi-company) runs right after the tables, before the code objects that use CompanyId.
$scripts = @(
    "01_CreateDatabase.sql", "02_Tables.sql", "10_MultiCompany.sql", "16_AssetConfiguration.sql", "17_TechnicianAssignment.sql", "03_Functions.sql", "04_Views.sql",
    "05_StoredProcedures.sql", "06_Triggers.sql", "07_SeedData.sql", "08_DatabaseSecurity.sql", "11_Workspaces.sql", "12_Reports.sql", "13_Approvals.sql", "14_Notifications.sql", "15_CompanyProfile.sql", "18_DocumentNumbering.sql", "19_CompanyDataExport.sql"
)
if ($SkipSeed) { $scripts = $scripts | Where-Object { $_ -ne "07_SeedData.sql" } }

try {
    foreach ($s in $scripts) {
        $path = Join-Path $scriptDir $s
        if (-not (Test-Path $path)) { throw "Missing script: $path" }
        Invoke-SqlScript -File $path -Variables $vars
    }
    # later changes (numbered, safe to run twice): see SQLScripts\Migrations\README.md
    $migrationDir = Join-Path $scriptDir "Migrations"
    if (Test-Path $migrationDir) {
        foreach ($m in (Get-ChildItem $migrationDir -Filter "????_*.sql" | Sort-Object Name)) { Invoke-SqlScript -File $m.FullName -Variables $vars }
    }
    Write-Log "Deployment completed successfully."
    Write-Log "NEXT: back up the encryption certificate - see docs\Deployment.md 'Encryption key backup'." "WARN"
}
finally {
    foreach ($n in $vars.Keys) { [Environment]::SetEnvironmentVariable($n, $null, "Process") }
    $vars.MasterKeyPassword = $null
$masterKeyPlain = $null
    Remove-Variable masterKeyPassword -ErrorAction SilentlyContinue
}
