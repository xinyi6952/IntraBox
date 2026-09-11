using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 手动 GC：轻工具切换走 CollectSafely（不压缩工作集，避免侧栏连点抖一下）。
    /// 重工具切走走 CollectAndTrimIdle；状态栏「回收内存」走 TryCollectAndTrim（2 秒闸门）；
    /// 藏到托盘走 CollectAndTrimNow（无闸门，空闲时 GC + EmptyWorkingSet）。
    /// 每次强制 GC / 因闸门跳过都追加一行到数据根 gc.log，便于看频率。
    /// </summary>
    public static class GcHelper
    {
        private static readonly object Gate = new object();
        private static long _lastTicks;
        private const int MinIntervalMs = 2000;

        public static void CollectSafely(string reason)
        {
            if (!TryBegin())
            {
                WriteSkip(reason, "gate");
                return;
            }
            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp != null)
                disp.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => RunAndLog(reason, false)));
            else
                RunAndLog(reason, false);
        }

        /// <summary>同步回收并压缩工作集。2 秒内重复点击返回 false。</summary>
        public static bool TryCollectAndTrim(out MemoryUsage after, string reason)
        {
            after = null;
            if (!TryBegin())
            {
                WriteSkip(reason, "gate");
                return false;
            }
            after = RunAndLog(reason, true);
            return true;
        }

        /// <summary>同步强制 GC 并 EmptyWorkingSet。不受 2 秒闸门限制（藏托盘时即使刚切过工具也要瘦工作集）。</summary>
        public static void CollectAndTrimNow(string reason)
        {
            lock (Gate)
            {
                _lastTicks = DateTime.UtcNow.Ticks;
            }
            RunAndLog(reason, true);
        }

        /// <summary>UI 空闲后再 GC + 压工作集。重工具切走时用。</summary>
        public static void CollectAndTrimIdle(string reason)
        {
            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp != null)
                disp.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => CollectAndTrimNow(reason)));
            else
                CollectAndTrimNow(reason);
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

        /// <summary>单测用：格式化 gc.log 一行，不含换行符。</summary>
        public static string FormatLine(DateTime now, bool skipped, string reason, string skipWhy, bool trim, long elapsedMs, MemoryUsage before, MemoryUsage after)
        {
            reason = SanitizeReason(reason);
            var sb = new System.Text.StringBuilder(160);
            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            if (skipped)
            {
                sb.Append(" GC skip reason=");
                sb.Append(reason);
                sb.Append(" why=");
                sb.Append(string.IsNullOrEmpty(skipWhy) ? "gate" : SanitizeReason(skipWhy));
            }
            else
            {
                sb.Append(" GC run reason=");
                sb.Append(reason);
                sb.Append(" trim=");
                sb.Append(trim ? "1" : "0");
                sb.Append(" elapsed=");
                sb.Append(elapsedMs);
                sb.Append("ms");
                AppendPair(sb, " ws=", before, after, true);
                AppendPair(sb, " private=", before, after, false);
                sb.Append(" managed=");
                sb.Append(Mb(before != null ? before.ManagedBytes : 0));
                sb.Append("->");
                sb.Append(Mb(after != null ? after.ManagedBytes : 0));
            }
            return sb.ToString();
        }

        public static string SanitizeReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "unspecified";
            reason = reason.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (reason.Length > 80) reason = reason.Substring(0, 80);
            return reason;
        }

        private static void AppendPair(System.Text.StringBuilder sb, string label, MemoryUsage before, MemoryUsage after, bool workingSet)
        {
            sb.Append(label);
            long b = 0, a = 0;
            if (before != null) b = workingSet ? before.WorkingSetBytes : before.PrivateBytes;
            if (after != null) a = workingSet ? after.WorkingSetBytes : after.PrivateBytes;
            sb.Append(Mb(b));
            sb.Append("->");
            sb.Append(Mb(a));
        }

        private static long Mb(long bytes)
        {
            if (bytes < 0) bytes = 0;
            return bytes / 1024L / 1024L;
        }

        private static MemoryUsage RunAndLog(string reason, bool trim)
        {
            MemoryUsage before = MemoryUsage.Capture();
            var sw = Stopwatch.StartNew();
            RunCollect();
            if (trim) TrimWorkingSet();
            sw.Stop();
            MemoryUsage after = MemoryUsage.Capture();
            if (trim) MemoryIdleGuard.MarkTrimmedNow();
            WriteLine(FormatLine(DateTime.Now, false, reason, null, trim, sw.ElapsedMilliseconds, before, after));
            return after;
        }

        private static void WriteSkip(string reason, string why)
        {
            WriteLine(FormatLine(DateTime.Now, true, reason, why, false, 0, null, null));
        }

        private static void WriteLine(string line)
        {
            LogFileHelper.Append(DataPaths.GcLog, line + Environment.NewLine);
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
