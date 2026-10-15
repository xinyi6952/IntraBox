# Requires -Version 5.1
# 下载 WebView2 Fixed Version（x64 CAB）并解压到 redist\WebView2Runtime。
# UTF-8 BOM。不入库：redist/ 已在 .gitignore。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/fetch-webview2-runtime.ps1
# 也可先把完整 CAB 放到 redist\ 再运行本脚本（仅解压）。

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$redist = Join-Path $root "redist"
$dest = Join-Path $redist "WebView2Runtime"
$cab = Join-Path $redist "Microsoft.WebView2.FixedVersionRuntime.x64.cab"
$urlFile = Join-Path $redist "webview2-x64.url.txt"
$minCabBytes = 80MB

if (-not (Test-Path $redist)) { New-Item -ItemType Directory -Path $redist | Out-Null }

function Expand-Cab([string]$cabPath, [string]$outDir) {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Path $outDir | Out-Null
    & expand.exe $cabPath -F:* $outDir | Out-Null
    $exe = Get-ChildItem -Path $outDir -Filter "msedgewebview2.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $exe) { throw "CAB 解压后未找到 msedgewebview2.exe" }
    $runtimeRoot = $exe.Directory.FullName
    if ($runtimeRoot -ne (Resolve-Path $outDir).Path) {
        $tmp = Join-Path $redist ("WebView2Runtime_" + [Guid]::NewGuid().ToString("N"))
        Move-Item $runtimeRoot $tmp
        Remove-Item $outDir -Recurse -Force
        Move-Item $tmp $outDir
    }
    Write-Host "WebView2 Fixed Version -> $outDir"
}

function Download-File([string]$url, [string]$outFile) {
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        Write-Host "使用 curl 下载（失败可断点续传）..."
        & curl.exe -L --retry 5 --retry-all-errors --retry-delay 3 -C - -o $outFile $url
        if ($LASTEXITCODE -ne 0) { throw "curl 下载失败，退出码 $LASTEXITCODE" }
        return
    }
    Invoke-WebRequest -Uri $url -OutFile $outFile
}

function Get-PageHtml([string]$url) {
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        $tmp = Join-Path $redist ("webview2-page-" + [Guid]::NewGuid().ToString("N") + ".html")
        & curl.exe -L --retry 3 --retry-delay 2 -o $tmp $url
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmp)) { throw "读取下载页失败，退出码 $LASTEXITCODE" }
        $html = [IO.File]::ReadAllText($tmp)
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        return $html
    }
    return (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
}

function Get-LatestX64CabUrl([string]$html) {
    # 官网页把 URL 写在前端 JSON 里，斜杠常为 \u002F，不能直接用 https:// 正则。
    $decoded = $html.Replace('\u002F', '/').Replace('\/', '/')
    $bestUrl = $null
    $bestVer = New-Object System.Version 0, 0, 0, 0
    $rx = 'https://[^"''\\\s<>]+Microsoft\.WebView2\.FixedVersionRuntime\.([0-9.]+)\.x64\.cab'
    foreach ($m in [regex]::Matches($decoded, $rx)) {
        $v = $null
        if ([version]::TryParse($m.Groups[1].Value, [ref]$v) -and $v -gt $bestVer) {
            $bestVer = $v
            $bestUrl = $m.Value
        }
    }
    return $bestUrl
}

$existingCab = Get-ChildItem -Path $redist -Filter "Microsoft.WebView2.FixedVersionRuntime*.x64.cab" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($existingCab) { $cab = $existingCab.FullName }

$cabReady = (Test-Path $cab) -and ((Get-Item $cab).Length -ge $minCabBytes)
if (-not $cabReady) {
    Write-Host "正在解析微软 Fixed Version 下载地址（x64）..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    } catch { }
    $href = $null
    if (Test-Path $urlFile) {
        $href = (Get-Content $urlFile -Raw).Trim()
    }
    if (-not $href) {
        try {
            $page = Get-PageHtml "https://developer.microsoft.com/en-us/microsoft-edge/webview2/"
            $href = Get-LatestX64CabUrl $page
        } catch {
            Write-Host $_.Exception.Message
        }
    }
    if (-not $href) {
        Write-Host "无法自动解析下载链接。请打开："
        Write-Host "  https://developer.microsoft.com/microsoft-edge/webview2/"
        Write-Host "选择 Fixed Version + x64，把 CAB 放到："
        Write-Host "  $redist"
        Write-Host "再运行本脚本。"
        exit 2
    }
    Set-Content -Path $urlFile -Value $href -Encoding ASCII
    Write-Host "下载 $href"
    Download-File $href $cab
}

Expand-Cab $cab $dest
Write-Host "完成。下次 build.bat 会把 CAB 放进发布目录；打开预览时再解压。"
