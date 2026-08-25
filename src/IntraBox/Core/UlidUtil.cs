using System;
using System.Security.Cryptography;

namespace IntraBox.Core
{
    /// <summary>
    /// ULID：48 位时间戳 + 80 位随机，Crockford Base32，26 字符，可按时间排序。
    /// </summary>
    public static class UlidUtil
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string NewUlid()
        {
            var bytes = new byte[16];
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bytes[0] = (byte)((ms >> 40) & 0xFF);
            bytes[1] = (byte)((ms >> 32) & 0xFF);
            bytes[2] = (byte)((ms >> 24) & 0xFF);
            bytes[3] = (byte)((ms >> 16) & 0xFF);
            bytes[4] = (byte)((ms >> 8) & 0xFF);
            bytes[5] = (byte)(ms & 0xFF);
            var rnd = new byte[10];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(rnd);
            Buffer.BlockCopy(rnd, 0, bytes, 6, 10);
            return Encode(bytes);
        }

        private static string Encode(byte[] bytes)
        {
            var chars = new char[26];
            chars[0] = Alphabet[(bytes[0] & 0xE0) >> 5];
            chars[1] = Alphabet[bytes[0] & 0x1F];
            chars[2] = Alphabet[(bytes[1] & 0xF8) >> 3];
            chars[3] = Alphabet[((bytes[1] & 0x07) << 2) | ((bytes[2] & 0xC0) >> 6)];
            chars[4] = Alphabet[(bytes[2] & 0x3E) >> 1];
            chars[5] = Alphabet[((bytes[2] & 0x01) << 4) | ((bytes[3] & 0xF0) >> 4)];
            chars[6] = Alphabet[((bytes[3] & 0x0F) << 1) | ((bytes[4] & 0x80) >> 7)];
            chars[7] = Alphabet[(bytes[4] & 0x7C) >> 2];
            chars[8] = Alphabet[((bytes[4] & 0x03) << 3) | ((bytes[5] & 0xE0) >> 5)];
            chars[9] = Alphabet[bytes[5] & 0x1F];
            chars[10] = Alphabet[(bytes[6] & 0xF8) >> 3];
            chars[11] = Alphabet[((bytes[6] & 0x07) << 2) | ((bytes[7] & 0xC0) >> 6)];
            chars[12] = Alphabet[(bytes[7] & 0x3E) >> 1];
            chars[13] = Alphabet[((bytes[7] & 0x01) << 4) | ((bytes[8] & 0xF0) >> 4)];
            chars[14] = Alphabet[((bytes[8] & 0x0F) << 1) | ((bytes[9] & 0x80) >> 7)];
            chars[15] = Alphabet[(bytes[9] & 0x7C) >> 2];
            chars[16] = Alphabet[((bytes[9] & 0x03) << 3) | ((bytes[10] & 0xE0) >> 5)];
            chars[17] = Alphabet[bytes[10] & 0x1F];
            chars[18] = Alphabet[(bytes[11] & 0xF8) >> 3];
            chars[19] = Alphabet[((bytes[11] & 0x07) << 2) | ((bytes[12] & 0xC0) >> 6)];
            chars[20] = Alphabet[(bytes[12] & 0x3E) >> 1];
            chars[21] = Alphabet[((bytes[12] & 0x01) << 4) | ((bytes[13] & 0xF0) >> 4)];
            chars[22] = Alphabet[((bytes[13] & 0x0F) << 1) | ((bytes[14] & 0x80) >> 7)];
            chars[23] = Alphabet[(bytes[14] & 0x7C) >> 2];
            chars[24] = Alphabet[((bytes[14] & 0x03) << 3) | ((bytes[15] & 0xE0) >> 5)];
            chars[25] = Alphabet[bytes[15] & 0x1F];
            return new string(chars);
        }
    }
}
