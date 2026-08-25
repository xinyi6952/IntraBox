using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>证书解码纯逻辑测试（CertDecodeHelper）。</summary>
    [TestClass]
    public class CertDecodeTests
    {
        private static X509Certificate2 CreateSelfSigned()
        {
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=IntraBox Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
            }
        }

        [TestMethod]
        public void Format_自签名证书_含主体信息()
        {
            using (var cert = CreateSelfSigned())
            {
                string s = CertDecodeHelper.Format(cert);
                Assert.IsTrue(s.IndexOf("CN=IntraBox Test", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(s.IndexOf("有效期始", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(s.IndexOf("指纹 SHA1", StringComparison.Ordinal) >= 0);
            }
        }

        [TestMethod]
        public void TryParsePem_PEM文本_解析出证书()
        {
            using (var cert = CreateSelfSigned())
            {
                var der = cert.Export(X509ContentType.Cert);
                var pem = "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(der) + "\n-----END CERTIFICATE-----";
                X509Certificate2 parsed;
                string err;
                Assert.IsTrue(CertDecodeHelper.TryParsePem(pem, out parsed, out err));
                Assert.IsNotNull(parsed);
                Assert.IsTrue(parsed.Subject.IndexOf("CN=IntraBox Test", StringComparison.Ordinal) >= 0);
                parsed.Dispose();
            }
        }

        [TestMethod]
        public void TryParsePem_空或非法_返回false并给出错误()
        {
            X509Certificate2 cert;
            string err;
            Assert.IsFalse(CertDecodeHelper.TryParsePem("", out cert, out err));
            Assert.IsFalse(string.IsNullOrEmpty(err));
            Assert.IsFalse(CertDecodeHelper.TryParsePem("不是证书", out cert, out err));
            Assert.IsFalse(string.IsNullOrEmpty(err));
        }
    }
}
