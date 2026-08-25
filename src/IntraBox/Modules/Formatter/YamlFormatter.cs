using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// YAML 格式化：先校验语法，再做行级处理（保留注释）。
    /// 说明：YamlStream 往返会丢弃注释，因此这里只用它做语法校验；
    /// 实际美化采用「按现有层级重新对齐」，压缩采用「去掉空行与行尾空格」，两者都保留注释。
    /// </summary>
    public sealed class YamlFormatter : ITextFormatter
    {
        public string FormatName => "YAML";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (!Validate(input, out error)) return null;
            return NormalizeIndent(input, indent);
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (!Validate(input, out error)) return null;
            return RemoveBlankLines(input);
        }

        /// <summary>用 YamlStream 校验语法，错误时输出行/列位置。</summary>
        private static bool Validate(string input, out string error)
        {
            try
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(input));
                error = null;
                return true;
            }
            catch (YamlException ex)
            {
                error = "第 " + (ex.Start.Line + 1) + " 行 第 " + (ex.Start.Column + 1) + " 列：" + ex.Message;
                return false;
            }
        }

        /// <summary>压缩：去掉空行与行尾空格，保留注释与结构。</summary>
        private static string RemoveBlankLines(string input)
        {
            var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue; // 跳过空行
                sb.AppendLine(line.TrimEnd());
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>美化：按现有缩进层级重新对齐（保留注释与相对层级）。</summary>
        private static string NormalizeIndent(string input, string indent)
        {
            var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // 收集所有不同的缩进宽度，映射为层级（0,1,2,...）
            var widths = new SortedSet<int>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                widths.Add(CountLeadingWhitespace(line));
            }

            var levelOf = new Dictionary<int, int>();
            int rank = 0;
            foreach (var w in widths) levelOf[w] = rank++;

            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine(); continue; }
                int spaces = CountLeadingWhitespace(line);
                int lvl = levelOf[spaces];
                sb.Append(Repeat(indent, lvl));
                sb.AppendLine(line.Substring(spaces));
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static int CountLeadingWhitespace(string s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            return i;
        }

        private static string Repeat(string s, int n)
        {
            var sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }
    }
}
