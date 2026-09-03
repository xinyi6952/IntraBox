#Requires -Version 5.1
<#
.SYNOPSIS
  列出「当前工作目录」停在指定目录里的进程（这种占用用任务管理器/资源监视器查不到）。
.DESCRIPTION
  Windows 上进程的工作目录(cwd)会持有目录句柄，导致 rmdir 删不掉该目录。
  常规工具只查文件句柄/DLL，不显示 cwd。本脚本读取每个进程的 PEB 里的
  CurrentDirectory 字段，找出 cwd 落在目标目录（或其子目录）的进程。
  只读枚举，不结束任何进程。
.PARAMETER Path
  要检查的目标目录绝对路径，例如 C:\work\xinyi\code\IntraBox\dist\IntraBox
.OUTPUTS
  打印占用进程列表；退出码 = 占用进程数（0 表示无占用）。
#>
param(
    [Parameter(Mandatory = $true)][string]$Path
)

# 让中文提示在当前控制台正常显示（控制台代码页可能是 GBK）
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

# 规范化为带反斜杠结尾的小写比较串，匹配该目录及其子目录
$target = $Path.TrimEnd('\').ToLowerInvariant()

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class CwdFinder {
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int c, ref PBI pbi, int len, out int ret);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(int acc, bool inh, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, int size, out int read);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr h, out bool wow64);
    [StructLayout(LayoutKind.Sequential)] struct PBI { public IntPtr R1, Peb, R2, R3, Pid, R4; }
    [StructLayout(LayoutKind.Sequential)] struct UNICODE_STRING { public ushort Length, MaxLength; public IntPtr Buffer; }
    public static string GetCwd(int pid) {
        IntPtr h = OpenProcess(0x1000|0x0400|0x0010, false, pid); // QUERY_LIMITED|VM_READ|QUERY_INFO
        if (h == IntPtr.Zero) return null;
        try {
            bool wow; IsWow64Process(h, out wow);
            if (wow) return null; // 跨位数(32位进程)读不了 PEB，跳过
            PBI pbi = new PBI(); int r;
            if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out r) != 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(8);
            IntPtr pp;
            // x64 PEB+0x20 = ProcessParameters
            ReadProcessMemory(h, IntPtr.Add(pbi.Peb, 0x20), buf, 8, out r);
            pp = Marshal.ReadIntPtr(buf);
            IntPtr usBuf = Marshal.AllocHGlobal(16);
            // ProcessParameters+0x38 = CurrentDirectory(UNICODE_STRING)
            ReadProcessMemory(h, IntPtr.Add(pp, 0x38), usBuf, 16, out r);
            var us = (UNICODE_STRING)Marshal.PtrToStructure(usBuf, typeof(UNICODE_STRING));
            if (us.Buffer == IntPtr.Zero || us.Length == 0) { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(usBuf); return null; }
            IntPtr sb = Marshal.AllocHGlobal(us.Length);
            ReadProcessMemory(h, us.Buffer, sb, us.Length, out r);
            string s = Marshal.PtrToStringUni(sb, us.Length / 2);
            Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(usBuf); Marshal.FreeHGlobal(sb);
            return s;
        } catch { return null; } finally { CloseHandle(h); }
    }
}
"@

$hits = New-Object System.Collections.Generic.List[object]
foreach ($p in Get-Process) {
    $cwd = $null
    try { $cwd = [CwdFinder]::GetCwd($p.Id) } catch { }
    if ([string]::IsNullOrEmpty($cwd)) { continue }
    $norm = $cwd.TrimEnd('\').ToLowerInvariant()
    if ($norm -eq $target -or $norm.StartsWith($target + '\')) {
        $hits.Add([pscustomobject]@{
            PID  = $p.Id
            进程 = $p.ProcessName
            工作目录 = $cwd
        })
    }
}

if ($hits.Count -eq 0) {
    Write-Host "未发现有进程的工作目录占用该目录。" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "发现 $($hits.Count) 个进程的工作目录停在目标目录里，正在占用它：" -ForegroundColor Yellow
    $hits | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "提示：这些是后台服务/终端进程。结束它们（或重启）后即可删除目录。" -ForegroundColor Yellow
    Write-Host "      可用命令结束：Stop-Process -Id <PID> -Force" -ForegroundColor DarkGray
}
exit $hits.Count
