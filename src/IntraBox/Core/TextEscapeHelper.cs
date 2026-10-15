using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>
    /// 文本转义 / 去转义。按格式选择编法；去转义会连续剥开多层
    /// （JSON/JS/C# 字符串、XML/HTML 实体、百分号编码），直到不再变化或达到层数上限。
    /// </summary>
    public static class TextEscapeHelper
    {
        public enum Kind
        {
            Json = 0,
            Markup = 1,
            Sql = 2,
            Css = 3
        }

        private const int MaxLayers = 5;

        private static readonly string[] LooseNames = new[]
        {
            "nbsp", "lt", "gt", "amp", "quot", "apos", "copy", "reg", "trade",
            "hellip", "mdash", "ndash", "bull", "times", "divide", "euro",
            "pound", "yen", "cent", "laquo", "raquo", "middot", "sect", "deg",
            "plusmn", "micro", "para"
        };

        private static readonly UTF8Encoding Utf8Strict = new UTF8Encoding(false, true);

        public static Kind KindForFormat(string formatName)
        {
            if (string.IsNullOrEmpty(formatName)) return Kind.Json;
            switch (formatName)
            {
                case "XML":
                case "HTML":
                case "Markdown":
                    return Kind.Markup;
                case "SQL":
                    return Kind.Sql;
                case "CSS":
                    return Kind.Css;
                default:
                    return Kind.Json;
            }
        }

        public static string Unescape(string input, Kind kind)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";
            string cur = input;
            for (int i = 0; i < MaxLayers; i++)
            {
                string next = UnescapeOnce(cur, kind);
                if (string.Equals(next, cur, StringComparison.Ordinal))
                    return cur;
                cur = next;
                // 只在还看得到再包一层的痕迹时继续，避免把刚还原的 \b、\n 再解成控制符。
                if (!NeedsAnotherLayer(cur))
                    return cur;
            }
            return cur;
        }

        private static bool NeedsAnotherLayer(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.IndexOf("\\\\", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("\\\"", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("\\u", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("\\U", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("\\x", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("&amp;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&gt;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&lt;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&quot;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&apos;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&nbsp;", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("&#", StringComparison.Ordinal) >= 0) return true;
            if (s.IndexOf("%5C", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("%22", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("%26", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("%3C", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("%3E", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s.IndexOf("%u", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static string Escape(string input, Kind kind)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";
            switch (kind)
            {
                case Kind.Markup: return EscapeMarkup(input);
                case Kind.Sql: return EscapeSql(input);
                case Kind.Css: return EscapeCss(input);
                default: return EscapeJson(input);
            }
        }

        private static string UnescapeOnce(string s, Kind kind)
        {
            string cur = s;
            string unwrapped;
            if (TryUnwrapJsonString(cur, out unwrapped))
                cur = unwrapped;
            else
                cur = ScanStringEscapes(cur);
            if (kind == Kind.Css)
                cur = UnescapeCss(cur);

            if (kind == Kind.Sql)
                cur = DecodeSqlQuotes(cur);
            cur = NormalizeLooseEntities(cur);
            string html = WebUtility.HtmlDecode(cur);
            if (!string.Equals(html, cur, StringComparison.Ordinal))
                cur = html;
            cur = DecodePercent(cur);
            cur = DecodeUnicodePlus(cur);
            return cur;
        }

        private static bool TryUnwrapJsonString(string text, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();
            if (t.Length < 2 || t[0] != '"' || t[t.Length - 1] != '"') return false;
            try
            {
                object obj = JsonConvert.DeserializeObject(t);
                string s = obj as string;
                if (s == null) return false;
                value = s;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ScanStringEscapes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool changed = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char n = s[i + 1];
                int cp;
                int last;
                switch (n)
                {
                    case '"':
                    case '\'':
                    case '\\':
                    case '/':
                        sb.Append(n);
                        i++;
                        changed = true;
                        break;
                    case 'b':
                        sb.Append('\b');
                        i++;
                        changed = true;
                        break;
                    case 'f':
                        sb.Append('\f');
                        i++;
                        changed = true;
                        break;
                    case 'n':
                        sb.Append('\n');
                        i++;
                        changed = true;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        changed = true;
                        break;
                    case 't':
                        sb.Append('\t');
                        i++;
                        changed = true;
                        break;
                    case '0':
                        if (i + 2 >= s.Length || !char.IsDigit(s[i + 2]))
                        {
                            sb.Append('\0');
                            i++;
                            changed = true;
                        }
                        else sb.Append(c);
                        break;
                    case 'u':
                        if (TryReadUnicodeEscape(s, i, out cp, out last))
                        {
                            AppendCodePoint(sb, cp);
                            i = last;
                            changed = true;
                        }
                        else sb.Append(c);
                        break;
                    case 'U':
                        if (TryReadHex(s, i + 2, 8, out cp) && cp <= 0x10FFFF)
                        {
                            AppendCodePoint(sb, cp);
                            i += 9;
                            changed = true;
                        }
                        else sb.Append(c);
                        break;
                    case 'x':
                        if (TryReadHex(s, i + 2, 2, out cp))
                        {
                            sb.Append((char)cp);
                            i += 3;
                            changed = true;
                        }
                        else sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return changed ? sb.ToString() : s;
        }

        /// <summary>\uXXXX、\uXXXX\uXXXX 代理对、\u{1F600}。i 指向反斜杠。</summary>
        private static bool TryReadUnicodeEscape(string s, int i, out int cp, out int last)
        {
            cp = 0;
            last = i;
            if (i + 2 >= s.Length || s[i] != '\\' || s[i + 1] != 'u') return false;
            if (s[i + 2] == '{')
            {
                int j = i + 3;
                int digits = 0;
                int value = 0;
                while (j < s.Length && digits < 6 && Hex(s[j]) >= 0)
                {
                    value = (value << 4) + Hex(s[j]);
                    j++;
                    digits++;
                }
                if (digits == 0 || j >= s.Length || s[j] != '}' || value > 0x10FFFF) return false;
                cp = value;
                last = j;
                return true;
            }
            int unit;
            if (!TryReadHex(s, i + 2, 4, out unit)) return false;
            last = i + 5;
            if (unit >= 0xD800 && unit <= 0xDBFF && last + 6 < s.Length
                && s[last + 1] == '\\' && s[last + 2] == 'u')
            {
                int low;
                if (TryReadHex(s, last + 3, 4, out low) && low >= 0xDC00 && low <= 0xDFFF)
                {
                    cp = 0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00);
                    last = last + 6;
                    return true;
                }
            }
            cp = unit;
            return true;
        }

        private static string UnescapeCss(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool changed = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }
                int j = i + 1;
                if (s[j] == '\n' || s[j] == '\r' || s[j] == '\f')
                {
                    if (s[j] == '\r' && j + 1 < s.Length && s[j + 1] == '\n') j++;
                    i = j;
                    changed = true;
                    continue;
                }
                int hex = 0;
                int count = 0;
                while (count < 6 && j < s.Length && Hex(s[j]) >= 0)
                {
                    hex = (hex << 4) + Hex(s[j]);
                    j++;
                    count++;
                }
                if (count > 0 && hex <= 0x10FFFF)
                {
                    if (j < s.Length && IsCssWs(s[j]))
                    {
                        if (s[j] == '\r' && j + 1 < s.Length && s[j + 1] == '\n') j++;
                        j++;
                    }
                    AppendCodePoint(sb, hex);
                    i = j - 1;
                    changed = true;
                    continue;
                }
                sb.Append(s[j]);
                i = j;
                changed = true;
            }
            return changed ? sb.ToString() : s;
        }

        private static bool IsCssWs(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }

        private static string DecodeSqlQuotes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("''", StringComparison.Ordinal) < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\'' && i + 1 < s.Length && s[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i++;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string NormalizeLooseEntities(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length + 8);
            bool changed = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '&')
                {
                    sb.Append(s[i]);
                    continue;
                }
                int bestLen = 0;
                string best = null;
                for (int n = 0; n < LooseNames.Length; n++)
                {
                    string name = LooseNames[n];
                    if (name.Length > bestLen && MatchAt(s, i + 1, name))
                    {
                        best = name;
                        bestLen = name.Length;
                    }
                }
                if (best == null)
                {
                    sb.Append('&');
                    continue;
                }
                int end = i + 1 + bestLen;
                if (end < s.Length && (s[end] == ';' || IsNameChar(s[end])))
                {
                    sb.Append('&');
                    continue;
                }
                sb.Append('&').Append(best).Append(';');
                i = end - 1;
                changed = true;
            }
            return changed ? sb.ToString() : s;
        }

        private static bool MatchAt(string s, int i, string name)
        {
            if (i + name.Length > s.Length) return false;
            for (int k = 0; k < name.Length; k++)
            {
                if (s[i + k] != name[k]) return false;
            }
            return true;
        }

        private static bool IsNameChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        }

        private static string DecodePercent(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool changed = false;
            var bytes = new byte[256];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && i + 5 < s.Length && (s[i + 1] == 'u' || s[i + 1] == 'U'))
                {
                    int u;
                    if (TryReadHex(s, i + 2, 4, out u))
                    {
                        sb.Append((char)u);
                        i += 5;
                        changed = true;
                        continue;
                    }
                }
                if (s[i] == '%' && i + 2 < s.Length && Hex(s[i + 1]) >= 0 && Hex(s[i + 2]) >= 0)
                {
                    int n = 0;
                    int j = i;
                    while (j + 2 < s.Length && s[j] == '%' && Hex(s[j + 1]) >= 0 && Hex(s[j + 2]) >= 0)
                    {
                        if (n == bytes.Length) Array.Resize(ref bytes, bytes.Length * 2);
                        bytes[n++] = (byte)((Hex(s[j + 1]) << 4) + Hex(s[j + 2]));
                        j += 3;
                    }
                    string decoded;
                    if (TryUtf8(bytes, n, out decoded))
                    {
                        sb.Append(decoded);
                        i = j - 1;
                        changed = true;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return changed ? sb.ToString() : s;
        }

        private static bool TryUtf8(byte[] bytes, int count, out string text)
        {
            text = null;
            try
            {
                text = Utf8Strict.GetString(bytes, 0, count);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static string DecodeUnicodePlus(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int plus = s.IndexOf('+');
            if (plus < 1) return s;
            var sb = new StringBuilder(s.Length);
            bool changed = false;
            for (int i = 0; i < s.Length; i++)
            {
                if ((s[i] == 'U' || s[i] == 'u') && i + 2 < s.Length && s[i + 1] == '+' && Hex(s[i + 2]) >= 0)
                {
                    int j = i + 2;
                    int value = 0;
                    int digits = 0;
                    while (digits < 6 && j < s.Length && Hex(s[j]) >= 0)
                    {
                        value = (value << 4) + Hex(s[j]);
                        j++;
                        digits++;
                    }
                    if (digits >= 4 && value <= 0x10FFFF)
                    {
                        AppendCodePoint(sb, value);
                        i = j - 1;
                        changed = true;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return changed ? sb.ToString() : s;
        }

        private static string EscapeJson(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string EscapeMarkup(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    case '\u00A0': sb.Append("&nbsp;"); break;
                    default:
                        if (c < 32 && c != '\n' && c != '\r' && c != '\t')
                        {
                            sb.Append("&#");
                            sb.Append((int)c);
                            sb.Append(';');
                        }
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string EscapeSql(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string EscapeCss(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '"' || c == '\'' || c == '<' || c == '>' || c == '&' || c < 32)
                    sb.Append('\\').Append(((int)c).ToString("x")).Append(' ');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static void AppendCodePoint(StringBuilder sb, int cp)
        {
            if (cp < 0 || cp > 0x10FFFF)
            {
                sb.Append('\uFFFD');
                return;
            }
            if (cp >= 0xD800 && cp <= 0xDFFF)
            {
                sb.Append((char)cp);
                return;
            }
            if (cp <= 0xFFFF)
            {
                sb.Append((char)cp);
                return;
            }
            cp -= 0x10000;
            sb.Append((char)(0xD800 + (cp >> 10)));
            sb.Append((char)(0xDC00 + (cp & 0x3FF)));
        }

        private static bool TryReadHex(string s, int i, int n, out int value)
        {
            value = 0;
            if (i < 0 || n <= 0 || i + n > s.Length) return false;
            for (int k = 0; k < n; k++)
            {
                int h = Hex(s[i + k]);
                if (h < 0) return false;
                value = (value << 4) + h;
            }
            return true;
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
