<#
.SYNOPSIS
    First-time, side-by-side installation of the SaaS Enterprise Assets Manager on a Windows server (IIS + SQL Server 2022).

.DESCRIPTION
    Installs a NEW, separate copy: its own IIS site and application pool, folders, catalog database, SQL logins and cookies.
    It never changes an existing installation (another IIS site, its database or its folders). If the site, pool or
    folders it would create already exist, it stops and tells you; use -Update to deploy a new version of THIS installation.

    The script asks:
      * which port to use: the default HTTPS port (443) or a custom port;
      * how people reach their company: a company-code page (works everywhere), or one sub-domain per company;
      * whether to create the demo company with sample data.

    What it does, in order:
      1. checks prerequisites (administrator, IIS, .NET 8 Hosting Bundle, .NET 8 SDK, sqlcmd) and that the port and names are free;
      2. publishes the web application (Release) and copies it to the site folder;
      3. creates the application pool (no idle time-out, no managed code) and the data folders (logs, keys, documents);
      4. prepares SQL Server: the catalog database, a provisioning login (create databases only) and the application identity;
      5. writes appsettings.Production.json (secrets are generated here, shown once, and readable only by Administrators
         and the application pool);
      6. binds HTTPS (your certificate, or a self-signed one for testing), optionally opens the firewall port, starts the site
         and checks /health.
    Customer databases are created later by the application itself (tenant provisioning, or the demo company at first start).

.EXAMPLE
    .\Install-SaaS.ps1
    .\Install-SaaS.ps1 -SqlServer "SQLSRV01\MSSQL2022" -PortMode Custom -Port 8443 -HostName eams.contoso.local -CreateDemo
.NOTES
    Run from an elevated Windows PowerShell 5.1+ as a SQL Server sysadmin. SQL Server must allow SQL logins (mixed mode)
    because the provisioning login uses a password; the application itself signs in to SQL Server as its pool identity.
#>
[CmdletBinding()]
param(
    [string] $SiteName = "EAMSCloud",
    [string] $AppPoolName = "EAMSCloud",
    [string] $SitePath = "C:\inetpub\EAMSCloud",
    [string] $DataRoot = "C:\EAMSCloud",
    [string] $SqlServer = "localhost",
    [string] $CatalogDatabase = "ITInv_Catalog",
    [string] $ProvisionerLogin = "eams_provisioner",
    [string] $HostName,
    [ValidateSet("Default", "Custom")] [string] $PortMode,
    [int]    $Port,
    [ValidateSet("CompanyCode", "Subdomain")] [string] $TenantMode,
    [string] $BaseDomain,
    [string] $CertificateThumbprint,
    [switch] $CreateDemo,
    [switch] $NoDemo,
    # let anyone who can reach the site register a company (page /Register); otherwise only the operator creates companies
    [switch] $AllowSelfRegistration,
    [switch] $OpenFirewall,
    [switch] $SkipPublish,
    # deploy a new version of THIS installation (keeps appsettings.Production.json, the databases and the certificate)
    [switch] $Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project    = Join-Path $repoRoot "src\AMGH.ITInventory.Web\AMGH.ITInventory.Web.csproj"
$publishDir = Join-Path $repoRoot "publish\SaaSWeb"
$sqlSetup   = Join-Path $repoRoot "deployment\SQLScripts\Catalog\C00_SetupAccess.sql"
$logFile    = Join-Path $PSScriptRoot ("Install-SaaS_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan; Add-Content -Path $logFile -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $Text) }
function Write-Note([string] $Text, [string] $Color = "Gray") { Write-Host "  $Text" -ForegroundColor $Color }

function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"   # native stderr must not abort; the exit code decides
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host "  $_" }; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "'$Exe' failed with exit code $code." }
}

