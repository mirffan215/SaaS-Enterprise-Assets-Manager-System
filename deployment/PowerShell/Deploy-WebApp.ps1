<#
.SYNOPSIS
    Publishes the AMGH IT Inventory web application to IIS on this server (HTTPS only).

.DESCRIPTION
      1. checks prerequisites (administrator, IIS, .NET 8 Hosting Bundle, .NET 8 SDK);
      2. publishes the Web project (Release);
      3. takes the site offline (app_offline.htm), copies the files, keeps any existing
         appsettings.Production.json, brings the site back online;
      4. creates the log and key folders and grants the app pool identity access;
      5. configures the app pool (No Managed Code) and the site with an HTTPS binding
         (existing certificate, or a new self-signed one for testing).

    Re-run it to deploy a new version: settings and certificate bindings are kept.

.EXAMPLE
    .\Deploy-WebApp.ps1
    .\Deploy-WebApp.ps1 -HostName "itinventory.amgh.local" -CertificateThumbprint "AB12...EF"
#>
[CmdletBinding()]
param(
    [string] $SiteName = "AMGHITInventory",
    [string] $AppPoolName = "AMGHITInventory",
    [string] $SitePath = "C:\inetpub\AMGHITInventory",
    [string] $HostName = $env:COMPUTERNAME.ToLowerInvariant(),
    [int]    $Port = 443,
    [string] $CertificateThumbprint,
    [string] $LogsPath = "C:\AMGH\Logs",
    [string] $KeysPath = "C:\AMGH\Keys",
    [string] $DocumentsPath = "C:\AMGH\Documents",
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project    = Join-Path $repoRoot "src\AMGH.ITInventory.Web\AMGH.ITInventory.Web.csproj"
$publishDir = Join-Path $repoRoot "publish\Web"

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"   # native stderr must not abort; the exit code decides
    try {
        & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host "  $_" }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "'$Exe $($Arguments -join ' ')' failed with exit code $code." }
}

# ---------------------------------------------------------------- prerequisites
Write-Step "Checking prerequisites"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell (Run as administrator)."
}
if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
    throw "IIS is not installed. Run: Install-WindowsFeature Web-Server, Web-Mgmt-Console"
}
if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2")) {
    throw "The ASP.NET Core Hosting Bundle is not installed. Install the '.NET 8 Hosting Bundle' from https://dotnet.microsoft.com/download/dotnet/8.0, then run 'iisreset'."
}
Import-Module WebAdministration

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Write-Step "Publishing the web application"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET 8 SDK not found (needed to publish)." }
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Native "dotnet" @("publish", $project, "-c", "Release", "-o", $publishDir, "--nologo")
}
if (-not (Test-Path (Join-Path $publishDir "AMGH.ITInventory.Web.dll"))) { throw "Published files not found in $publishDir." }

# ---------------------------------------------------------------- folders
Write-Step "Preparing folders"
foreach ($dir in @($SitePath, $LogsPath, $KeysPath, $DocumentsPath)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

# ---------------------------------------------------------------- app pool
Write-Step "Configuring application pool '$AppPoolName'"
$poolPath = "IIS:\AppPools\$AppPoolName"
if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $AppPoolName | Out-Null }
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""
Set-ItemProperty $poolPath -Name processModel.identityType -Value "ApplicationPoolIdentity"
# The background notification sender must keep running: no idle shut-down, start with IIS (Release 2.5)
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name startMode -Value "AlwaysRunning"
Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true   # needed for DPAPI key protection

$poolIdentity = "IIS AppPool\$AppPoolName"
Write-Step "Granting $poolIdentity access to the site, log, key and document folders"
Invoke-Native "icacls" @($SitePath, "/grant", "${poolIdentity}:(OI)(CI)RX", "/T", "/Q")
Invoke-Native "icacls" @($LogsPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")
Invoke-Native "icacls" @($KeysPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")
Invoke-Native "icacls" @($DocumentsPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")

# ---------------------------------------------------------------- copy files (site offline meanwhile)
Write-Step "Deploying files to $SitePath"
$offline = Join-Path $SitePath "app_offline.htm"
Set-Content -Path $offline -Value "<html><body><h1>AMGH IT Inventory is being updated</h1><p>Please try again in a minute.</p></body></html>"
Start-Sleep -Seconds 3

$prodSettings = Join-Path $SitePath "appsettings.Production.json"
$backup = $null
if (Test-Path $prodSettings) { $backup = Get-Content $prodSettings -Raw }

# /MIR keeps the folder identical to the publish output; app_offline and production settings are excluded.
& robocopy $publishDir $SitePath /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm appsettings.Production.json | Out-Null
if ($LASTEXITCODE -ge 8) { throw "File copy failed (robocopy exit code $LASTEXITCODE)." }
if ($null -ne $backup) { Set-Content -Path $prodSettings -Value $backup -Encoding UTF8 }

# ---------------------------------------------------------------- certificate
Write-Step "Configuring site '$SiteName' on https://${HostName}:$Port"
if (-not $CertificateThumbprint) {
    $existing = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq "CN=$HostName" -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($existing) {
        $CertificateThumbprint = $existing.Thumbprint
    } else {
        Write-Host "  No certificate supplied - creating a self-signed certificate for $HostName (testing only)." -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate -DnsName $HostName, "localhost" -CertStoreLocation Cert:\LocalMachine\My `
            -NotAfter (Get-Date).AddYears(2) -KeyExportPolicy NonExportable -FriendlyName "AMGH IT Inventory (self-signed)"
        $CertificateThumbprint = $cert.Thumbprint
        # Trust it on this server so the local browser accepts it.
        $root = New-Object Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
        $root.Open("ReadWrite")
        $root.Add($cert)
        $root.Close()
    }
}

# ---------------------------------------------------------------- site + HTTPS binding
if (-not (Get-Website -Name $SiteName)) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName `
        -Port $Port -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

# Preload the site so the background notification sender starts with IIS (new and existing sites)
try { Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true }
catch { Write-Warning "Could not enable preload (install the IIS 'Application Initialization' feature); the sender starts with the first request instead." }

# HTTPS only: remove any plain HTTP binding on this site.
Get-WebBinding -Name $SiteName -Protocol "http" -ErrorAction SilentlyContinue | Remove-WebBinding

$binding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostName
if (-not $binding) {
    New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostName -SslFlags 1
    $binding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostName
}
$binding.AddSslCertificate($CertificateThumbprint, "My")

# ---------------------------------------------------------------- start
Remove-Item $offline -Force -ErrorAction SilentlyContinue
if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne "Started") { Start-WebAppPool -Name $AppPoolName }
if ((Get-WebsiteState -Name $SiteName).Value -ne "Started") { Start-Website -Name $SiteName }

Write-Step "Checking the application responds"
Start-Sleep -Seconds 5
try {
    $response = Invoke-WebRequest -Uri "https://${HostName}:$Port/health" -UseBasicParsing -TimeoutSec 60
    Write-Host "  Health check: $($response.StatusCode) $($response.Content)" -ForegroundColor Green
} catch {
    Write-Host "  Health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  See $LogsPath and the Windows Application event log." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Deployed. Open https://${HostName}:$Port and sign in as the administrator." -ForegroundColor Green
