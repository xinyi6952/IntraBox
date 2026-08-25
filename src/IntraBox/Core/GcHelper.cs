using System;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 手动 GC：只在销毁/清空等时机调用。2 秒内重复调用直接忽略，避免轮询或连点拖垮 UI。
    /// </summary>
    public static class GcHelper
    {
        private static readonly object Gate = new object();
        private static long _lastTicks;
        private const int MinIntervalMs = 2000;

        public static void CollectSafely()
        {
            long now = DateTime.UtcNow.Ticks;
            lock (Gate)
            {
                if (_lastTicks != 0 && (now - _lastTicks) < (long)MinIntervalMs * TimeSpan.TicksPerMillisecond)
                    return;
                _lastTicks = now;
            }

            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp != null)
                disp.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunCollect));
            else
                RunCollect();
        }

        private static void RunCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