function Read-Choice([string] $Question, [string[]] $Options, [int] $Default = 1) {
    Write-Host ""
    Write-Host $Question -ForegroundColor White
    for ($i = 0; $i -lt $Options.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $Options[$i]) }
    while ($true) {
        $answer = Read-Host ("Choose 1-{0} (Enter = {1})" -f $Options.Count, $Default)
        if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
        $n = 0
        if ([int]::TryParse($answer, [ref]$n) -and $n -ge 1 -and $n -le $Options.Count) { return $n }
        Write-Host "  Please enter a number from the list." -ForegroundColor Yellow
    }
}

function Read-YesNo([string] $Question, [bool] $Default) {
    $suffix = if ($Default) { "[Y/n]" } else { "[y/N]" }
    while ($true) {
        $answer = (Read-Host "$Question $suffix").Trim().ToLowerInvariant()
        if ($answer -eq "") { return $Default }
        if ($answer -in @("y", "yes")) { return $true }
        if ($answer -in @("n", "no")) { return $false }
    }
}

# letters and digits only: safe inside connection strings, JSON and T-SQL string literals
function New-Secret([int] $Length = 32) {
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789".ToCharArray()
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] ($Length * 4)
        $rng.GetBytes($bytes)
        $chars = for ($i = 0; $i -lt $Length; $i++) { $alphabet[[BitConverter]::ToUInt32($bytes, $i * 4) % $alphabet.Length] }
        return (-join $chars)
    } finally { $rng.Dispose() }
}

function Read-SecurePasswordText([string] $Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# The password rules of the application (12+ characters, upper, lower, digit, symbol; no easy words).
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

# DNS names of a certificate. Windows PowerShell versions and certificates differ in what DnsNameList holds
# (objects with a Unicode property, plain strings, or nothing), so every shape is accepted and errors are ignored.
function Get-CertificateDnsNames($Certificate) {
    $names = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($entry in @($Certificate.DnsNameList)) {
            if ($null -eq $entry) { continue }
            if ($entry -is [string]) { $names.Add($entry) }
            elseif ($entry.PSObject.Properties["Unicode"]) { $names.Add([string]$entry.Unicode) }
            else { $names.Add([string]$entry) }
        }
    } catch { }
    try { $cn = $Certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false); if ($cn) { $names.Add($cn) } } catch { }
    return $names.ToArray()
}

# Backup folder for company backups: the SQL Server service writes the backup files, the application pool may only list them.
function Initialize-BackupFolder([string] $Folder, [string] $SqlServer, [string] $PoolIdentity) {
    if (-not (Test-Path $Folder)) { New-Item -ItemType Directory -Path $Folder | Out-Null }
    $parts = @($SqlServer -split '\\')
    $local = $parts[0] -in @("localhost", ".", "(local)", $env:COMPUTERNAME)
    if (-not $local) { Write-Warning "SQL Server is on another machine: give its service account write access to $Folder (a share both can reach) yourself."; return }
    $serviceName = if ($parts.Count -gt 1) { 'MSSQL$' + $parts[1] } else { "MSSQLSERVER" }
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.StartName) { & icacls $Folder /grant "$($service.StartName):(OI)(CI)M" /Q | Out-Null }
    else { Write-Warning "Could not find the SQL Server service '$serviceName': give its account Modify rights on $Folder yourself." }
    & icacls $Folder /grant "${PoolIdentity}:(CI)(RD,RA,REA)" /Q | Out-Null   # list the file names and sizes; never read the backups
}

