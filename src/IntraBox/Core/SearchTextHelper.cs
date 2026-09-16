using System;

namespace IntraBox.Core
{
    /// <summary>下拉模糊匹配：开头、包含、较长子序列（不区分大小写）。</summary>
    public static class SearchTextHelper
    {
        public static int Score(string query, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (string.IsNullOrWhiteSpace(query)) return 1;
            string q = query.Trim();
            if (text.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 100;
            if (text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return 50;
            if (q.Length >= 2 && IsSubsequence(text, q)) return 15;
            return 0;
        }

        public static int Score(string query, string a, string b)
        {
            int x = Score(query, a);
            int y = Score(query, b);
            return x > y ? x : y;
        }

        public static bool Matches(string query, string text)
        {
            return Score(query, text) > 0;
        }

        private static bool IsSubsequence(string hay, string needle)
        {
            int j = 0;
            for (int i = 0; i < hay.Length && j < needle.Length; i++)
            {
                if (char.ToUpperInvariant(hay[i]) == char.ToUpperInvariant(needle[j]))
                    j++;
            }
            return j == needle.Length;
        }
    }
}
