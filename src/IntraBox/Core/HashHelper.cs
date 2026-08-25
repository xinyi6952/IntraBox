using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 哈希计算纯逻辑（供界面与测试共用）。
    /// </summary>
    public static class HashHelper
    {
        /// <summary>对字节数据计算指定算法的十六进制哈希。</summary>
        public static string ComputeHex(string algorithm, byte[] data)
        {
            using (var algo = CreateAlgorithm(algorithm))
                return ToHex(algo.ComputeHash(data));
        }

        /// <summary>对文件计算指定算法的十六进制哈希。</summary>
        public static string ComputeFileHex(string algorithm, string path)
        {
            using (var algo = CreateAlgorithm(algorithm))
            using (var stream = File.OpenRead(path))
                return ToHex(algo.ComputeHash(stream));
        }

        /// <summary>字节数组转小写十六进制字符串。</summary>
        public static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static HashAlgorithm CreateAlgorithm(string algorithm)
        {
            switch ((algorithm ?? "").ToUpperInvariant())
            {
                case "MD5": return MD5.Create();
                case "SHA1": return SHA1.Create();
                case "SHA512": return SHA512.Create();
                default: return SHA256.Create();
            }
        }
    }
}
