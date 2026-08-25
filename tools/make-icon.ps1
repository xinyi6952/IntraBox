# 从指定图片生成多尺寸 app.ico（PNG 编码，兼容 Win7+）
# 用法：powershell -NoProfile -File tools/make-icon.ps1 -srcPath "C:\path\to\source.png"
param(
    [Parameter(Mandatory = $true)][string]$srcPath,
    [string]$dstPath = ""
)

Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrEmpty($dstPath)) {
    $dstPath = Join-Path $PSScriptRoot "..\src\IntraBox\app.ico"
}

$src = [System.Drawing.Bitmap]::FromFile($srcPath)
Write-Host ("源图尺寸: {0} x {1}" -f $src.Width, $src.Height)

# 裁剪为正方形（取中心）
[int]$side = [Math]::Min($src.Width, $src.Height)
[int]$ox = ($src.Width - $side) / 2
[int]$oy = ($src.Height - $side) / 2

$square = New-Object System.Drawing.Bitmap($side, $side)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$destRect = New-Object System.Drawing.Rectangle(0, 0, $side, $side)
$srcRect = New-Object System.Drawing.Rectangle($ox, $oy, $side, $side)
$g.DrawImage($src, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

$sizes = @(16, 32, 48, 64, 128, 256)
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($square, $s, $s)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += ,@($s, $ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}
$square.Dispose()

$fs = New-Object System.IO.FileStream($dstPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)   # reserved
$bw.Write([UInt16]1)   # type = icon
$bw.Write([UInt16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)     # color count
    $bw.Write([Byte]0)     # reserved
    $bw.Write([UInt16]1)   # planes
    $bw.Write([UInt16]32)  # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Close(); $fs.Close()

Write-Host ("已生成: {0} ({1} 个尺寸)" -f $dstPath, $entries.Count)
