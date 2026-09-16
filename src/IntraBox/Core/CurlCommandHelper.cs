using System;
using System.Collections.Generic;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>浏览器「复制为 cURL」解析成 REST 步骤字段。只解析，不发请求。</summary>
    public sealed class CurlCommand
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public List<KeyValuePair<string, string>> Headers { get; set; }
        public string Body { get; set; }
        public bool Insecure { get; set; }
    }

    public static class CurlCommandHelper
    {
        public static bool TryParse(string text, out CurlCommand cmd, out string error)
        {
            cmd = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "请粘贴 curl 命令";
                return false;
            }
            string src = Unwrap(text.Trim());
            var tokens = Tokenize(src);
            if (tokens.Count == 0)
            {
                error = "无法解析 curl 命令";
                return false;
            }
            int i = 0;
            if (!IsCurl(tokens[0]))
            {
                error = "请以 curl 开头（支持从浏览器复制）";
                return false;
            }
            i++;
            string method = "GET";
            string url = null;
            string body = null;
            bool insecure = false;
            var headers = new List<KeyValuePair<string, string>>();
            while (i < tokens.Count)
            {
                string t = tokens[i];
                if (t == "-X" || t == "--request")
                {
                    if (i + 1 >= tokens.Count) { error = "缺少请求方法"; return false; }
                    method = tokens[++i].ToUpperInvariant();
                }
                else if (t == "-H" || t == "--header")
                {
                    if (i + 1 >= tokens.Count) { error = "缺少 Header"; return false; }
                    string h = tokens[++i];
                    int p = h.IndexOf(':');
                    if (p <= 0) { error = "Header 格式应为 Name: Value"; return false; }
                    headers.Add(new KeyValuePair<string, string>(h.Substring(0, p).Trim(), h.Substring(p + 1).Trim()));
                }
                else if (t == "-d" || t == "--data" || t == "--data-raw" || t == "--data-binary" || t == "--data-ascii")
                {
                    if (i + 1 >= tokens.Count) { error = "缺少请求体"; return false; }
                    body = UnescapeBody(tokens[++i]);
                    if (method == "GET") method = "POST";
                }
                else if (t == "--url")
                {
                    if (i + 1 >= tokens.Count) { error = "缺少 URL"; return false; }
                    url = tokens[++i];
                }
                else if (t == "-k" || t == "--insecure")
                    insecure = true;
                else if (t == "-I" || t == "--head")
                    method = "HEAD";
                else if (t.Length > 0 && t[0] == '-' )
                {
                    if (TakesValue(t) && i + 1 < tokens.Count) i++;
                }
                else if (LooksLikeUrl(t))
                    url = t;
                i++;
            }
            if (string.IsNullOrEmpty(url))
            {
                error = "命令里没有 http:// 或 https:// 地址";
                return false;
            }
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                error = "只允许 http:// 或 https://";
                return false;
            }
            cmd = new CurlCommand
            {
                Method = string.IsNullOrEmpty(method) ? "GET" : method,
                Url = url,
                Headers = headers,
                Body = body ?? "",
                Insecure = insecure
            };
            return true;
        }

        private static bool IsCurl(string t)
        {
            string s = t.Trim('"').Trim('\'');
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 4);
            return string.Equals(s, "curl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeUrl(string t)
        {
            return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TakesValue(string flag)
        {
            if (flag == "-o" || flag == "--output" || flag == "-A" || flag == "--user-agent") return true;
            if (flag == "-u" || flag == "--user" || flag == "-m" || flag == "--max-time") return true;
            if (flag == "--connect-timeout" || flag == "-e" || flag == "--referer") return true;
            if (flag == "-b" || flag == "--cookie" || flag == "-c" || flag == "--cookie-jar") return true;
            return false;
        }

        private static string Unwrap(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[i + 1];
                    if (n == '\r' || n == '\n')
                    {
                        i++;
                        if (n == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        continue;
                    }
                    if (n == '^')
                    {
                        i++;
                        continue;
                    }
                }
                if (c == '^' && i + 1 < text.Length)
                {
                    char caretNext = text[i + 1];
                    if (caretNext == '\r' || caretNext == '\n')
                    {
                        i++;
                        if (caretNext == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        continue;
                    }
                    if (caretNext == '"' || caretNext == '\\')
                        continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string UnescapeBody(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\\"", "\"").Replace("\\'", "'");
        }

        private static List<string> Tokenize(string src)
        {
            var list = new List<string>();
            int i = 0;
            while (i < src.Length)
            {
                while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
                if (i >= src.Length) break;
                char q = src[i];
                if (q == '"' || q == '\'')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < src.Length && src[i] != q)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length)
                        {
                            sb.Append(src[i + 1]);
                            i += 2;
                            continue;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    if (i < src.Length) i++;
                    list.Add(sb.ToString());
                }
                else
                {
                    int start = i;
                    while (i < src.Length && !char.IsWhiteSpace(src[i])) i++;
                    list.Add(src.Substring(start, i - start));
                }
            }
            return list;
        }
    }
}
