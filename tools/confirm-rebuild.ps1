#Requires -Version 5.1
# 完全重建前的确认：会清空本机 IntraBox 数据。UTF-8 BOM，供 rebuild.bat 调用。
# 退出码 0 = 继续；非 0 = 取消。
Add-Type -AssemblyName System.Windows.Forms | Out-Null

$data = Join-Path $env:LOCALAPPDATA "IntraBox\Data"
$msg = "完全重建会永久删除本机 IntraBox 数据，无法从回收站恢复。" +
    [Environment]::NewLine + [Environment]::NewLine +
    "将删除：" + [Environment]::NewLine +
    "- " + $data + [Environment]::NewLine +
    "  （配置、历史、待办、笔记、账号备忘、启动器收藏等）" + [Environment]::NewLine +
    "- 若 dist\IntraBox\datapath.txt 指向自定义目录，该目录一并删除" + [Environment]::NewLine +
    "- dist\IntraBox\ 旧发布目录" + [Environment]::NewLine + [Environment]::NewLine +
    "下次启动将回到首次默认（欢迎引导、示例数据）。" + [Environment]::NewLine + [Environment]::NewLine +
    "确定要继续吗？"

$result = [System.Windows.Forms.MessageBox]::Show(
    $msg,
    "IntraBox 完全重建",
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Warning,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button2)

if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
    exit 0
}
exit 1
