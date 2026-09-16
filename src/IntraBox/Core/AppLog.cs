using System;
using System.IO;
using System.Text;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 应用排查日志：固定写到 %LOCALAPPDATA%\IntraBox\logs\yyyy-MM-dd.log（按天切分）。
    /// 与数据根 error.log / gc.log 分开；不随自定义数据目录迁移。写入失败静默。
    /// </summary>
    public static class AppLog
    {
        private static readonly object Sync = new object();
        private static string _dir;

        public static string LogsDir
        {
            get
            {
                if (_dir != null) return _dir;
                lock (Sync)
                {
                    if (_dir != null) return _dir;
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _dir = Path.Combine(local, "IntraBox", "logs");
                    return _dir;
                }
            }
        }

        public static string PathFor(DateTime day)
        {
            return Path.Combine(LogsDir, day.ToString("yyyy-MM-dd") + ".log");
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        public static void Exception(Exception ex)
        {
            Write("ERROR", "未处理异常", ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var now = DateTime.Now;
                var sb = new StringBuilder(256);
                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [");
                sb.Append(level);
                sb.Append("] [T");
                sb.Append(Thread.CurrentThread.ManagedThreadId);
                sb.Append("] ");
                sb.Append(message ?? "");
                if (ex != null)
                {
                    sb.AppendLine();
                    var e = ex;
                    while (e != null)
                    {
                        sb.Append("  ");
                        sb.Append(e.GetType().FullName);
                        sb.Append(": ");
                        sb.AppendLine(e.Message);
                        if (!string.IsNullOrEmpty(e.StackTrace))
                            sb.AppendLine(e.StackTrace);
                        e = e.InnerException;
                        if (e != null) sb.AppendLine("  ---");
                    }
                }
                sb.AppendLine();

                string path = PathFor(now.Date);
                lock (Sync)
                {
                    Directory.CreateDirectory(LogsDir);
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                /* 日志失败不影响运行 */
            }
        }
    }
}
