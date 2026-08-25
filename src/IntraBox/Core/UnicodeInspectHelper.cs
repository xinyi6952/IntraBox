using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>单个码点信息。</summary>
    public sealed class UnicodeCodePoint
    {
        public string Ch { get; set; }
        public string Code { get; set; }
        public string Utf8 { get; set; }
        public string Utf16 { get; set; }
        public string Html { get; set; }
        public string Cat { get; set; }
    }

    /// <summary>Unicode 字符检查：切分码点并输出码点值/编码/HTML 实体/类别。纯逻辑，便于单元测试。</summary>
    public static class UnicodeInspectHelper
    {
        public static List<UnicodeCodePoint> Analyze(string text)
        {
            var rows = new List<UnicodeCodePoint>();
            var s = text ?? "";
            var utf8 = Encoding.UTF8;
            var utf16 = Encoding.Unicode;
            int i = 0;
            while (i < s.Length)
            {
                int len = char.IsSurrogatePair(s, i) ? 2 : 1;
                string ch = s.Substring(i, len);
                int cp = char.ConvertToUtf32(s, i);
                rows.Add(new UnicodeCodePoint
                {
                    Ch = ch,
                    Code = "U+" + cp.ToString("X4", CultureInfo.InvariantCulture),
                    Utf8 = ToHex(utf8.GetBytes(ch)),
                    Utf16 = ToHex(utf16.GetBytes(ch)),
                    Html = "&#" + cp.ToString(CultureInfo.InvariantCulture) + ";",
                    Cat = char.GetUnicodeCategory(s, i).ToString()
                });
                i += len;
            }
            return rows;
        }

        public static string ToHex(byte[] b)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(b[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