# ---------------------------------------------------------------- 1. prerequisites
Write-Step "Checking prerequisites"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script from an elevated PowerShell (Run as administrator)." }
if (-not (Get-Module -ListAvailable -Name WebAdministration)) { throw "IIS is not installed. Run: Install-WindowsFeature Web-Server, Web-Mgmt-Console" }
if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2")) {
    throw "The .NET 8 Hosting Bundle is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0, run 'iisreset', then start again."
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "sqlcmd was not found. Install the SQL Server command-line utilities." }
if (-not $SkipPublish -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET 8 SDK not found (it is installed with Visual Studio 2022; needed to publish)." }
Import-Module WebAdministration

# The new installation must not touch an existing one.
if (-not $Update) {
    if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) { throw "An IIS site named '$SiteName' already exists. Choose another -SiteName, or use -Update to deploy a new version of it." }
    if (Test-Path "IIS:\AppPools\$AppPoolName") { throw "An application pool named '$AppPoolName' already exists. Choose another -AppPoolName." }
    foreach ($p in @($SitePath, $DataRoot)) {
        if ((Test-Path $p) -and (Get-ChildItem $p -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) { throw "The folder '$p' is not empty. Choose another path so no existing installation is changed." }
    }
}

# ---------------------------------------------------------------- 2. questions
if (-not $HostName) {
    $default = $env:COMPUTERNAME.ToLowerInvariant()
    $entered = Read-Host "Host name people will type in the browser (Enter = $default)"
    $HostName = if ([string]::IsNullOrWhiteSpace($entered)) { $default } else { $entered.Trim().ToLowerInvariant() }
}

if (-not $PortMode) { $PortMode = if ((Read-Choice "Which port should the web site use?" @("Default HTTPS port (443)", "Custom port")) -eq 1) { "Default" } else { "Custom" } }
if ($PortMode -eq "Default") { $Port = 443 }

function Test-PortUsable([int] $Candidate, [string] $Host_) {
    if ($Candidate -lt 1 -or $Candidate -gt 65535) { return "A port is a number from 1 to 65535." }
    # something other than IIS (http.sys, process id 4) is listening on it
    $listener = Get-NetTCPConnection -State Listen -LocalPort $Candidate -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -ne 4 } | Select-Object -First 1
    if ($listener) {
        $proc = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue   # may have exited meanwhile; strict mode forbids reading a property of $null
        $name = if ($proc) { $proc.ProcessName } else { "another program" }
        return "Port $Candidate is already used by '$name'."
    }
    # another IIS site uses this port for the same host name, or for every host name (which cannot be shared with a host-name binding)
    foreach ($b in (Get-WebBinding -ErrorAction SilentlyContinue)) {
        $parts = $b.bindingInformation -split ":"
        if ($parts.Count -ge 3 -and $parts[1] -eq "$Candidate" -and ($parts[2] -eq $Host_ -or $parts[2] -eq "")) {
            $other = ($b.ItemXPath -split "'")[1]
            return "IIS site '$other' already uses port $Candidate for this host name or for all host names. Choose another port."
        }
    }
    return $null
}

while ($true) {
    if ($PortMode -eq "Custom" -and $Port -eq 0) {
        $text = Read-Host "Port number (for example 8443)"
        $n = 0
        if (-not [int]::TryParse($text, [ref]$n)) { Write-Host "  Enter a number." -ForegroundColor Yellow; continue }
        $Port = $n
    }
    $problem = if ($Update) { $null } else { Test-PortUsable $Port $HostName }
    if (-not $problem) { break }
    Write-Host "  $problem" -ForegroundColor Yellow
    if ($PortMode -eq "Default") { Write-Host "  Switching to a custom port." -ForegroundColor Yellow; $PortMode = "Custom" }
    $Port = 0
}
if ($Port -lt 1024 -and $Port -ne 443) { Write-Note "Port $Port is below 1024; make sure that is what you want." "Yellow" }

# updating an existing installation: take the company mode from its settings instead of asking again
if ($Update -and -not $TenantMode -and (Test-Path (Join-Path $SitePath "appsettings.Production.json"))) {
    $existingSettings = Get-Content (Join-Path $SitePath "appsettings.Production.json") -Raw | ConvertFrom-Json
    $existingBase = [string]$existingSettings.Tenancy.BaseDomain
    if ($existingBase) { $TenantMode = "Subdomain"; $BaseDomain = $existingBase } else { $TenantMode = "CompanyCode" }
}
if (-not $TenantMode) {
    $TenantMode = if ((Read-Choice "How do people reach their company?" @(
        "Company-code page: everyone uses the same address and types their company code (works everywhere)",
        "One sub-domain per company, e.g. acme.example.com (needs wildcard DNS and a wildcard certificate)")) -eq 1) { "CompanyCode" } else { "Subdomain" }
}
if ($TenantMode -eq "Subdomain" -and -not $BaseDomain) { $BaseDomain = (Read-Host "Base domain (for example example.com)").Trim().ToLowerInvariant() }
if ($TenantMode -eq "Subdomain" -and [string]::IsNullOrWhiteSpace($BaseDomain)) { throw "A base domain is required for sub-domain mode." }

