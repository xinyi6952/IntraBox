using System;
using System.Diagnostics;

namespace IntraBox.Core
{
    /// <summary>进程内存快照：状态栏用工作集，排查窗同时给出专用字节与托管堆。</summary>
    public sealed class MemoryUsage
    {
        public long WorkingSetBytes { get; set; }
        public long PrivateBytes { get; set; }
        public long ManagedBytes { get; set; }

        public static MemoryUsage Capture()
        {
            var u = new MemoryUsage();
            try
            {
                using (var p = Process.GetCurrentProcess())
                {
                    u.WorkingSetBytes = p.WorkingSet64;
                    u.PrivateBytes = p.PrivateMemorySize64;
                }
            }
            catch { }
            try
            {
                u.ManagedBytes = GC.GetTotalMemory(false);
            }
            catch { }
            return u;
        }

        public static string FormatMb(long bytes)
        {
            if (bytes < 0) bytes = 0;
            return (bytes / 1024L / 1024L) + " MB";
        }

        public string WorkingSetText
        {
            get { return FormatMb(WorkingSetBytes); }
        }

        public string PrivateText
        {
            get { return FormatMb(PrivateBytes); }
        }

        public string ManagedText
        {
            get { return FormatMb(ManagedBytes); }
        }
    }
}
