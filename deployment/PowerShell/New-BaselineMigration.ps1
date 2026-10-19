<#
.SYNOPSIS
    Creates an EF Core migration that records a schema change already applied by the SQL scripts
    ("Baseline" the first time; e.g. "AddLabelDesign" for later releases).

.DESCRIPTION
    The schema is created by the SQL scripts, so the baseline migration must NOT create anything.
    This script:
      1. installs the dotnet-ef tool (8.0.10) if missing;
      2. builds the solution;
      3. generates the Baseline migration and model snapshot;
      4. empties Baseline.Up/Down (the tables already exist);
      5. applies it, which only records "Baseline" in dbo.__EFMigrationsHistory;
      6. verifies that the EF model has no pending changes.
    Run once, from the build/development machine, as a user with rights to the database.

.EXAMPLE
    .\New-BaselineMigration.ps1
    .\New-BaselineMigration.ps1 -Name AddLabelDesign
    .\New-BaselineMigration.ps1 -ConnectionString "Server=localhost\SQL2022;Database=AMGH_ITInventory;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z][A-Za-z0-9]{2,60}$')]
    [string] $Name = "Baseline",
    [string] $ConnectionString = "Server=localhost;Database=AMGH_ITInventory;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$solution     = Join-Path $repoRoot "AMGH.ITInventory.sln"
$project      = Join-Path $repoRoot "src\AMGH.ITInventory.Persistence\AMGH.ITInventory.Persistence.csproj"
$migrationDir = Join-Path $repoRoot "src\AMGH.ITInventory.Persistence\Migrations"

function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    Write-Host "> $Exe $($Arguments -join ' ')" -ForegroundColor Cyan
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"   # native stderr must not abort; the exit code decides
    try {
        & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host "  $_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) { throw "'$Exe $($Arguments -join ' ')' failed with exit code $code." }
}

# ---------------------------------------------------------------- checks
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
}
if (-not (Test-Path $project)) { throw "Persistence project not found at $project" }
if ((Test-Path $migrationDir) -and (Get-ChildItem $migrationDir -Filter "*_$Name.cs" -ErrorAction SilentlyContinue)) {
    throw "A '$Name' migration already exists in $migrationDir. Nothing to do."
}
if ($Name -ne "Baseline" -and -not ((Test-Path $migrationDir) -and (Get-ChildItem $migrationDir -Filter "*_Baseline.cs" -ErrorAction SilentlyContinue))) {
    throw "Create the Baseline migration first (run this script without -Name), or copy your Migrations folder from the previous release."
}

# ---------------------------------------------------------------- dotnet-ef tool
$toolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"
if ($env:PATH -notlike "*$toolsPath*") { $env:PATH = "$env:PATH;$toolsPath" }

$previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$tools = (& dotnet tool list --global 2>&1 | Out-String)
$ErrorActionPreference = $previous
if ($tools -notmatch "dotnet-ef") {
    Invoke-Native "dotnet" @("tool", "install", "--global", "dotnet-ef", "--version", "8.0.10")
}

# ---------------------------------------------------------------- build + migration
$env:AMGH_ConnectionString = $ConnectionString
try {
    Invoke-Native "dotnet" @("build", $solution, "-c", "Debug")

    Invoke-Native "dotnet" @("ef", "migrations", "add", $Name,
        "--project", $project, "--startup-project", $project, "--output-dir", "Migrations", "--no-build")

    $baseline = Get-ChildItem $migrationDir -Filter "*_$Name.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -First 1
    if (-not $baseline) { throw "Migration file was not generated." }

    $emptyMigration = @'
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMGH.ITInventory.Persistence.Migrations
{
    /// <summary>
    /// The schema change was applied by deployment/SQLScripts (idempotent scripts); this migration
    /// only records the model in EF Core's snapshot. Do not add operations here.
    /// </summary>
    public partial class __NAME__ : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
'@
    Set-Content -Path $baseline.FullName -Value $emptyMigration.Replace("__NAME__", $Name) -Encoding UTF8
    Write-Host "Emptied $($baseline.Name) (tables already exist)." -ForegroundColor Green

    Invoke-Native "dotnet" @("build", $project, "-c", "Debug")
    Invoke-Native "dotnet" @("ef", "database", "update", "--project", $project, "--startup-project", $project, "--no-build")
    Invoke-Native "dotnet" @("ef", "migrations", "has-pending-model-changes", "--project", $project, "--startup-project", $project, "--no-build")

    Write-Host ""
    Write-Host "Migration '$Name' created and recorded in dbo.__EFMigrationsHistory." -ForegroundColor Green
    Write-Host "Commit the src\AMGH.ITInventory.Persistence\Migrations folder to source control." -ForegroundColor Green
}
finally {
    Remove-Item Env:\AMGH_ConnectionString -ErrorAction SilentlyContinue
}
