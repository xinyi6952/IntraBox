using System;

namespace IntraBox.Core
{
    /// <summary>示例条目是否仍像出厂内容，供版本升级时决定是否刷新。</summary>
    public static class SampleGuard
    {
        public const string TitlePrefix = "【示例】";

        /// <summary>
        /// 标题仍以【示例】开头，且正文与出厂原文相同才允许覆盖。缺文件（currentPlain 为 null）视为可刷新。
        /// </summary>
        public static bool ShouldRefresh(string title, string currentPlain, string stockPlain)
        {
            return ShouldRefresh(title, currentPlain, stockPlain, null, null);
        }

        /// <summary>
        /// 另比富文本格式戳：纯文本相同但加粗/颜色/高亮等已改则不覆盖。
        /// 两戳都空则只比正文（Markdown）。
        /// </summary>
        public static bool ShouldRefresh(string title, string currentPlain, string stockPlain, string currentStamp, string stockStamp)
        {
            if (currentPlain == null) return true;
            if (!HasExamplePrefix(title)) return false;
            if (!SamePlain(currentPlain, stockPlain)) return false;
            if (string.IsNullOrEmpty(currentStamp) && string.IsNullOrEmpty(stockStamp))
                return true;
            return string.Equals(currentStamp ?? "", stockStamp ?? "", StringComparison.Ordinal);
        }

        public static bool HasExamplePrefix(string title)
        {
            return !string.IsNullOrEmpty(title)
                && title.StartsWith(TitlePrefix, StringComparison.Ordinal);
        }

        public static bool SamePlain(string a, string b)
        {
            return Normalize(a) == Normalize(b);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }
    }
}