$demoPassword = $null
if (-not $NoDemo -and -not $Update) {
    if (-not $CreateDemo) { $CreateDemo = [bool](Read-YesNo "Create the demo company with sample data (a sandbox people can explore)?" $true) }
    if ($CreateDemo) {
        while ($true) {
            $demoPassword = Read-SecurePasswordText "Password for the demo administrator (12+ characters, upper, lower, digit, symbol)"
            $missing = Test-AppPassword $demoPassword
            if (-not $missing) { break }
            Write-Host "  The password needs $missing." -ForegroundColor Yellow
        }
    }
}
$showDemoCredentials = $false
if (-not $Update -and -not $AllowSelfRegistration) { $AllowSelfRegistration = [bool](Read-YesNo "Let people register their own company on the sign-in page (otherwise only you create companies, with the operator key)?" $false) }
if ($CreateDemo -and -not $Update) { $showDemoCredentials = [bool](Read-YesNo "Show the demo user name and password on the demo sign-in page (public sandbox)?" $false) }

Write-Host ""
Write-Host "Summary" -ForegroundColor White
Write-Note "Site:        $SiteName  ->  https://${HostName}$(if ($Port -ne 443) { ':' + $Port })"
Write-Note "Folders:     $SitePath  and  $DataRoot"
Write-Note "SQL Server:  $SqlServer  (catalog database $CatalogDatabase; customer databases ITInv_Tenant_<code>)"
Write-Note "Companies:   $(if ($TenantMode -eq 'CompanyCode') { 'company-code page' } else { "sub-domains of $BaseDomain" })"
Write-Note "Demo:        $(if ($CreateDemo) { 'yes' } else { 'no' })"
if (-not (Read-YesNo "Continue?" $true)) { Write-Host "Nothing was changed."; return }

# ---------------------------------------------------------------- 3. publish
if (-not $SkipPublish) {
    Write-Step "Publishing the web application"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Native "dotnet" @("publish", $project, "-c", "Release", "-o", $publishDir, "--nologo")
}
if (-not (Test-Path (Join-Path $publishDir "AMGH.ITInventory.Web.dll"))) { throw "Published files not found in $publishDir." }
if (-not (Test-Path (Join-Path $publishDir "SQLScripts\02_Tables.sql"))) { throw "The published output has no SQLScripts folder; tenant provisioning would fail." }

# ---------------------------------------------------------------- 4. folders and application pool
Write-Step "Preparing folders and the application pool"
$logsPath = Join-Path $DataRoot "Logs"; $keysPath = Join-Path $DataRoot "Keys"; $docsPath = Join-Path $DataRoot "Documents"
foreach ($dir in @($SitePath, $logsPath, $keysPath, $docsPath)) { if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null } }

