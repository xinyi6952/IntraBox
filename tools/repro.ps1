$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$exe = Join-Path $PSScriptRoot "..\src\IntraBox\bin\Release\IntraBox.exe"
[System.Reflection.Assembly]::LoadFrom($exe) | Out-Null

# 创建 App 实例并加载 App.xaml 里的 Application.Resources（主题色等）
$app = New-Object IntraBox.App
$uri = New-Object System.Uri("/IntraBox;component/app.xaml", [System.UriKind]::Relative)
[System.Windows.Application]::LoadComponent($app, $uri)

$types = @(
    "IntraBox.Controls.CodeEditor",
    "IntraBox.Modules.Formatter.FormatterView",
    "IntraBox.Modules.Diff.DiffView",
    "IntraBox.Modules.Base64.Base64View",
    "IntraBox.Modules.Hash.HashView",
    "IntraBox.Modules.Timestamp.TimestampView",
    "IntraBox.Modules.ClipboardHistory.ClipboardView",
    "IntraBox.Modules.PortTest.PortTestView",
    "IntraBox.Modules.RegexTest.RegexTestView",
    "IntraBox.Modules.Generator.GeneratorView",
    "IntraBox.Modules.ColorPicker.ColorPickerView",
    "IntraBox.Modules.TxtToExcel.TxtToExcelView"
)

foreach ($t in $types) {
    try {
        $obj = New-Object $t
        Write-Host ("OK    " + $t)
    } catch {
        Write-Host ("FAIL  " + $t)
        $ex = $_.Exception
        while ($ex) {
            Write-Host ("      [" + $ex.GetType().Name + "] " + $ex.Message)
            if ($ex.StackTrace) { Write-Host ("      " + ($ex.StackTrace -split "`n")[0]) }
            $ex = $ex.InnerException
        }
    }
}
$app.Shutdown()
