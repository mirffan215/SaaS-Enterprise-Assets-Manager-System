<#
.SYNOPSIS
    Release 2.0: moves existing uploaded documents into the folder of company 1 (AMGH).

.DESCRIPTION
    From Release 2.0 each company's files live in <RootPath>\<CompanyId>\<area>\ (for example
    C:\AMGH\Documents\1\assets\). This script moves the v1.x folders (assets, _imports) from the root
    into <RootPath>\1\. File names are unchanged, so the database records keep working.

    Safe to run more than once: folders already moved are skipped. Use -WhatIf to preview.
    Run it in the go-live window, after the database migration and before starting the new application.

.EXAMPLE
    .\Move-DocumentsToCompanyFolders.ps1 -WhatIf
    .\Move-DocumentsToCompanyFolders.ps1
    .\Move-DocumentsToCompanyFolders.ps1 -Rollback      # moves them back to the v1.x layout
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $RootPath = "C:\AMGH\Documents",
    [int] $CompanyId = 1,
    [switch] $Rollback
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path $RootPath)) { throw "Documents folder not found: $RootPath" }
$companyFolder = Join-Path $RootPath $CompanyId
$areas = @("assets", "_imports")
$moved = 0

foreach ($area in $areas) {
    $flat = Join-Path $RootPath $area
    $scoped = Join-Path $companyFolder $area
    $from, $to = if ($Rollback) { $scoped, $flat } else { $flat, $scoped }

    if (-not (Test-Path $from)) { Write-Host "  $area : nothing to move ($from not found)"; continue }
    if (-not (Test-Path (Split-Path -Parent $to))) {
        if ($PSCmdlet.ShouldProcess((Split-Path -Parent $to), "Create folder")) { New-Item -ItemType Directory -Path (Split-Path -Parent $to) | Out-Null }
    }

    $files = @(Get-ChildItem -Path $from -File -Recurse)
    if (Test-Path $to) {
        # Target exists (partly moved earlier): move file by file, never overwrite.
        foreach ($f in $files) {
            $dest = Join-Path $to $f.Name
            if (Test-Path $dest) { Write-Warning "Skipped (already present): $dest"; continue }
            if ($PSCmdlet.ShouldProcess($f.FullName, "Move to $dest")) { Move-Item -LiteralPath $f.FullName -Destination $dest; $moved++ }
        }
        if (-not (Get-ChildItem -Path $from -Force) -and $PSCmdlet.ShouldProcess($from, "Remove empty folder")) { Remove-Item $from }
    }
    elseif ($PSCmdlet.ShouldProcess($from, "Move folder to $to")) {
        Move-Item -LiteralPath $from -Destination $to
        $moved += $files.Count
    }
    Write-Host ("  {0,-9}: {1} file(s) -> {2}" -f $area, $files.Count, $to) -ForegroundColor Green
}

# Count check: every document is now in exactly one place.
$check = if ($Rollback) { $RootPath } else { $companyFolder }
$total = @(Get-ChildItem -Path $check -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "*.partial" }).Count
Write-Host "Done. $moved file(s) moved; $total file(s) now under $check." -ForegroundColor Cyan
Write-Host "Folder permissions are inherited from $RootPath (the application pool already has access)."