$poolPath = "IIS:\AppPools\$AppPoolName"
if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $AppPoolName | Out-Null }
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""
Set-ItemProperty $poolPath -Name processModel.identityType -Value "ApplicationPoolIdentity"
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name startMode -Value "AlwaysRunning"
Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
$poolIdentity = "IIS AppPool\$AppPoolName"
$backupsPath = Join-Path $DataRoot "Backups"
Initialize-BackupFolder $backupsPath $SqlServer $poolIdentity
Invoke-Native "icacls" @($logsPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")
Invoke-Native "icacls" @($keysPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")
Invoke-Native "icacls" @($docsPath, "/grant", "${poolIdentity}:(OI)(CI)M", "/T", "/Q")

# ---------------------------------------------------------------- 5. SQL Server and configuration (first installation only)
$settingsFile = Join-Path $SitePath "appsettings.Production.json"
$secretsToShow = $null
if (-not $Update -or -not (Test-Path $settingsFile)) {
    Write-Step "Preparing SQL Server ($SqlServer)"
    $provisionerPassword = New-Secret 32
    $masterKeyPassword   = New-Secret 32
    $operatorKey         = New-Secret 32
    $sqlArgs = @("-S", $SqlServer, "-E", "-b", "-I", "-C", "-i", $sqlSetup)
    $vars = @{ CatalogDatabase = $CatalogDatabase; ProvisionerLogin = $ProvisionerLogin; ProvisionerPassword = $provisionerPassword; AppIdentity = "IIS APPPOOL\$AppPoolName" }
    foreach ($k in $vars.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$vars[$k], "Process") }
    try { Invoke-Native "sqlcmd" $sqlArgs }
    finally { foreach ($k in $vars.Keys) { [Environment]::SetEnvironmentVariable($k, $null, "Process") } }

    Write-Step "Writing $settingsFile"
    $serverCs = "Server=$SqlServer;User Id=$ProvisionerLogin;Password=$provisionerPassword;Encrypt=True;TrustServerCertificate=True;Application Name=EAMS.Provisioning"
    $catalogCs = "Server=$SqlServer;Database=$CatalogDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=EAMS.Catalog"
    $template  = "Server=$SqlServer;Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=EAMS.Cloud"
    $tenancy = [ordered]@{
        Enabled = $true
        BaseDomain = $(if ($TenantMode -eq "Subdomain") { $BaseDomain } else { "" })
        AllowQueryParameter = ($TenantMode -eq "CompanyCode")
        ServerConnectionString = $serverCs
        TenantConnectionTemplate = $template
        ScriptsPath = "SQLScripts"
        BackupFolder = $backupsPath
        AppLogin = "IIS APPPOOL\$AppPoolName"
        MasterKeyPassword = $masterKeyPassword
        OperatorKey = $operatorKey
        AllowSelfRegistration = [bool]$AllowSelfRegistration
        DemoEnabled = [bool]$CreateDemo
        DemoAdminPassword = $(if ($CreateDemo) { $demoPassword } else { "" })
        ShowDemoCredentials = [bool]$showDemoCredentials
    }
    $settings = [ordered]@{
        ConnectionStrings = [ordered]@{ TenantCatalog = $catalogCs }
        Tenancy = $tenancy
        Storage = [ordered]@{ RootPath = $docsPath }
        DataProtection = [ordered]@{ KeysPath = $keysPath }
        Serilog = [ordered]@{ WriteTo = @([ordered]@{ Name = "File"; Args = [ordered]@{ path = (Join-Path $logsPath "eams-.log") } }, [ordered]@{ Name = "Console" }) }
    }
    $json = $settings | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($settingsFile, $json, (New-Object Text.UTF8Encoding($false)))
    # only Administrators, SYSTEM and the application pool may read it (it holds secrets)
    Invoke-Native "icacls" @($settingsFile, "/inheritance:r", "/grant:r", "Administrators:F", "SYSTEM:F", "${poolIdentity}:R")
    $secretsToShow = @{ MasterKey = $masterKeyPassword; ProvisionerLogin = $ProvisionerLogin; OperatorKey = $operatorKey }
}

# ---------------------------------------------------------------- 6. files (site offline meanwhile)
Write-Step "Deploying files to $SitePath"
$offline = Join-Path $SitePath "app_offline.htm"
Set-Content -Path $offline -Value "<html><body><h1>The application is being installed</h1><p>Please try again in a minute.</p></body></html>"
Start-Sleep -Seconds 2
# the offline page must never be left behind: remove it even when a later step fails
try {
    & robocopy $publishDir $SitePath /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm appsettings.Production.json | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "File copy failed (robocopy exit code $LASTEXITCODE)." }
    Invoke-Native "icacls" @($SitePath, "/grant", "${poolIdentity}:(OI)(CI)RX", "/T", "/Q")

    # ---------------------------------------------------------------- 7. certificate, site and bindings
    Write-Step "Configuring site '$SiteName' on https://${HostName}:$Port"
    if (-not $CertificateThumbprint) {
        $wanted = if ($TenantMode -eq "Subdomain") { "*.$BaseDomain" } else { $HostName }
        $existing = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
                (Get-CertificateDnsNames $_) -contains $wanted -or $_.Subject -eq "CN=$wanted"
            } | Where-Object { $_.NotAfter -gt (Get-Date).AddDays(30) } | Sort-Object NotAfter -Descending | Select-Object -First 1
        if ($existing) { $CertificateThumbprint = $existing.Thumbprint }
        else {
            Write-Note "No certificate found for $wanted - creating a self-signed one (testing only; install a real certificate for production)." "Yellow"
            $names = @($HostName, "localhost"); if ($TenantMode -eq "Subdomain") { $names += @($BaseDomain, "*.$BaseDomain") }
            $cert = New-SelfSignedCertificate -DnsName $names -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(2) -KeyExportPolicy NonExportable -FriendlyName "EAMS Cloud (self-signed)"
            $CertificateThumbprint = $cert.Thumbprint
            $root = New-Object Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine"); $root.Open("ReadWrite"); $root.Add($cert); $root.Close()
        }
    }

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }
    try { Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true } catch { Write-Note "Install the IIS 'Application Initialization' feature so the notification sender starts with IIS." "Yellow" }
    Get-WebBinding -Name $SiteName -Protocol "http" -ErrorAction SilentlyContinue | Remove-WebBinding   # HTTPS only

    $hosts = @($HostName); if ($TenantMode -eq "Subdomain") { $hosts += @($BaseDomain, "*.$BaseDomain") }
    foreach ($h in ($hosts | Select-Object -Unique)) {
        $binding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $h
        if (-not $binding) { New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $h -SslFlags 1; $binding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $h }
        $binding.AddSslCertificate($CertificateThumbprint, "My")
    }

    if ($OpenFirewall -or ($Port -ne 443 -and (Read-YesNo "Open Windows Firewall for TCP port $Port?" $true))) {
        $ruleName = "EAMS Cloud HTTPS $Port"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null }
    }

}
finally {
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 8. start
if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne "Started") { Start-WebAppPool -Name $AppPoolName }
if ((Get-WebsiteState -Name $SiteName).Value -ne "Started") { Start-Website -Name $SiteName }

