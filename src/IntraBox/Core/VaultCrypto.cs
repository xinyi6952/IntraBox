using System;
using System.Security.Cryptography;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 账号备忘口令派生与字段加密。口令经 PBKDF2 得到 AES-256 密钥，密文为 IV+密文的 Base64。
    /// 不保存用户口令。
    /// </summary>
    public static class VaultCrypto
    {
        public const int Iterations = 80000;
        public const int SaltSize = 16;
        public const int KeySize = 32;
        public const string VerifierPlain = "IntraBox.Vault.v1";

        public static byte[] NewSalt()
        {
            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);
            return salt;
        }

        public static byte[] DeriveKey(string password, byte[] salt)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("请填写查看密码");
            if (salt == null || salt.Length != SaltSize)
                throw new InvalidOperationException("盐值无效");
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations))
                return kdf.GetBytes(KeySize);
        }

        public static string MakeVerifier(byte[] key)
        {
            return EncryptString(key, VerifierPlain);
        }

        public static bool CheckVerifier(byte[] key, string verifier)
        {
            if (key == null || string.IsNullOrEmpty(verifier)) return false;
            try
            {
                string plain = DecryptString(key, verifier);
                return string.Equals(plain, VerifierPlain, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static string EncryptString(byte[] key, string plain)
        {
            if (key == null || key.Length != KeySize)
                throw new InvalidOperationException("密钥无效");
            byte[] data = Encoding.UTF8.GetBytes(plain ?? "");
            byte[] packed = CryptoHelper.SymmetricEncrypt(false, key, data);
            return Convert.ToBase64String(packed);
        }

        public static string DecryptString(byte[] key, string cipher)
        {
            if (key == null || key.Length != KeySize)
                throw new InvalidOperationException("密钥无效");
            if (string.IsNullOrEmpty(cipher))
                return "";
            byte[] packed = Convert.FromBase64String(cipher);
            byte[] data = CryptoHelper.SymmetricDecrypt(false, key, packed);
            return Encoding.UTF8.GetString(data);
        }
    }
}
