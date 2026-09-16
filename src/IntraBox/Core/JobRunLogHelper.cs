using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IntraBox.Core
{
    /// <summary>解析 jobs\{uid}\runs.log 分段（==== yyyy-MM-dd HH:mm:ss OK|FAIL ====）。</summary>
    public static class JobRunLogHelper
    {
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

        private static string TrimBody(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.TrimEnd('\r', '\n', ' ', '\t');
        }
    }
}
