# 检查是否存在「含非 ASCII（中文）但无 BOM」的 .cs/.xaml 源码（应恒为 0）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-bom.ps1
# 退出码：0 = 全部合规；1 = 存在漏网文件（需运行 tools/add-bom.ps1）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bad = @()

Get-ChildItem -Recurse -Path "$root\src", "$root\tests" -Include "*.cs", "*.xaml" |
    Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" } |
    ForEach-Object {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $hasNonAscii = $false
        foreach ($b in $bytes) { if ($b -gt 127) { $hasNonAscii = $true; break } }
        if ($hasNonAscii -and -not $hasBom) { $bad += $_.FullName }
    }

if ($bad.Count -eq 0) {
    Write-Host "OK: 所有含中文的源码均带 UTF-8 BOM"
    exit 0
} else {
    Write-Host "发现 $($bad.Count) 个含中文但缺 BOM 的文件，请运行 tools/add-bom.ps1："
    $bad | ForEach-Object { Write-Host "  $_" }
    exit 1
}
