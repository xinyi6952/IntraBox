#Requires -Version 5.1
# Pack version: bump the last segment of repo-root version.txt, then patch AssemblyInfo.
# stdout: the new four-part version (for build.bat). Host messages go to stderr via Write-Host.
# UTF-8 BOM.
param(
    [switch]$SyncOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$versionPath = Join-Path $root "version.txt"
$assemblyPath = Join-Path $root "src\IntraBox\Properties\AssemblyInfo.cs"

function Parse-FourPart([string]$text) {
    $t = ($text | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($t)) { throw "version.txt is empty" }
    $parts = $t.Split('.')
    if ($parts.Length -lt 2 -or $parts.Length -gt 4) { throw "version.txt must be 2-4 numeric parts, got: $t" }
    $nums = @(0, 0, 0, 0)
    for ($i = 0; $i -lt $parts.Length; $i++) {
        $n = 0
        if (-not [int]::TryParse($parts[$i], [ref]$n) -or $n -lt 0 -or $n -gt 65535) {
            throw "version.txt part is not 0-65535: $($parts[$i])"
        }
        $nums[$i] = $n
    }
    return ,$nums
}

function Format-Full($nums) {
    return "{0}.{1}.{2}.{3}" -f $nums[0], $nums[1], $nums[2], $nums[3]
}

function Format-Display($nums) {
    if ($nums[2] -eq 0) { return "{0}.{1}.{2}" -f $nums[0], $nums[1], $nums[3] }
    return Format-Full $nums
}

function Bump-Pack($nums) {
    $a = @([int]$nums[0], [int]$nums[1], [int]$nums[2], [int]$nums[3])
    if ($a[3] -lt 65535) { $a[3] = $a[3] + 1 }
    elseif ($a[2] -lt 65535) { $a[2] = $a[2] + 1; $a[3] = 0 }
    elseif ($a[1] -lt 65535) { $a[1] = $a[1] + 1; $a[2] = 0; $a[3] = 0 }
    else { $a[0] = $a[0] + 1; $a[1] = 0; $a[2] = 0; $a[3] = 0 }
    return ,$a
}

if (-not (Test-Path $versionPath)) {
    [System.IO.File]::WriteAllText($versionPath, "1.0.0.0`r`n", (New-Object System.Text.UTF8Encoding $false))
}

$nums = Parse-FourPart ([System.IO.File]::ReadAllText($versionPath))
if (-not $SyncOnly) { $nums = Bump-Pack $nums }
$full = Format-Full $nums
$display = Format-Display $nums

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($versionPath, $full + "`r`n", $utf8NoBom)

if (-not (Test-Path $assemblyPath)) { throw "AssemblyInfo.cs not found: $assemblyPath" }
$asm = [System.IO.File]::ReadAllText($assemblyPath)
$asm2 = [regex]::Replace($asm, '\[assembly:\s*AssemblyVersion\("[^"]*"\)\]', "[assembly: AssemblyVersion(`"$full`")]")
$asm2 = [regex]::Replace($asm2, '\[assembly:\s*AssemblyFileVersion\("[^"]*"\)\]', "[assembly: AssemblyFileVersion(`"$full`")]")
if ($asm2 -match 'AssemblyInformationalVersion') {
    $asm2 = [regex]::Replace($asm2, '\[assembly:\s*AssemblyInformationalVersion\("[^"]*"\)\]', "[assembly: AssemblyInformationalVersion(`"$display`")]")
} else {
    $asm2 = [regex]::Replace($asm2, '(\[assembly:\s*AssemblyFileVersion\("[^"]*"\)\])', "`$1`r`n[assembly: AssemblyInformationalVersion(`"$display`")]")
}
if ($asm2 -eq $asm -and $asm -notmatch [regex]::Escape($full)) {
    throw "Failed to patch AssemblyVersion in AssemblyInfo.cs"
}
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($assemblyPath, $asm2, $utf8Bom)

Write-Host ("Pack version {0} (display {1})" -f $full, $display)
Write-Output $full
