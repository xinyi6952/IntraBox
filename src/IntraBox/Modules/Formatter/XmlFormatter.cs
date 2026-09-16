using System.Text;
using System.Xml;
using System.Xml.Linq;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// XML 格式化：基于 LINQ to XML（纯 BCL），美化/压缩与自定义缩进，
    /// 语法错误时返回行/列位置与原因。
    /// 输入含 XML 声明时保留 version/standalone；encoding 一律按 UTF-8 回写（字符串处理，不落盘 UTF-16）。
    /// 无声明则不额外生成。
    /// </summary>
    public sealed class XmlFormatter : ITextFormatter
    {
        public string FormatName => "XML";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            try
            {
                // XDocument.ToString 默认 2 空格缩进，再按需转换；声明需单独拼回
                var doc = XDocument.Parse(input);
                var s = WithDeclaration(doc, doc.ToString(), true);
                return IndentUtil.ConvertIndent(s, indent);
            }
            catch (XmlException ex)
            {
                error = "第 " + ex.LineNumber + " 行 第 " + ex.LinePosition + " 列：" + ex.RootMessage();
                return null;
            }
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            try
            {
                var doc = XDocument.Parse(input);
                return WithDeclaration(doc, doc.ToString(SaveOptions.DisableFormatting), false);
            }
            catch (XmlException ex)
            {
                error = "第 " + ex.LineNumber + " 行 第 " + ex.LinePosition + " 列：" + ex.RootMessage();
                return null;
            }
        }

        /// <summary>
        /// XDocument.ToString 不输出 Declaration；拼回时 encoding 固定 UTF-8（不以声明里的 utf-16 改写出编码）。
        /// </summary>
        private static string WithDeclaration(XDocument doc, string body, bool beautify)
        {
            if (doc.Declaration == null) return body;
            var d = doc.Declaration;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"");
            sb.Append(string.IsNullOrEmpty(d.Version) ? "1.0" : d.Version);
            sb.Append("\" encoding=\"UTF-8\"");
            if (!string.IsNullOrEmpty(d.Standalone))
            {
                sb.Append(" standalone=\"");
                sb.Append(d.Standalone);
                sb.Append("\"");
            }
            sb.Append("?>");
            string decl = sb.ToString();
            if (string.IsNullOrEmpty(body)) return decl;
            return beautify ? (decl + "\n" + body) : (decl + body);
        }
    }
}
