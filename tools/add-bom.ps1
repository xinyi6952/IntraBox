# 给所有含非 ASCII（中文等）且无 BOM 的 .cs/.xaml 源码加 UTF-8 BOM
# 目的：让 csc 在 GBK 代码页（Win7 内网机等）下也能正确解析 UTF-8 中文。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/add-bom.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$count = 0
Get-ChildItem -Recurse -Path "$root\src", "$root\tests" -Include "*.cs", "*.xaml" |
    Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" } |
    ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $hasNonAscii = $false
        foreach ($b in $bytes) { if ($b -gt 127) { $hasNonAscii = $true; break } }
        if ($hasNonAscii -and -not $hasBom) {
            $newBytes = New-Object byte[] ($bytes.Length + 3)
            $newBytes[0] = 0xEF; $newBytes[1] = 0xBB; $newBytes[2] = 0xBF
            [Array]::Copy($bytes, 0, $newBytes, 3, $bytes.Length)
            [System.IO.File]::WriteAllBytes($_.FullName, $newBytes)
            $count++
        }
    }

Write-Host "已给 $count 个文件加 UTF-8 BOM"
