using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>
    /// 兼容降级：写排查日志，同一原因本进程只记一次，避免 20 秒刷新刷屏。
    /// 界面文案由调用方用 FormHint / 预览条展示。
    /// </summary>
    public static class CompatDegrade
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> _once = new HashSet<string>(StringComparer.Ordinal);

        public static bool Once(string key, string message, Exception ex)
        {
            if (string.IsNullOrEmpty(key)) key = message ?? "";
            lock (Sync)
            {
                if (_once.Contains(key)) return false;
                _once.Add(key);
            }
            if (ex != null) AppLog.Error(message, ex);
            else AppLog.Warn(message);
            return true;
        }

        public static void ErrorAlways(string message, Exception ex)
        {
            AppLog.Error(message, ex);
        }
    }
}
