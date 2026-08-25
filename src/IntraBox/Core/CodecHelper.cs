using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IntraBox.Core
{
    /// <summary>URL / HTML / Unicode 编解码。纯逻辑，便于单元测试。</summary>
    public static class CodecHelper
    {
        /// <summary>0 URL 编码 1 URL 解码 2 HTML 编码 3 HTML 解码 4 Unicode 转义 5 Unicode 反转义</summary>
        public static string Transform(string input, int type)
        {
            switch (type)
            {
                case 1: return UrlDecode(input);
                case 2: return WebUtility.HtmlEncode(input);
                case 3: return WebUtility.HtmlDecode(input);
                case 4: return UnicodeEscape(input);
                case 5: return UnicodeUnescape(input);
                default: return UrlEncode(input);
            }
        }

        /// <summary>.NET Framework 的 EscapeDataString 对超长串会抛错，按块编码。</summary>
        public static string UrlEncode(string s)
        {
            const int chunk = 30000;
            if (s.Length <= chunk) return Uri.EscapeDataString(s);
            var sb = new StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i += chunk)
            {
                int len = Math.Min(chunk, s.Length - i);
                sb.Append(Uri.EscapeDataString(s.Substring(i, len)));
            }
            return sb.ToString();
        }

        public static string UrlDecode(string s)
        {
            return Uri.UnescapeDataString(s.Replace("+", " "));
        }

        public static string UnicodeEscape(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (var c in s)
            {
                if (c < 32 || c > 126)
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly Regex UnicodePattern = new Regex(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

        public static string UnicodeUnescape(string s)
        {
            return UnicodePattern.Replace(s, m =>
            {
                int code;
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out code))
                    return ((char)code).ToString();
                return m.Value;
            });
        }
    }
}
