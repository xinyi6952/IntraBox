using System;

namespace IntraBox.Core
{
    /// <summary>
    /// 内存压缩策略（纯函数）：重工具名单、托盘空闲/水位夹取、是否该 Trim 或静默卸载。
    /// 不碰 Dispatcher / 进程，便于单测。
    /// </summary>
    public static class MemoryTrimPolicy
    {
        public const int DefaultIdleSec = 90;
        public const int MinIdleSec = 30;
        public const int MaxIdleSec = 600;
        public const int DefaultHighMb = 220;
        public const int MinHighMb = 80;
        public const int MaxHighMb = 2048;
        public const int DefaultLowMb = 120;
        public const int MinLowMb = 50;
        public const int MaxLowMb = MaxHighMb - 20;
        public const int TrimCooldownSec = 60;

        /// <summary>切走后额外压工作集的工具（大文本/位图/IE/表格）。轻工具只 GC 不 Trim，避免侧栏连点抖动。</summary>
        private static readonly string[] HeavyKeys =
        {
            "formatter", "diff", "mdpreview", "notes", "txt2excel", "imageconvert",
            "colorblind", "dupfiles", "qrcode", "screenshot", "hash", "filesearch",
            "crypto", "base64", "textstats", "unicodeinspect", "jsonschema", "jsontoclass",
            "regexviz", "certdecode"
        };

        public static bool IsHeavyModule(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < HeavyKeys.Length; i++)
            {
                if (string.Equals(HeavyKeys[i], key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static int ClampIdleSec(int n)
        {
            if (n <= 0) return DefaultIdleSec;
            if (n < MinIdleSec) return MinIdleSec;
            if (n > MaxIdleSec) return MaxIdleSec;
            return n;
        }

        public static int ClampHighMb(int n)
        {
            if (n <= 0) return DefaultHighMb;
            if (n < MinHighMb) return MinHighMb;
            if (n > MaxHighMb) return MaxHighMb;
            return n;
        }

        public static int ClampLowMb(int n, int highMb)
        {
            int high = ClampHighMb(highMb);
            int maxLow = high - 20;
            if (maxLow < MinLowMb) maxLow = MinLowMb;
            int v = n <= 0 ? DefaultLowMb : n;
            if (v < MinLowMb) v = MinLowMb;
            if (v > maxLow) v = maxLow;
            return v;
        }

        /// <summary>主窗已明确藏托盘、未退出、无 Loading、无待办提醒窗时才自动 Trim/卸载。</summary>
        public static bool CanAutoAct(bool parked, bool exiting, bool hasLoading, bool reminderOpen)
        {
            return parked && !exiting && !hasLoading && !reminderOpen;
        }

        /// <summary>工作集达到高水位才按阈值 Trim；落到低水位则停，避免 EmptyWorkingSet 后数字回升反复打。</summary>
        public static bool ShouldThresholdTrim(long workingSetMb, int highMb, int lowMb)
        {
            int high = ClampHighMb(highMb);
            int low = ClampLowMb(lowMb, high);
            if (workingSetMb <= low) return false;
            return workingSetMb >= high;
        }

        /// <summary>藏托盘满空闲秒数且距上次 Trim 已过冷却，再压一次工作集（可同时卸模块）。</summary>
        public static bool ShouldIdleTrim(double hiddenSeconds, int idleSec, double sinceLastTrimSeconds, int cooldownSec)
        {
            int idle = ClampIdleSec(idleSec);
            int cool = cooldownSec < 1 ? TrimCooldownSec : cooldownSec;
            return hiddenSeconds >= idle && sinceLastTrimSeconds >= cool;
        }
    }
}
