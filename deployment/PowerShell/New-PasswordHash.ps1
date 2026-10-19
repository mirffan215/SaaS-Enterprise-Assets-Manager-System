<#
.SYNOPSIS
    Prompts for a password and prints its ASP.NET Core Identity v3 hash
    (PBKDF2-HMAC-SHA512, 100,000 iterations) for manual seeding or emergency resets.
.EXAMPLE
    .\New-PasswordHash.ps1 | Set-Clipboard
#>
[CmdletBinding()] param()
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "PasswordFunctions.ps1")
$plain = Read-ValidatedPassword "Password to hash"
try { New-IdentityV3PasswordHash $plain } finally { Remove-Variable plain }
