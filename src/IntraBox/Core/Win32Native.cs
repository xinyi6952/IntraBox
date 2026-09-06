using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>本机 Win32：Restart Manager、窗口枚举/置顶、执行状态。</summary>
    public static class Win32Native
    {
        public const int HwndTopmost = -1;
        public const int HwndNotopmost = -2;
        public const uint SwpNomove = 0x0002;
        public const uint SwpNosize = 0x0001;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        public const int GwlExstyle = -20;
        public const int WsExToolwindow = 0x00000080;
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int FindExecutable(string lpFile, string lpDirectory, StringBuilder lpResult);

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RmProcessInfo[] rgAffectedApps, ref uint lpdwRebootReasons);

        [StructLayout(LayoutKind.Sequential)]
        public struct RmUniqueProcess
        {
            public int DwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string StrAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string StrServiceShortName;
            public uint ApplicationType;
            public uint AppStatus;
            public uint TsSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool BRestartable;
        }

        public static List<WindowEntry> ListVisibleWindows()
        {
            var list = new List<WindowEntry>();
            EnumWindows((hWnd, l) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int ex = GetWindowLong(hWnd, GwlExstyle);
                if ((ex & WsExToolwindow) != 0) return true;
                var sb = new StringBuilder(512);
                if (GetWindowText(hWnd, sb, sb.Capacity) <= 0) return true;
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                list.Add(new WindowEntry { Handle = hWnd, Title = title, Pid = (int)pid });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool TryGetLockingProcesses(string filePath, out List<LockingProcess> procs, out string error)
        {
            procs = new List<LockingProcess>();
            error = null;
            uint handle;
            string key = Guid.NewGuid().ToString();
            int rc = RmStartSession(out handle, 0, key);
            if (rc != 0)
            {
                error = "无法启动 Restart Manager（错误 " + rc + "）";
                return false;
            }
            try
            {
                string[] files = new string[] { filePath };
                rc = RmRegisterResources(handle, 1, files, 0, IntPtr.Zero, 0, null);
                if (rc != 0)
                {
                    error = "注册资源失败（错误 " + rc + "）";
                    return false;
                }
                uint needed = 0;
                uint n = 0;
                uint reboot = 0;
                rc = RmGetList(handle, out needed, ref n, null, ref reboot);
                if (needed == 0)
                    return true;
                var arr = new RmProcessInfo[needed];
                n = needed;
                rc = RmGetList(handle, out needed, ref n, arr, ref reboot);
                if (rc != 0)
                {
                    error = "获取占用进程失败（错误 " + rc + "）";
                    return false;
                }
                for (int i = 0; i < n; i++)
                {
                    procs.Add(new LockingProcess
                    {
                        Pid = arr[i].Process.DwProcessId,
                        Name = arr[i].StrAppName
                    });
                }
                return true;
            }
            finally
            {
                RmEndSession(handle);
            }
        }
    }

    public sealed class WindowEntry
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public int Pid { get; set; }
    }

    public sealed class LockingProcess
    {
        public int Pid { get; set; }
        public string Name { get; set; }
    }
}
