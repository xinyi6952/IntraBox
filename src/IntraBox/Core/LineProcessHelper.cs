using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>行处理：拆行/去重/排序/删空行/首尾空白/前后缀/自然排序。纯逻辑，便于单元测试。</summary>
    public static class LineProcessHelper
    {
        public static string[] SplitLines(string text)
        {
            var t = text ?? "";
            return t.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        public static List<string> Deduplicate(string[] lines)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (var l in lines) if (seen.Add(l)) list.Add(l);
            return list;
        }

        public static List<string> DeduplicateSort(string[] lines)
        {
            var set = new HashSet<string>(lines);
            var list = new List<string>(set);
            list.Sort(StringComparer.CurrentCulture);
            return list;
        }

        public static List<string> SortAsc(string[] lines)
        {
            var list = new List<string>(lines);
            list.Sort(StringComparer.CurrentCulture);
            return list;
        }

        public static List<string> SortDesc(string[] lines)
        {
            var list = new List<string>(lines);
            list.Sort(StringComparer.CurrentCulture);
            list.Reverse();
            return list;
        }

        public static List<string> SortNatural(string[] lines)
        {
            var list = new List<string>(lines);
            list.Sort(NaturalCompare);
            return list;
        }

        public static List<string> RemoveBlank(string[] lines)
        {
            var list = new List<string>();
            foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) list.Add(l);
            return list;
        }

        public static List<string> TrimLines(string[] lines)
        {
            var list = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++) list.Add(lines[i].Trim());
            return list;
        }

        public static List<string> AddAffix(string[] lines, string prefix, string suffix)
        {
            var pre = prefix ?? "";
            var suf = suffix ?? "";
            var list = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++) list.Add(pre + lines[i] + suf);
            return list;
        }

        /// <summary>自然排序比较：数字段按数值比较，其余按不区分大小写。</summary>
        public static int NaturalCompare(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    long na = 0, nb = 0;
                    while (i < a.Length && char.IsDigit(a[i])) { na = na * 10 + (a[i] - '0'); i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { nb = nb * 10 + (b[j] - '0'); j++; }
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
