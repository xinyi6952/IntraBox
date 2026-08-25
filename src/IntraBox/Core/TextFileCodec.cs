using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 文本文件编解码：BOM 优先，无 BOM 时 UTF-8 严格解码，失败回退 GBK。
    /// </summary>
    public sealed class EncodingChoice
    {
        public string Display { get; set; }
        public int CodePage { get; set; }
        public bool EmitBom { get; set; }
        public bool IsAuto { get; set; }

        public override string ToString()
        {
            return Display;
        }
    }

    public static class TextFileCodec
    {
        public static readonly EncodingChoice Auto = new EncodingChoice { Display = "自动检测", IsAuto = true };
        public static readonly EncodingChoice Utf8 = new EncodingChoice { Display = "UTF-8", CodePage = 65001 };
        public static readonly EncodingChoice Utf8Bom = new EncodingChoice { Display = "UTF-8 BOM", CodePage = 65001, EmitBom = true };
        public static readonly EncodingChoice Gbk = new EncodingChoice { Display = "GBK", CodePage = 936 };
        public static readonly EncodingChoice Gb2312 = new EncodingChoice { Display = "GB2312", CodePage = 936 };
        public static readonly EncodingChoice Big5 = new EncodingChoice { Display = "Big5", CodePage = 950 };
        public static readonly EncodingChoice Latin1 = new EncodingChoice { Display = "ISO-8859-1", CodePage = 28591 };
        public static readonly EncodingChoice Utf16Le = new EncodingChoice { Display = "UTF-16 LE", CodePage = 1200 };
        public static readonly EncodingChoice Utf16Be = new EncodingChoice { Display = "UTF-16 BE", CodePage = 1201 };
        public static readonly EncodingChoice Utf32Le = new EncodingChoice { Display = "UTF-32 LE", CodePage = 12000 };
        public static readonly EncodingChoice Utf32Be = new EncodingChoice { Display = "UTF-32 BE", CodePage = 12001 };

        public static IList<EncodingChoice> All
        {
            get
            {
                return new EncodingChoice[]
                {
                    Auto, Utf8, Utf8Bom, Gbk, Gb2312, Big5, Latin1, Utf16Le, Utf16Be, Utf32Le, Utf32Be
                };
            }
        }

        public static Encoding Resolve(EncodingChoice choice)
        {
            if (choice == null || choice.IsAuto) return new UTF8Encoding(false);
            if (choice.CodePage == 65001) return new UTF8Encoding(choice.EmitBom);
            if (choice.CodePage == 1200) return Encoding.Unicode;
            if (choice.CodePage == 1201) return Encoding.BigEndianUnicode;
            if (choice.CodePage == 12000) return new UTF32Encoding(false, true);
            if (choice.CodePage == 12001) return new UTF32Encoding(true, true);
            try
            {
                return Encoding.GetEncoding(choice.CodePage);
            }
            catch (Exception)
            {
                return new UTF8Encoding(false);
            }
        }

        /// <summary>
        /// 检测编码。uncertain=true 表示无 BOM 且 UTF-8 严格解码失败，已按 GBK/系统 ANSI 回退。
        /// </summary>
        public static Encoding Detect(byte[] bytes, out EncodingChoice matched, out bool uncertain)
        {
            if (bytes == null) bytes = new byte[0];
            uncertain = false;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                matched = Utf8Bom;
                return new UTF8Encoding(true);
            }
            // UTF-32 必须先于 UTF-16：UTF-32 LE BOM 以 FF FE 00 00 开头
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                matched = Utf32Le;
                return new UTF32Encoding(false, true);
            }
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                matched = Utf32Be;
                return new UTF32Encoding(true, true);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                matched = Utf16Le;
                return Encoding.Unicode;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                matched = Utf16Be;
                return Encoding.BigEndianUnicode;
            }

            if (TryUtf8Strict(bytes))
            {
                matched = Utf8;
                return new UTF8Encoding(false, true);
            }

            uncertain = true;
            try
            {
                matched = Gbk;
                return Encoding.GetEncoding(936);
            }
            catch
            {
                matched = Gbk;
                return Encoding.Default;
            }
        }

        public static string ReadAll(string path, EncodingChoice choice, long maxBytes, out EncodingChoice used)
        {
            bool uncertain;
            return ReadAll(path, choice, maxBytes, out used, out uncertain);
        }

        public static string ReadAll(string path, EncodingChoice choice, long maxBytes, out EncodingChoice used, out bool uncertain)
        {
            uncertain = false;
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length > maxBytes)
                    throw new InvalidOperationException("文件过大（超过 " + (maxBytes / 1024 / 1024) + "MB 限制）");
                bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            Encoding enc;
            if (choice == null || choice.IsAuto)
                enc = Detect(bytes, out used, out uncertain);
            else
            {
                used = choice;
                enc = Resolve(choice);
            }
            return DecodeSkippingPreamble(enc, bytes);
        }

        public static void WriteAll(string path, string text, EncodingChoice choice)
        {
            var enc = Resolve(choice == null || choice.IsAuto ? Utf8 : choice);
            var bytes = enc.GetBytes(text ?? "");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var preamble = enc.GetPreamble();
                if (preamble != null && preamble.Length > 0)
                    fs.Write(preamble, 0, preamble.Length);
                fs.Write(bytes, 0, bytes.Length);
            }
        }

        private static bool TryUtf8Strict(byte[] bytes)
        {
            try
            {
                new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string DecodeSkippingPreamble(Encoding enc, byte[] bytes)
        {
            if (enc == null) enc = new UTF8Encoding(false);
            if (bytes == null || bytes.Length == 0) return "";
            var pre = enc.GetPreamble();
            int start = 0;
            if (pre != null && pre.Length > 0 && bytes.Length >= pre.Length)
            {
                bool match = true;
                for (int i = 0; i < pre.Length; i++)
                {
                    if (bytes[i] != pre[i]) { match = false; break; }
                }
                if (match) start = pre.Length;
            }
            return enc.GetString(bytes, start, bytes.Length - start);
        }
    }
}
