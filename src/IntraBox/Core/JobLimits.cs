using System;
using System.IO;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 任务编排资源边界：长任务超时、响应体上限、抽取变量截断（纯函数，便于单测与 UI 文案共用）。
    /// </summary>
    public static class JobLimits
    {
        /// <summary>一条编排最多步骤数（超出只读打开、不能保存）。</summary>
        public const int MaxStepsPerJob = 5;

        /// <summary>未填写超时（0）表示不限制；填写时最长 4 小时。</summary>
        public const int DefaultTimeoutSec = 0;

        /// <summary>允许的最大超时（秒）= 4 小时，支持半小时以上长任务。</summary>
        public const int MaxTimeoutSec = 14400;

        /// <summary>HTTP/SOAP 响应体最大字节数（UTF-8 读入上限）；超出截断并记日志，避免撑爆内存。</summary>
        public const int MaxResponseBytes = 2 * 1024 * 1024;

        /// <summary>写入 ${变量} 的单值最大字符数；超出截断。</summary>
        public const int MaxVarChars = 256 * 1024;

        public static bool IsStepOverflow(JobItem job)
        {
            return job != null && job.Steps != null && job.Steps.Count > MaxStepsPerJob;
        }

        public static int ClampTimeoutSec(int timeoutSec)
        {
            if (timeoutSec < 1) return 0;
            if (timeoutSec > MaxTimeoutSec) return MaxTimeoutSec;
            return timeoutSec;
        }

        /// <summary>HttpWebRequest 毫秒超时；未设置时为 Infinite。</summary>
        public static int ToHttpTimeoutMs(int timeoutSec)
        {
            int sec = ClampTimeoutSec(timeoutSec);
            if (sec < 1) return System.Threading.Timeout.Infinite;
            return sec * 1000;
        }

        /// <summary>等进程毫秒数；未设置时返回 0 表示一直等到取消或退出。</summary>
        public static int ToWaitTimeoutMs(int timeoutSec)
        {
            int sec = ClampTimeoutSec(timeoutSec);
            if (sec < 1) return 0;
            return sec * 1000;
        }

        public static string TimeoutHint()
        {
            return "超时秒：REST/SOAP 为整次请求（含等待响应）；程序/批处理/Python 为等待退出。"
                + "默认不填表示不限制（离开模块仍会取消）。若填写，最长 " + MaxTimeoutSec + " 秒（"
                + (MaxTimeoutSec / 3600) + " 小时），半小时以上请填 ≥1800。"
                + "离开模块会取消后续步骤；正在进行的 HTTP 也会中止。";
        }

        /// <summary>从流按字节上限读 UTF-8 文本；truncated 表示触达上限后停止。</summary>
        public static string ReadUtf8Limited(Stream stream, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (stream == null) return "";
            if (maxBytes < 1) maxBytes = MaxResponseBytes;

            var ms = new MemoryStream();
            var buf = new byte[8192];
            int total = 0;
            while (true)
            {
                int want = Math.Min(buf.Length, maxBytes - total);
                if (want <= 0)
                {
                    truncated = true;
                    break;
                }
                int n = stream.Read(buf, 0, want);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
                total += n;
                if (total >= maxBytes)
                {
                    // 若还能再读到字节，视为截断
                    int peek = stream.ReadByte();
                    if (peek >= 0)
                    {
                        truncated = true;
                    }
                    break;
                }
            }

            string text = Encoding.UTF8.GetString(ms.ToArray());
            if (truncated)
                text = text + "\n…[响应已截断，上限 " + FormatBytes(maxBytes) + "]";
            return text;
        }

        public static string CapVarValue(string value, out bool truncated)
        {
            truncated = false;
            if (value == null) return "";
            if (value.Length <= MaxVarChars) return value;
            truncated = true;
            return value.Substring(0, MaxVarChars) + "…[变量已截断，上限 " + MaxVarChars + " 字符]";
        }

        public static string FormatBytes(int bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)) + "MB";
            if (bytes >= 1024)
                return (bytes / 1024) + "KB";
            return bytes + "B";
        }

        public static string ResponseHint()
        {
            return "超大返回：响应体最多读入 " + FormatBytes(MaxResponseBytes)
                + "，超出截断后仍可做 JSONPath/XPath（仅针对已读入部分）。"
                + "抽取进 ${变量} 单值最多 " + MaxVarChars + " 字符。"
                + "日志只留摘要。跑完会释放响应缓冲并尝试回收工作集。"
                + "勿用本工具下载超大文件/二进制包。";
        }

        public static string MemoryHint()
        {
            return "内存：长任务期间客户端会保持运行（Loading 遮罩）；"
                + "峰值主要来自当次响应与变量。跑完后自动 GC 并尝试压工作集。"
                + "若任务管理器数字仍偏高，可点右下角内存回收或藏托盘。";
        }
    }
}
