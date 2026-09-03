using System.Xml;
using System.Xml.Linq;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// XML 格式化：基于 LINQ to XML（纯 BCL），美化/压缩与自定义缩进，
    /// 语法错误时返回行/列位置与原因。
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
                // XDocument.ToString 默认 2 空格缩进，再按需转换
                var s = XDocument.Parse(input).ToString();
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
                return XDocument.Parse(input).ToString(SaveOptions.DisableFormatting);
            }
            catch (XmlException ex)
            {
                error = "第 " + ex.LineNumber + " 行 第 " + ex.LinePosition + " 列：" + ex.RootMessage();
                return null;
            }
        }
    }
}
