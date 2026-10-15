using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IntraBox.Core
{
    public sealed class JobLogRead
    {
        public string Text;
        public bool TruncatedFile;
        public string Error;
    }

    /// <summary>解析 jobs\{uid}\runs.log 分段（==== yyyy-MM-dd HH:mm:ss OK|FAIL ====）。</summary>
    public static class JobRunLogHelper
    {
        /// <summary>约 256 KB。更大的运行日志只读文件尾部，避免一次读入整文件。</summary>
        public const int LogTailBytes = 256 * 1024;
        /// <summary>抽屉日志框最多展示的字符数。</summary>
        public const int DisplayChars = 8000;

        private static readonly Regex HeaderRe = new Regex(
            @"^====\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+(OK|FAIL)\s+====\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<JobRunLogEntry> Parse(string text)
        {
            var list = new List<JobRunLogEntry>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            JobRunLogEntry cur = null;
            var body = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                var m = HeaderRe.Match(line);
                if (m.Success)
                {
                    if (cur != null)
                    {
                        cur.Body = TrimBody(body.ToString());
                        list.Add(cur);
                    }
                    DateTime t;
                    if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                        t = DateTime.MinValue;
                    cur = new JobRunLogEntry
                    {
                        Time = t,
                        Ok = string.Equals(m.Groups[2].Value, "OK", StringComparison.OrdinalIgnoreCase)
                    };
                    body.Clear();
                    continue;
                }
                if (cur != null)
                {
                    if (body.Length > 0) body.Append('\n');
                    body.Append(line);
                }
            }
            if (cur != null)
            {
                cur.Body = TrimBody(body.ToString());
                list.Add(cur);
            }
            // 最新在前
            list.Reverse();
            return list;
        }

        public static JobLogRead ReadTail(string path)
        {
            var result = new JobLogRead();
            result.Text = "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length <= LogTailBytes)
                {
                    result.Text = File.ReadAllText(path, Encoding.UTF8);
                    return result;
                }
                result.TruncatedFile = true;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = fi.Length - LogTailBytes;
                    if (start < 0) start = 0;
                    fs.Seek(start, SeekOrigin.Begin);
                    var buf = new byte[(int)(fi.Length - start)];
                    int n = fs.Read(buf, 0, buf.Length);
                    string text = Encoding.UTF8.GetString(buf, 0, n);
                    int nl = text.IndexOf('\n');
                    if (nl >= 0 && nl + 1 < text.Length)
                        text = text.Substring(nl + 1);
                    result.Text = text;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Text = "";
            }
            return result;
        }

        /// <summary>超过展示长度时在开头说明已截断。不超过则原样返回。</summary>
        public static string ClipDisplay(string text, int maxChars)
        {
            if (maxChars < 1) maxChars = DisplayChars;
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
            return "（日志过长，仅显示最后 " + maxChars + " 字符，完整内容见 runs.log）\r\n"
                + text.Substring(text.Length - maxChars);
        }

        private static string TrimBody(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.TrimEnd('\r', '\n', ' ', '\t');
        }
    }
}
