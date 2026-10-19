# Shared password helpers for AMGH IT Inventory deployment scripts (dot-sourced).
Set-StrictMode -Version Latest

function ConvertTo-PlainText([Security.SecureString] $Secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Test-PasswordPolicy([string] $Password, [int] $MinLength = 12) {
    $errors = @()
    if ($Password.Length -lt $MinLength)        { $errors += "at least $MinLength characters" }
    if ($Password -cnotmatch '[A-Z]')           { $errors += "an upper-case letter" }
    if ($Password -cnotmatch '[a-z]')           { $errors += "a lower-case letter" }
    if ($Password -notmatch '\d')               { $errors += "a digit" }
    if ($Password -notmatch '[^A-Za-z0-9]')     { $errors += "a symbol" }
    if ($Password -match "['""]" -or $Password.Contains('$(')) { $errors += "no quote characters or '`$('" }
    return $errors
}

function Read-ValidatedPassword([string] $Prompt) {
    while ($true) {
        $p1 = ConvertTo-PlainText (Read-Host -Prompt $Prompt -AsSecureString)
        $problems = @(Test-PasswordPolicy $p1)
        if ($problems.Count -gt 0) { Write-Host ("Password must contain: " + ($problems -join ", ")) -ForegroundColor Yellow; continue }
        $p2 = ConvertTo-PlainText (Read-Host -Prompt "Confirm" -AsSecureString)
        if ($p1 -cne $p2) { Write-Host "Passwords do not match." -ForegroundColor Yellow; continue }
        return $p1
    }
}

function Get-BigEndianBytes([uint32] $Value) {
    $b = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($b) }
    return ,$b
}

<# ASP.NET Core Identity PasswordHasher V3 format:
   0x01 | PRF (uint32 BE, 2 = HMACSHA512) | iterations (uint32 BE) | salt length (uint32 BE) | salt | subkey #>
function New-IdentityV3PasswordHash([string] $Password) {
    $iterations = 100000
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $pwdBytes = [Text.Encoding]::UTF8.GetBytes($Password)
    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($pwdBytes, $salt, $iterations, [Security.Cryptography.HashAlgorithmName]::SHA512)
    try { $subkey = $kdf.GetBytes(32) } finally { $kdf.Dispose() }

    $ms = New-Object IO.MemoryStream
    $ms.WriteByte(0x01)
    foreach ($part in @((Get-BigEndianBytes 2), (Get-BigEndianBytes $iterations), (Get-BigEndianBytes 16), $salt, $subkey)) {
        $ms.Write($part, 0, $part.Length)
    }
    return [Convert]::ToBase64String($ms.ToArray())
}