Write-Step "Checking the application responds"
Start-Sleep -Seconds 8
$baseUrl = "https://${HostName}$(if ($Port -ne 443) { ':' + $Port })"
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 90
    Write-Note "Health check: $($response.StatusCode) $($response.Content)" "Green"
} catch {
    Write-Note "Health check failed: $($_.Exception.Message)" "Yellow"
    Write-Note "See $logsPath and the Windows Application event log." "Yellow"
}

Write-Host ""
Write-Host "Installed. Open $baseUrl" -ForegroundColor Green
if ($CreateDemo) { Write-Note "The demo company is created in the background about a minute after the first start (company code: demo, user: admin)." }
if ($secretsToShow) {
    Write-Host ""
    Write-Host "KEEP THESE (shown once; they are also in $settingsFile, readable only by Administrators):" -ForegroundColor Yellow
    Write-Note "Database master-key password (needed to restore a customer database backup): $($secretsToShow.MasterKey)" "Yellow"
    Write-Note "Operator key for the page that creates companies ($baseUrl/Tenant/Admin): $($secretsToShow.OperatorKey)" "Yellow"
    Write-Note "SQL login for creating customer databases: $($secretsToShow.ProvisionerLogin) (password is in the settings file)" "Yellow"
    Write-Note "Back up the Keys folder ($keysPath) too: without it, signed-in sessions and stored secrets cannot be read after a rebuild." "Yellow"
}
