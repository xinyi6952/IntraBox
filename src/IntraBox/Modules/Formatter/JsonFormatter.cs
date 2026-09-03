using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// JSON 格式化：基于 Newtonsoft.Json，支持美化/压缩与自定义缩进，
    /// 语法错误时返回行/列位置与原因。
    /// </summary>
    public sealed class JsonFormatter : ITextFormatter
    {
        public string FormatName => "JSON";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            try
            {
                // Newtonsoft 默认 2 空格缩进，再按需转换为目标缩进
                var s = JToken.Parse(input).ToString(Formatting.Indented);
                return IndentUtil.ConvertIndent(s, indent);
            }
            catch (JsonReaderException ex)
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
                return JToken.Parse(input).ToString(Formatting.None);
            }
            catch (JsonReaderException ex)
            {
                error = "第 " + ex.LineNumber + " 行 第 " + ex.LinePosition + " 列：" + ex.RootMessage();
                return null;
            }
        }
    }
}
