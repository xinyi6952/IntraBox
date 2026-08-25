using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>URL 解析结果的一项（字段名 + 值）。</summary>
    public sealed class UrlPart
    {
        public UrlPart() { }
        public UrlPart(string key, string value) { Key = key; Value = value; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>URL 解析：拆出协议/主机/端口/路径/Query/Fragment 及 query 参数。纯逻辑，便于单元测试。</summary>
    public static class UrlParseHelper
    {
        public static bool TryParse(string raw, out List<UrlPart> parts, out string error)
        {
            parts = new List<UrlPart>();
            error = null;
            var input = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(input))
            {
                error = "请输入 URL";
                return false;
            }
            Uri uri;
            if (!Uri.TryCreate(input, UriKind.Absolute, out uri))
            {
                error = "不是合法的绝对 URL";
                return false;
            }

            parts.Add(new UrlPart("协议", uri.Scheme));
            parts.Add(new UrlPart("用户信息", uri.UserInfo));
            parts.Add(new UrlPart("主机", uri.Host));
            parts.Add(new UrlPart("端口", uri.IsDefaultPort ? uri.Port + "（默认）" : uri.Port.ToString()));
            parts.Add(new UrlPart("路径", uri.AbsolutePath));
            parts.Add(new UrlPart("Query", uri.Query));
            parts.Add(new UrlPart("Fragment", uri.Fragment));

            string q = uri.Query ?? "";
            if (q.StartsWith("?", StringComparison.Ordinal)) q = q.Substring(1);
            if (!string.IsNullOrEmpty(q))
            {
                var kv = q.Split('&');
                for (int i = 0; i < kv.Length; i++)
                {
                    if (string.IsNullOrEmpty(kv[i])) continue;
                    int eq = kv[i].IndexOf('=');
                    string k = eq < 0 ? kv[i] : kv[i].Substring(0, eq);
                    string v = eq < 0 ? "" : kv[i].Substring(eq + 1);
                    try { k = Uri.UnescapeDataString(k); } catch { }
                    try { v = Uri.UnescapeDataString(v); } catch { }
                    parts.Add(new UrlPart("query." + k, v));
                }
            }

            string frag = uri.Fragment ?? "";
            if (frag.StartsWith("#", StringComparison.Ordinal)) frag = frag.Substring(1);
            if (!string.IsNullOrEmpty(frag))
                parts.Add(new UrlPart("hash", frag));
            return true;
        }
    }
}
