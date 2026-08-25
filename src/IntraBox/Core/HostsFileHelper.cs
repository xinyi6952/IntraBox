using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// Hosts 文件一行：注释/空行原样保留；映射行可在表格里改 IP/域名/启用状态。
    /// </summary>
    public sealed class HostsEntry
    {
        public bool IsMapping { get; set; }
        public string Raw { get; set; }
        public bool Enabled { get; set; }
        public string Ip { get; set; }
        public string Host { get; set; }
        public string Comment { get; set; }
    }

    /// <summary>
    /// 按原文件顺序读写 Hosts：只改映射行，不把注释和空行抽走或重排。
    /// </summary>
    public static class HostsFileHelper
    {
        public static List<HostsEntry> Parse(string text)
        {
            var list = new List<HostsEntry>();
            if (text == null) return list;
            int i = 0;
            while (i < text.Length)
            {
                int end = i;
                while (end < text.Length && text[end] != '\n' && text[end] != '\r')
                    end++;
                string line = text.Substring(i, end - i);
                list.Add(ParseLine(line));
                i = end;
                if (i < text.Length && text[i] == '\r') i++;
                if (i < text.Length && text[i] == '\n') i++;
            }
            return list;
        }

        public static HostsEntry ParseLine(string line)
        {
            var passthrough = new HostsEntry { IsMapping = false, Raw = line ?? "" };
            if (line == null) return passthrough;

            int pos = 0;
            while (pos < line.Length && IsWs(line[pos])) pos++;
            if (pos >= line.Length) return passthrough;

            bool enabled = true;
            if (line[pos] == '#')
            {
                enabled = false;
                pos++;
                while (pos < line.Length && IsWs(line[pos])) pos++;
                if (pos >= line.Length) return passthrough;
            }

            int ipStart = pos;
            while (pos < line.Length && !IsWs(line[pos])) pos++;
            if (pos == ipStart) return passthrough;
            string ip = line.Substring(ipStart, pos - ipStart);
            IPAddress addr;
            if (!IPAddress.TryParse(ip, out addr)) return passthrough;

            while (pos < line.Length && IsWs(line[pos])) pos++;
            if (pos >= line.Length) return passthrough;

            int hostStart = pos;
            while (pos < line.Length && !IsWs(line[pos]) && line[pos] != '#') pos++;
            if (pos == hostStart) return passthrough;
            string host = line.Substring(hostStart, pos - hostStart);

            string comment = "";
            int hash = line.IndexOf('#', pos);
            if (hash >= 0)
                comment = line.Substring(hash + 1).Trim();

            return new HostsEntry
            {
                IsMapping = true,
                Raw = line,
                Enabled = enabled,
                Ip = ip,
                Host = host,
                Comment = comment
            };
        }

        public static string FormatMapping(HostsEntry e)
        {
            if (e == null) return "";
            if (!string.IsNullOrEmpty(e.Raw))
            {
                var orig = ParseLine(e.Raw);
                if (orig.IsMapping
                    && orig.Ip == (e.Ip ?? "")
                    && orig.Host == (e.Host ?? "")
                    && orig.Comment == (e.Comment ?? ""))
                {
                    if (orig.Enabled == e.Enabled) return e.Raw;
                    return ToggleLeadingComment(e.Raw, e.Enabled);
                }
            }

            string indent = LeadingWs(e.Raw);
            var sb = new StringBuilder();
            sb.Append(indent);
            if (!e.Enabled) sb.Append("# ");
            sb.Append(e.Ip ?? "").Append(' ').Append(e.Host ?? "");
            if (!string.IsNullOrWhiteSpace(e.Comment))
                sb.Append(" # ").Append(e.Comment.Trim());
            return sb.ToString();
        }

        /// <summary>
        /// 按 fileLines 原顺序输出。已从 mappings 删除的映射行跳过；新建映射追加在末尾。
        /// </summary>
        public static string Render(IList<HostsEntry> fileLines, IList<HostsEntry> mappings, string newLine)
        {
            if (string.IsNullOrEmpty(newLine)) newLine = "\r\n";
            var live = new HashSet<HostsEntry>();
            if (mappings != null)
            {
                for (int i = 0; i < mappings.Count; i++)
                {
                    if (mappings[i] != null && mappings[i].IsMapping)
                        live.Add(mappings[i]);
                }
            }

            var sb = new StringBuilder();
            var emitted = new HashSet<HostsEntry>();
            if (fileLines != null)
            {
                for (int i = 0; i < fileLines.Count; i++)
                {
                    var e = fileLines[i];
                    if (e == null) continue;
                    if (!e.IsMapping)
                    {
                        sb.Append(e.Raw ?? "").Append(newLine);
                        continue;
                    }
                    if (!live.Contains(e)) continue;
                    sb.Append(FormatMapping(e)).Append(newLine);
                    emitted.Add(e);
                }
            }
            if (mappings != null)
            {
                for (int i = 0; i < mappings.Count; i++)
                {
                    var e = mappings[i];
                    if (e == null || !e.IsMapping || emitted.Contains(e)) continue;
                    sb.Append(FormatMapping(e)).Append(newLine);
                }
            }
            return sb.ToString();
        }

        /// <summary>对比两份 Hosts 文本的映射增删改，供撤销前提示。</summary>
        public static string DescribeChanges(string oldText, string newText)
        {
            var oldMaps = MappingList(Parse(oldText ?? ""));
            var newMaps = MappingList(Parse(newText ?? ""));
            var lines = new List<string>();
            var usedOld = new bool[oldMaps.Count];
            var usedNew = new bool[newMaps.Count];

            PairMaps(oldMaps, newMaps, usedOld, usedNew, lines, SameKey);
            PairMaps(oldMaps, newMaps, usedOld, usedNew, lines, SameHost);

            for (int i = 0; i < oldMaps.Count; i++)
            {
                if (!usedOld[i])
                    lines.Add("删除：" + FormatKey(oldMaps[i]));
            }
            for (int j = 0; j < newMaps.Count; j++)
            {
                if (!usedNew[j])
                    lines.Add("新增：" + FormatKey(newMaps[j]));
            }
            if (lines.Count == 0)
                return "映射条目无增删改。";

            const int cap = 12;
            var sb = new StringBuilder();
            int n = lines.Count < cap ? lines.Count : cap;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            if (lines.Count > cap)
                sb.Append("\n…其余 ").Append(lines.Count - cap).Append(" 条");
            return sb.ToString();
        }

        private static List<HostsEntry> MappingList(List<HostsEntry> lines)
        {
            var maps = new List<HostsEntry>();
            if (lines == null) return maps;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null && lines[i].IsMapping)
                    maps.Add(lines[i]);
            }
            return maps;
        }

        private static void PairMaps(
            List<HostsEntry> oldMaps, List<HostsEntry> newMaps,
            bool[] usedOld, bool[] usedNew, List<string> lines,
            Func<HostsEntry, HostsEntry, bool> match)
        {
            for (int i = 0; i < oldMaps.Count; i++)
            {
                if (usedOld[i]) continue;
                for (int j = 0; j < newMaps.Count; j++)
                {
                    if (usedNew[j]) continue;
                    if (!match(oldMaps[i], newMaps[j])) continue;
                    usedOld[i] = true;
                    usedNew[j] = true;
                    RecordModify(lines, oldMaps[i], newMaps[j]);
                    break;
                }
            }
        }

        private static void RecordModify(List<string> lines, HostsEntry oldE, HostsEntry newE)
        {
            string detail = FieldDiff(oldE, newE);
            if (detail != null)
                lines.Add("修改：" + FormatKey(oldE) + "（" + detail + "）");
        }

        private static bool SameKey(HostsEntry a, HostsEntry b)
        {
            return string.Equals(a.Ip ?? "", b.Ip ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Host ?? "", b.Host ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameHost(HostsEntry a, HostsEntry b)
        {
            return string.Equals(a.Host ?? "", b.Host ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatKey(HostsEntry e)
        {
            string s = (e.Ip ?? "") + "  " + (e.Host ?? "");
            if (!e.Enabled) s += "（禁用）";
            return s;
        }

        private static string FieldDiff(HostsEntry oldE, HostsEntry newE)
        {
            var parts = new List<string>();
            if (!string.Equals(oldE.Ip ?? "", newE.Ip ?? "", StringComparison.OrdinalIgnoreCase))
                parts.Add("IP " + (oldE.Ip ?? "") + " → " + (newE.Ip ?? ""));
            if (!string.Equals(oldE.Host ?? "", newE.Host ?? "", StringComparison.OrdinalIgnoreCase))
                parts.Add("域名 " + (oldE.Host ?? "") + " → " + (newE.Host ?? ""));
            if (oldE.Enabled != newE.Enabled)
                parts.Add(newE.Enabled ? "禁用 → 启用" : "启用 → 禁用");
            if (!string.Equals(oldE.Comment ?? "", newE.Comment ?? "", StringComparison.Ordinal))
            {
                string a = string.IsNullOrEmpty(oldE.Comment) ? "（空）" : oldE.Comment;
                string b = string.IsNullOrEmpty(newE.Comment) ? "（空）" : newE.Comment;
                parts.Add("注释 " + a + " → " + b);
            }
            if (parts.Count == 0) return null;
            return string.Join("；", parts.ToArray());
        }

        public static string ToggleLeadingComment(string raw, bool enabled)
        {
            if (raw == null) raw = "";
            int i = 0;
            while (i < raw.Length && IsWs(raw[i])) i++;
            string lead = raw.Substring(0, i);
            string rest = raw.Substring(i);
            if (enabled)
            {
                if (rest.Length > 0 && rest[0] == '#')
                    rest = rest.Substring(1);
                return lead + rest;
            }
            if (rest.Length > 0 && rest[0] == '#') return raw;
            if (rest.Length > 0 && IsWs(rest[0])) return lead + "#" + rest;
            return lead + "# " + rest;
        }

        private static string LeadingWs(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = 0;
            while (i < s.Length && IsWs(s[i])) i++;
            return s.Substring(0, i);
        }

        private static bool IsWs(char c)
        {
            return c == ' ' || c == '\t';
        }
    }
}
