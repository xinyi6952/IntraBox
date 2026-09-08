#Requires -Version 5.1
# 构建/完全重建前：若 IntraBox.exe 在跑，二次确认后再结束。UTF-8 BOM。
# 退出码 0 = 未在运行或已结束；1 = 用户取消；2 = 结束失败。
param(
    [string]$Purpose = "build"
)

Add-Type -AssemblyName System.Windows.Forms | Out-Null

function Get-IntraBoxProcesses {
    Get-Process -Name IntraBox -ErrorAction SilentlyContinue
}

$procs = @(Get-IntraBoxProcesses)
if ($procs.Count -eq 0) { exit 0 }

$title = "IntraBox 构建"
if ($Purpose -eq "rebuild") { $title = "IntraBox 完全重建" }

$lines = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $procs.Count; $i++) {
    $p = $procs[$i]
    $path = ""
    try { $path = $p.Path } catch { $path = "" }
    if ([string]::IsNullOrEmpty($path)) { $path = "(路径未知)" }
    $lines.Add("PID " + $p.Id + "  " + $path)
}

$nl = [Environment]::NewLine
$msg = "检测到 IntraBox 正在运行（通常在系统托盘）。覆盖 exe 前需要先结束该进程。" +
    $nl + $nl +
    [string]::Join($nl, $lines.ToArray()) +
    $nl + $nl +
    "确定结束上述进程并继续吗？" + $nl +
    "选「否」则中止，不结束进程。"

$result = [System.Windows.Forms.MessageBox]::Show(
    $msg,
    $title,
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Warning,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button2)

if ($result -ne [System.Windows.Forms.DialogResult]::Yes) {
    exit 1
}

for ($i = 0; $i -lt $procs.Count; $i++) {
    try { Stop-Process -Id $procs[$i].Id -Force -ErrorAction Stop } catch { }
}

$deadline = (Get-Date).AddSeconds(10)
do {
    Start-Sleep -Milliseconds 200
    $left = @(Get-IntraBoxProcesses)
    if ($left.Count -eq 0) { exit 0 }
} while ((Get-Date) -lt $deadline)

cmd /c "taskkill /F /IM IntraBox.exe >nul 2>&1"
Start-Sleep -Milliseconds 500
if (@(Get-IntraBoxProcesses).Count -eq 0) { exit 0 }
exit 2
