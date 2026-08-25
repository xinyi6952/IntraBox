using System;
using System.Collections.Generic;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>命名风格转换（驼峰/帕斯卡/下划线/短横线/大小写）。纯逻辑，便于单元测试。</summary>
    public static class NameCaseHelper
    {
        /// <summary>把命名拆成单词：下划线/短横线/空格/点/斜杠分隔，以及驼峰边界。</summary>
        public static List<string> SplitWords(string input)
        {
            var words = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return words;

            var normalized = input.Trim();
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (c == '_' || c == '-' || c == ' ' || c == '.' || c == '/')
                {
                    Flush(words, sb);
                }
                else if (char.IsUpper(c) && sb.Length > 0)
                {
                    var prev = sb[sb.Length - 1];
                    bool split = char.IsLower(prev) || char.IsDigit(prev);
                    if (split)
                    {
                        Flush(words, sb);
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    // 处理 XMLHttp：连续大写后接小写，把最后一个大写拆到下一词
                    if (sb.Length > 1 && char.IsUpper(sb[sb.Length - 1]) && char.IsLower(c))
                    {
                        var last = sb[sb.Length - 1];
                        sb.Length--;
                        Flush(words, sb);
                        sb.Append(last);
                    }
                    sb.Append(c);
                }
            }
            Flush(words, sb);
            return words;
        }

        public static string ToCamel(List<string> words)
        {
            if (words.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append(words[0].ToLowerInvariant());
            for (int i = 1; i < words.Count; i++)
                sb.Append(PascalOne(words[i]));
            return sb.ToString();
        }

        public static string ToPascal(List<string> words)
        {
            var sb = new StringBuilder();
            foreach (var w in words) sb.Append(PascalOne(w));
            return sb.ToString();
        }

        public static string ToSnake(List<string> words)
        {
            return Join(words, "_", true);
        }

        public static string ToScreamingSnake(List<string> words)
        {
            return Join(words, "_", false).ToUpperInvariant();
        }

        public static string ToKebab(List<string> words)
        {
            return Join(words, "-", true);
        }

        /// <summary>一次输出全部风格（供界面展示，含制表符对齐）。</summary>
        public static string FormatAll(string input)
        {
            var words = SplitWords(input);
            var sb = new StringBuilder();
            sb.AppendLine("camelCase\t" + ToCamel(words));
            sb.AppendLine("PascalCase\t" + ToPascal(words));
            sb.AppendLine("snake_case\t" + ToSnake(words));
            sb.AppendLine("SNAKE_CASE\t" + ToScreamingSnake(words));
            sb.AppendLine("kebab-case\t" + ToKebab(words));
            sb.AppendLine("UPPER CASE\t" + input.ToUpperInvariant());
            sb.Append("lower case\t" + input.ToLowerInvariant());
            return sb.ToString();
        }

        private static string PascalOne(string w)
        {
            if (string.IsNullOrEmpty(w)) return "";
            if (w.Length == 1) return w.ToUpperInvariant();
            return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
        }

        private static string Join(List<string> words, string sep, bool lower)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(lower ? words[i].ToLowerInvariant() : words[i]);
            }
            return sb.ToString();
        }

        private static void Flush(List<string> words, StringBuilder sb)
        {
            if (sb.Length == 0) return;
            words.Add(sb.ToString());
            sb.Clear();
        }
    }
}
