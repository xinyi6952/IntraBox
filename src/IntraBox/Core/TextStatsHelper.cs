using System;
using System.Collections.Generic;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>文本统计结果。</summary>
    public sealed class TextStatsResult
    {
        public int Chars { get; set; }
        public int CharsNoWs { get; set; }
        public int Words { get; set; }
        public int Lines { get; set; }
        public int Paragraphs { get; set; }
        public Dictionary<string, int> WordFreq { get; set; }
        public Dictionary<char, int> CharFreq { get; set; }
    }

    /// <summary>文本统计：字符/词/行/段落数与词频、字符频率。纯逻辑，便于单元测试。</summary>
    public static class TextStatsHelper
    {
        public static List<string> SplitWords(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '\'')
                    sb.Append(c);
                else
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                }
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        public static TextStatsResult Analyze(string text)
        {
            var s = text ?? "";
            var result = new TextStatsResult { Chars = s.Length };

            int noWs = 0;
            foreach (char c in s) if (!char.IsWhiteSpace(c)) noWs++;
            result.CharsNoWs = noWs;

            string norm = s.Replace("\r\n", "\n").Replace('\r', '\n');
            result.Lines = string.IsNullOrEmpty(norm) ? 0 : norm.Split('\n').Length;

            int paras = 0;
            bool inP = false;
            foreach (var line in norm.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) inP = false;
                else if (!inP) { paras++; inP = true; }
            }
            result.Paragraphs = paras;

            var words = SplitWords(s);
            result.Words = words.Count;

            var wordFreq = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var w in words)
            {
                int n;
                wordFreq.TryGetValue(w, out n);
                wordFreq[w] = n + 1;
            }
            result.WordFreq = wordFreq;

            var charFreq = new Dictionary<char, int>();
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                int n;
                charFreq.TryGetValue(c, out n);
                charFreq[c] = n + 1;
            }
            result.CharFreq = charFreq;

            return result;
        }
    }
}
