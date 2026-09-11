using System;
using System.IO;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 数据根文本日志：超过 1MB 只留尾部 256KB，避免托盘常驻把目录写满。
    /// error.log / gc.log 共用。写入失败静默。
    /// </summary>
    public static class LogFileHelper
    {
        public const long MaxBytes = 1024L * 1024L;
        public const int KeepBytes = 256 * 1024;

        public static void RotateIfNeeded(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Length <= MaxBytes) return;

            byte[] tail;
            using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int take = (int)Math.Min(KeepBytes, fs.Length);
                fs.Seek(-take, SeekOrigin.End);
                tail = new byte[take];
                int read = 0;
                while (read < take)
                {
                    int n = fs.Read(tail, read, take - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < take)
                {
                    var smaller = new byte[read];
                    Buffer.BlockCopy(tail, 0, smaller, 0, read);
                    tail = smaller;
                }
            }
            int start = 0;
            for (int i = 0; i < tail.Length; i++)
            {
                if (tail[i] == (byte)'\n') { start = i + 1; break; }
            }
            using (var fs = File.Create(path))
            {
                var header = Encoding.UTF8.GetBytes("==== log rotated ====\r\n");
                fs.Write(header, 0, header.Length);
                if (start < tail.Length)
                    fs.Write(tail, start, tail.Length - start);
            }
        }

        public static void Append(string path, string text)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) return;
            try
            {
                DataPaths.Initialize();
                RotateIfNeeded(path);
                File.AppendAllText(path, text);
            }
            catch { /* 日志失败不影响运行 */ }
        }
    }
}
