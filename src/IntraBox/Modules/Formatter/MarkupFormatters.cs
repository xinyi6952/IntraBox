using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// 结构类文本格式化（HTML / CSS / Markdown / Nginx），纯 BCL 启发式实现。
    /// 注：此类格式语法复杂，启发式实现侧重可读的缩进与空白规范化。
    /// </summary>

    internal static class IndentUtil
    {
        public static void AppendIndent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        public static string TrimBlankLines(string s)
        {
            return Regex.Replace(s, @"[ \t]+\n", "\n").Trim();
        }

        /// <summary>
        /// 将「2 空格缩进」的文本转换为目标缩进（"\t" / "  " / "    "）。
        /// 各格式化引擎统一先生成 2 空格缩进，再由这里按需转换。
        /// </summary>
        public static string ConvertIndent(string text, string indent)
        {
            if (indent == "  " || string.IsNullOrEmpty(text)) return text;
            text = text.Replace("\r\n", "\n");
            var sb = new StringBuilder(text.Length);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                int spaces = 0;
                while (spaces < line.Length && line[spaces] == ' ') spaces++;
                int level = spaces / 2;
                for (int k = 0; k < level; k++) sb.Append(indent);
                sb.Append(line, spaces, line.Length - spaces);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>HTML 格式化（按标签缩进）</summary>
    public sealed class HtmlFormatter : ITextFormatter
    {
        public string FormatName => "HTML";
        public override string ToString() { return FormatName; }

        private static readonly HashSet<string> VoidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
        };

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            var sb = new StringBuilder();
            int depth = 0;
            int i = 0;
            while (i < input.Length)
            {
                if (input[i] == '<')
                {
                    int end = input.IndexOf('>', i);
                    if (end < 0) { sb.Append(input.Substring(i)); break; }
                    string tag = input.Substring(i, end - i + 1);

                    bool isClosing = tag.StartsWith("</", StringComparison.Ordinal);
                    bool isSelfContained = tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal);
                    string name = ExtractTagName(tag);
                    bool isVoid = name != null && VoidTags.Contains(name);
                    bool isSelfClosing = tag.EndsWith("/>", StringComparison.Ordinal);

                    if (isClosing) depth = Math.Max(0, depth - 1);

                    sb.Append('\n');
                    IndentUtil.AppendIndent(sb, depth);
                    sb.Append(tag);

                    if (!isClosing && !isSelfContained && !isVoid && !isSelfClosing) depth++;
                    i = end + 1;
                }
                else
                {
                    int next = input.IndexOf('<', i);
                    if (next < 0) { AppendText(sb, input.Substring(i)); break; }
                    AppendText(sb, input.Substring(i, next - i));
                    i = next;
                }
            }
            return IndentUtil.ConvertIndent(IndentUtil.TrimBlankLines(sb.ToString()), indent);
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            return Regex.Replace(input, @">\s+<", "><").Trim();
        }

        private static string ExtractTagName(string tag)
        {
            var m = Regex.Match(tag, @"^</?\s*([a-zA-Z0-9]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void AppendText(StringBuilder sb, string text)
        {
            var t = text.Trim();
            if (t.Length > 0) sb.Append(t);
        }
    }

    /// <summary>CSS 格式化（大括号 + 分号缩进）</summary>
    public sealed class CssFormatter : ITextFormatter
    {
        public string FormatName => "CSS";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            return IndentUtil.ConvertIndent(IndentUtil.TrimBlankLines(FormatBraced(input)), indent);
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            return Regex.Replace(input, @"\s+", " ").Replace("{ ", "{").Replace(" }", "}").Trim();
        }

        internal static string FormatBraced(string input)
        {
            var sb = new StringBuilder();
            int depth = 0;
            foreach (var c in input)
            {
                switch (c)
                {
                    case '{':
                        sb.Append(" {\n");
                        depth++;
                        IndentUtil.AppendIndent(sb, depth);
                        break;
                    case '}':
                        depth = Math.Max(0, depth - 1);
                        sb.Append('\n');
                        IndentUtil.AppendIndent(sb, depth);
                        sb.Append("}\n");
                        IndentUtil.AppendIndent(sb, depth);
                        break;
                    case ';':
                        sb.Append(";\n");
                        IndentUtil.AppendIndent(sb, depth);
                        break;
                    case '\r':
                    case '\n':
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Nginx 配置格式化（大括号 + 分号缩进，同 CSS 规则）</summary>
    public sealed class NginxFormatter : ITextFormatter
    {
        public string FormatName => "Nginx";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            return IndentUtil.ConvertIndent(IndentUtil.TrimBlankLines(CssFormatter.FormatBraced(input)), indent);
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            return Regex.Replace(input, @"\s+", " ").Trim();
        }
    }

    /// <summary>Markdown 格式化（空白与空行规范化）</summary>
    public sealed class MarkdownFormatter : ITextFormatter
    {
        public string FormatName => "Markdown";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            return Normalize(input);
        }

        public string Minify(string input, out string error)
        {
            error = null;
            return Normalize(input);
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var sb = new StringBuilder();
            foreach (var line in input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                sb.AppendLine(line.TrimEnd());
            }
            return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").TrimEnd();
        }
    }
}
