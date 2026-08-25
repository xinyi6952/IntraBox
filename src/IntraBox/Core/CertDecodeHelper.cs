using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>证书解码：PEM/Base64 解析与信息格式化。纯逻辑，便于单元测试。</summary>
    public static class CertDecodeHelper
    {
        public static bool TryParsePem(string pem, out X509Certificate2 cert, out string error)
        {
            cert = null;
            error = null;
            var raw = (pem ?? "");
            raw = raw.Replace("-----BEGIN CERTIFICATE-----", "")
                .Replace("-----END CERTIFICATE-----", "")
                .Replace("\r", "").Replace("\n", "").Replace(" ", "");
            if (string.IsNullOrEmpty(raw))
            {
                error = "输入不能为空";
                return false;
            }
            try
            {
                var bytes = Convert.FromBase64String(raw);
                cert = new X509Certificate2(bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = "解析失败：" + ex.Message;
                return false;
            }
        }

        public static string Format(X509Certificate2 cert)
        {
            var sb = new StringBuilder();
            sb.AppendLine("使用者\t" + cert.Subject);
            sb.AppendLine("颁发者\t" + cert.Issuer);
            sb.AppendLine("有效期始\t" + cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("有效期止\t" + cert.NotAfter.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("序列号\t" + cert.SerialNumber);
            sb.AppendLine("指纹 SHA1\t" + cert.Thumbprint);
            sb.AppendLine("签名算法\t" + cert.SignatureAlgorithm.FriendlyName);
            sb.AppendLine("公钥算法\t" + cert.PublicKey.Oid.FriendlyName);
            return sb.ToString();
        }
    }
}
