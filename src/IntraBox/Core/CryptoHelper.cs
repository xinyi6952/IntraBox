using System;
using System.Security.Cryptography;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>加解密核心：AES/DES 对称（密文前缀随机 IV）、RSA 非对称，及密钥派生。纯逻辑，便于单元测试。</summary>
    public static class CryptoHelper
    {
        public static byte[] SymmetricEncrypt(bool des, byte[] key, byte[] data)
        {
            return SymmetricCrypt(true, des, key, data);
        }

        public static byte[] SymmetricDecrypt(bool des, byte[] key, byte[] data)
        {
            return SymmetricCrypt(false, des, key, data);
        }

        private static byte[] SymmetricCrypt(bool encrypt, bool des, byte[] key, byte[] data)
        {
            SymmetricAlgorithm alg = des ? (SymmetricAlgorithm)DES.Create() : Aes.Create();
            using (alg)
            {
                alg.Mode = CipherMode.CBC;
                alg.Padding = PaddingMode.PKCS7;
                alg.Key = key;
                if (encrypt)
                {
                    alg.GenerateIV();
                    using (var xf = alg.CreateEncryptor())
                    {
                        var cipher = xf.TransformFinalBlock(data, 0, data.Length);
                        var packed = new byte[alg.IV.Length + cipher.Length];
                        Buffer.BlockCopy(alg.IV, 0, packed, 0, alg.IV.Length);
                        Buffer.BlockCopy(cipher, 0, packed, alg.IV.Length, cipher.Length);
                        return packed;
                    }
                }
                int ivLen = des ? 8 : 16;
                if (data.Length <= ivLen)
                    throw new InvalidOperationException("密文过短，缺少 IV");
                var iv = new byte[ivLen];
                Buffer.BlockCopy(data, 0, iv, 0, ivLen);
                alg.IV = iv;
                var payload = new byte[data.Length - ivLen];
                Buffer.BlockCopy(data, ivLen, payload, 0, payload.Length);
                using (var xf = alg.CreateDecryptor())
                    return xf.TransformFinalBlock(payload, 0, payload.Length);
            }
        }

        /// <summary>把界面密钥文本派生为目标长度密钥：Base64 优先，否则按 SHA256 派生。</summary>
        public static byte[] DeriveKey(string text, bool des)
        {
            var t = (text ?? "").Trim();
            if (string.IsNullOrEmpty(t))
                throw new InvalidOperationException("请填写密钥，或先「生成密钥」");
            int need = des ? 8 : 32;
            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(t);
            }
            catch (FormatException)
            {
                raw = Encoding.UTF8.GetBytes(t);
            }
            if (raw.Length == need) return raw;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(raw);
                if (need == 32) return hash;
                var eight = new byte[8];
                Buffer.BlockCopy(hash, 0, eight, 0, 8);
                return eight;
            }
        }

        public static byte[] FromBase64(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("输入不能为空");
            try
            {
                return Convert.FromBase64String(text.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("不是合法的 Base64");
            }
        }

        public static RSACryptoServiceProvider LoadRsa(string xml, bool needPrivate)
        {
            var s = (xml ?? "").Trim();
            if (string.IsNullOrEmpty(s))
                throw new InvalidOperationException("请填写 RSA 密钥 XML，或先「生成密钥」");
            var rsa = new RSACryptoServiceProvider();
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(s);
            if (needPrivate && rsa.PublicOnly)
            {
                rsa.Dispose();
                throw new InvalidOperationException("当前 XML 只有公钥，解密/签名需要私钥");
            }
            return rsa;
        }

        public static byte[] RsaEncrypt(RSACryptoServiceProvider rsa, byte[] plain)
        {
            using (rsa)
            {
                int max = rsa.KeySize / 8 - 11;
                if (plain.Length > max)
                    throw new InvalidOperationException("明文过长（PKCS#1 上限 " + max + " 字节），请缩短或改用 AES");
                return rsa.Encrypt(plain, false);
            }
        }

        public static byte[] RsaDecrypt(RSACryptoServiceProvider rsa, byte[] cipher)
        {
            using (rsa)
                return rsa.Decrypt(cipher, false);
        }
    }
}
