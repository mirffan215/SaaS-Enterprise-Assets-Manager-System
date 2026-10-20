<#
.SYNOPSIS
    Creates (or replaces) the operator key and the demo administrator password of an installed SaaS site, and shows them once.

.DESCRIPTION
    * Operator key: the secret that opens the operator page (/Tenant/Admin) where you create companies and the demo company.
    * Demo password: the password of the demo company's administrator "admin".
    Both are random, are written into appsettings.Production.json of the site (readable only by Administrators and the application pool),
    and are printed once on this screen. Write them down in your password vault; they cannot be shown again.
    Existing values are kept unless you ask for new ones (-NewOperatorKey, -NewDemoPassword or -DemoPassword).
    Also turns the demo on (Tenancy:DemoEnabled) when a demo password is set, then restarts the application pool.

.EXAMPLE
    .\Set-OperatorAccess.ps1
    .\Set-OperatorAccess.ps1 -NewOperatorKey
    .\Set-OperatorAccess.ps1 -DemoPassword "My-Own-Strong#Pass2026"
#>
[CmdletBinding()]
param(
    [string] $SitePath = "C:\inetpub\EAMSCloud",
    [string] $AppPoolName = "EAMSCloud",
    [switch] $NewOperatorKey,
    [switch] $NewDemoPassword,
    [string] $DemoPassword,
    # prepare the folder the operator page uses for company backups, and tell the site about it
    [switch] $SetupBackups,
    [string] $BackupFolder = "C:\EAMSCloud\Backups",
    [string] $SqlServer = "localhost",
    [switch] $NoRestart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script from an elevated PowerShell (Run as administrator)." }

$file = Join-Path $SitePath "appsettings.Production.json"
if (-not (Test-Path $file)) { throw "Settings file not found: $file. Install the site first (Install-SaaS.ps1) or pass -SitePath." }

# Backup folder: the SQL Server service writes the backup files, the application pool may only list them.
function Initialize-BackupFolder([string] $Folder, [string] $SqlServer, [string] $PoolIdentity) {
    if (-not (Test-Path $Folder)) { New-Item -ItemType Directory -Path $Folder | Out-Null }
    $parts = @($SqlServer -split '\\')
    $local = $parts[0] -in @("localhost", ".", "(local)", $env:COMPUTERNAME)
    if (-not $local) { Write-Warning "SQL Server is on another machine: give its service account write access to $Folder (a share both can reach) yourself."; return }
    $serviceName = if ($parts.Count -gt 1) { 'MSSQL$' + $parts[1] } else { "MSSQLSERVER" }
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.StartName) { & icacls $Folder /grant "$($service.StartName):(OI)(CI)M" /Q | Out-Null }
    else { Write-Warning "Could not find the SQL Server service '$serviceName': give its account Modify rights on $Folder yourself." }
    & icacls $Folder /grant "${PoolIdentity}:(OI)(CI)M" /Q | Out-Null   # the site reads backups to offer them as a zip download, writes uploads to Restore\, and prunes old scheduled backups
}

function New-RandomText([int] $Length, [string] $Alphabet) {
    $chars = $Alphabet.ToCharArray()
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] ($Length * 4); $rng.GetBytes($bytes)
        return -join (0..($Length - 1) | ForEach-Object { $chars[[BitConverter]::ToUInt32($bytes, $_ * 4) % $chars.Length] })
    } finally { $rng.Dispose() }
}

# 20 characters with upper, lower, digit and symbol (the application's password rules), no look-alike letters, no common words
function New-StrongPassword {
    $upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; $lower = "abcdefghijkmnpqrstuvwxyz"; $digit = "23456789"; $symbol = "!#%*+-=?@"
    while ($true) {
        $text = (New-RandomText 5 $upper) + (New-RandomText 8 $lower) + (New-RandomText 4 $digit) + (New-RandomText 3 $symbol)
        $mixed = -join ($text.ToCharArray() | Sort-Object { Get-Random })
        $low = $mixed.ToLowerInvariant()
        if (@("password", "admin", "welcome", "qwerty", "letmein", "demo", "asdf", "abcd", "1234").Where({ $low.Contains($_) }).Count -eq 0) { return $mixed }
    }
}

function Test-AppPassword([string] $Password) {
    if ($Password.Length -lt 12) { return "at least 12 characters" }
    if ($Password -cnotmatch "[A-Z]") { return "an upper-case letter" }
    if ($Password -cnotmatch "[a-z]") { return "a lower-case letter" }
    if ($Password -notmatch "\d") { return "a digit" }
    if ($Password -notmatch "[^A-Za-z0-9]") { return "a symbol" }
    foreach ($bad in @("password", "passw0rd", "admin", "welcome", "qwerty", "qwe123", "asdf", "zxcv", "letmein", "iloveyou", "monkey", "dragon", "changeme", "inventory", "company", "assets", "123456", "1234", "abcd")) {
        if ($Password.ToLowerInvariant().Contains($bad)) { return "no common word such as '$bad'" }
    }
    return $null
}

$json = Get-Content $file -Raw | ConvertFrom-Json
if (-not $json.PSObject.Properties["Tenancy"]) { throw "The settings file has no Tenancy section: this site was not installed with Install-SaaS.ps1." }
$tenancy = $json.Tenancy
function Get-Setting([string] $Name) { if ($tenancy.PSObject.Properties[$Name]) { return [string]$tenancy.$Name } else { return "" } }
function Set-Setting([string] $Name, $Value) { $tenancy | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force }

$shown = [ordered]@{}
if ($NewOperatorKey -or [string]::IsNullOrWhiteSpace((Get-Setting "OperatorKey"))) {
    $key = New-RandomText 32 "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789"
    Set-Setting "OperatorKey" $key
    $shown["Operator key"] = $key
}
if ($DemoPassword) {
    $missing = Test-AppPassword $DemoPassword
    if ($missing) { throw "That demo password needs $missing." }
    Set-Setting "DemoAdminPassword" $DemoPassword
    $shown["Demo administrator password (user: admin)"] = $DemoPassword
} elseif ($NewDemoPassword -or [string]::IsNullOrWhiteSpace((Get-Setting "DemoAdminPassword"))) {
    $pw = New-StrongPassword
    Set-Setting "DemoAdminPassword" $pw
    $shown["Demo administrator password (user: admin)"] = $pw
}
Set-Setting "DemoEnabled" $true
if ($SetupBackups) {
    Initialize-BackupFolder $BackupFolder $SqlServer "IIS AppPool\$AppPoolName"
    Set-Setting "BackupFolder" $BackupFolder
    $shown["Backup folder (set up)"] = $BackupFolder
}

[IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
& icacls $file /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" "IIS AppPool\${AppPoolName}:R" | Out-Null

if (-not $NoRestart) { Restart-WebAppPool -Name $AppPoolName }

Write-Host ""
if ($shown.Count -eq 0) {
    Write-Host "Nothing was changed: an operator key and a demo password are already set. Use -NewOperatorKey or -NewDemoPassword to replace them." -ForegroundColor Yellow
} else {
    Write-Host "SAVE THESE NOW (shown once; they are stored in $file for the site only):" -ForegroundColor Yellow
    foreach ($k in $shown.Keys) { Write-Host ("  {0}: {1}" -f $k, $shown[$k]) -ForegroundColor Yellow }
}
Write-Host ""
Write-Host "Operator page: open  https://<your host>[:port]/Tenant/Admin  and enter the operator key." -ForegroundColor Green
Write-Host "The demo company is created about a minute after the site starts, or use 'Create demo company now' on the operator page." -ForegroundColor Green
