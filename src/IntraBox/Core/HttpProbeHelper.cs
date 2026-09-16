using System;

namespace IntraBox.Core
{
    /// <summary>编排 REST/SOAP「测通」用：解析 http(s) 地址与默认端口。变量替换走 JobVarHelper.TrySubstitute。本类不发请求。</summary>
    public sealed class HttpProbeTarget
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public bool UseSsl { get; set; }
        public string Url { get; set; }
    }

    public static class HttpProbeHelper
    {
        public static bool TryParseUrl(string url, out HttpProbeTarget target, out string error)
        {
            target = null;
            error = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                error = "请填写 http:// 或 https:// 地址";
                return false;
            }
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) || uri == null)
            {
                error = "地址无法解析";
                return false;
            }
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                error = "只允许 http:// 或 https://";
                return false;
            }
            if (string.IsNullOrEmpty(uri.Host))
            {
                error = "地址没有主机名";
                return false;
            }
            bool ssl = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            int port = uri.IsDefaultPort ? (ssl ? 443 : 80) : uri.Port;
            if (port < 1 || port > 65535)
            {
                error = "端口无效";
                return false;
            }
            target = new HttpProbeTarget
            {
                Host = uri.Host,
                Port = port,
                UseSsl = ssl,
                Url = uri.AbsoluteUri
            };
            return true;
        }
    }
}
