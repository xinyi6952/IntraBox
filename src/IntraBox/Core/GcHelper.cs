using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 手动 GC：切换工具/清历史时调用 CollectSafely（不压缩工作集，避免频繁抖一下）。
    /// 状态栏「回收内存」走 CollectAndTrim：强制回收后再把空闲页还给系统。
    /// </summary>
    public static class GcHelper
    {
        private static readonly object Gate = new object();
        private static long _lastTicks;
        private const int MinIntervalMs = 2000;

        public static void CollectSafely()
        {
            if (!TryBegin()) return;
            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp != null)
                disp.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunCollect));
            else
                RunCollect();
        }

        /// <summary>同步回收并压缩工作集。2 秒内重复点击返回 false。</summary>
        public static bool TryCollectAndTrim(out MemoryUsage after)
        {
            after = null;
            if (!TryBegin()) return false;
            RunCollect();
            TrimWorkingSet();
            after = MemoryUsage.Capture();
            return true;
        }

        public static void TrimWorkingSet()
        {
            try
            {
                using (var p = Process.GetCurrentProcess())
                    EmptyWorkingSet(p.Handle);
            }
            catch { }
        }

        private static bool TryBegin()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (Gate)
            {
                if (_lastTicks != 0 && (now - _lastTicks) < (long)MinIntervalMs * TimeSpan.TicksPerMillisecond)
                    return false;
                _lastTicks = now;
            }
            return true;
        }

        private static void RunCollect()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        }

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);
    }
}
