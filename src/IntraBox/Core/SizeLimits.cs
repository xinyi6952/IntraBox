using System;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 文件/文本体积上限：由设置页配置，默认 10MB，硬顶 50MB。
    /// 打开文件、粘贴超大文本、剪贴板图片均走这里。
    /// </summary>
    public static class SizeLimits
    {
        public const int DefaultMaxFileMb = 10;
        public const int MinFileMb = 1;
        public const int AbsoluteMaxFileMb = 200;

        public static int MaxFileMb
        {
            get { return ClampMb(ConfigManager.Instance.Settings.MaxFileSizeMb); }
        }

        public static long MaxFileBytes
        {
            get { return (long)MaxFileMb * 1024L * 1024L; }
        }

        /// <summary>按 UTF-16 估算的字符上限（约等于字节上限的一半）。</summary>
        public static int MaxTextChars
        {
            get
            {
                long chars = MaxFileBytes / 2;
                if (chars > int.MaxValue) return int.MaxValue;
                if (chars < 1) return 1;
                return (int)chars;
            }
        }

        /// <summary>
        /// 在内存中做格式化/比对等「生成全尺寸副本」操作的硬顶字符数。
        /// 这类操作会同时存在输入串、StringBuilder 输出、缩进转换的 Split 数组等多份全尺寸副本，
        /// 32 位进程 ~2GB 地址空间下极易 OOM。64 位下放宽。
        /// 与 MaxFileMb（文件落盘上限）解耦：即便用户把上限调到 200MB，格式化仍受此硬顶约束。
        /// </summary>
        public static int MaxFormatChars
        {
            get { return IntPtr.Size == 8 ? 200_000_000 : 5_000_000; }
        }

        public static int ClampMb(int mb)
        {
            if (mb < MinFileMb) return DefaultMaxFileMb;
            if (mb > AbsoluteMaxFileMb) return AbsoluteMaxFileMb;
            return mb;
        }

        public static bool TryCheckFile(string path, out string error)
        {
            error = null;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    error = "文件不存在";
                    return false;
                }
                if (fi.Length > MaxFileBytes)
                {
                    error = "文件过大（超过 " + MaxFileMb + " MB 限制），当前 " + FormatBytes(fi.Length)
                        + "。可在「设置」中调整上限（最高 " + AbsoluteMaxFileMb + " MB）。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryCheckText(string text, out string error)
        {
            error = null;
            if (text != null && text.Length > MaxTextChars)
            {
                error = "文本过大（超过约 " + MaxFileMb + " MB 限制）。可在「设置」中调整上限。";
                return false;
            }
            return true;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }
    }
}

