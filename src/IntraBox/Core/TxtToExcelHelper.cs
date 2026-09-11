using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>文本转 Excel：分列（分隔符 / 文本类型 / 关键字 / 固定宽度）与列格式。纯逻辑，便于单元测试。</summary>
    public enum TxtSplitMode
    {
        Delimiter = 0,
        TextType = 1,
        Keyword = 2,
        FixedWidth = 3
    }

    /// <summary>列数据格式，对齐 WPS/Excel 分列向导第三步；默认文本以免 00001、日期被改写。</summary>
    public enum TxtColumnFormat
    {
        Text = 0,
        General = 1,
        Number = 2,
        DateYmd = 3,
        DateMdy = 4,
        DateDmy = 5,
        Skip = 6
    }

    public sealed class TxtToExcelOptions
    {
        public TxtSplitMode Mode = TxtSplitMode.Delimiter;
        public string Separator = "\t";
        public bool ConsecutiveAsOne;
        public string Keyword = "";
        public int[] FixedWidths;
    }

    public struct TxtResolvedCell
    {
        public string Text;
        public double Number;
        public bool HasNumber;
        public DateTime Date;
        public bool HasDate;
    }

    public static class TxtToExcelHelper
    {
        public const int MaxPreviewRows = 5000;

        public static char DetectSeparator(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string first = null;
            foreach (var l in lines)
            {
                if (!string.IsNullOrWhiteSpace(l)) { first = l; break; }
            }
            if (first == null) return '\t';

            char best = '\t';
            int bestCount = -1;
            foreach (var c in new[] { ',', '\t', ';', '|' })
            {
                int count = first.Split(c).Length - 1;
                if (count > bestCount) { bestCount = count; best = c; }
            }
            return bestCount > 0 ? best : '\t';
        }

        public static DataTable Parse(string text, char sep)
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.Delimiter;
            opt.Separator = sep.ToString();
            return Parse(text, opt);
        }

        public static DataTable Parse(string text, TxtToExcelOptions options)
        {
            var dt = new DataTable();
            if (options == null) options = new TxtToExcelOptions();
            var nonEmpty = new List<string>();
            foreach (var l in NormalizeLines(text))
            {
                if (!string.IsNullOrWhiteSpace(l)) nonEmpty.Add(l);
            }
            if (nonEmpty.Count == 0) return dt;

            var rows = new List<string[]>();
            int maxCols = 0;
            foreach (var l in nonEmpty)
            {
                if (rows.Count >= MaxPreviewRows) break;
                var cells = SplitLine(l, options);
                rows.Add(cells);
                if (cells.Length > maxCols) maxCols = cells.Length;
            }
            if (maxCols == 0) return dt;

            for (int c = 0; c < maxCols; c++) dt.Columns.Add("列" + (c + 1), typeof(string));

            foreach (var cells in rows)
            {
                var row = dt.NewRow();
                for (int c = 0; c < cells.Length; c++)
                    row[c] = cells[c];
                dt.Rows.Add(row);
            }
            return dt;
        }

        public static int[] ParseFixedWidths(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new int[0];
            var parts = text.Replace('，', ',').Split(',');
            var list = new List<int>();
            foreach (var p in parts)
            {
                int n;
                if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                    list.Add(n);
            }
            return list.ToArray();
        }

        public static TxtColumnFormat GetFormat(TxtColumnFormat[] formats, int index)
        {
            if (formats == null || index < 0 || index >= formats.Length) return TxtColumnFormat.Text;
            return formats[index];
        }

        public static TxtColumnFormat[] EnsureFormats(TxtColumnFormat[] current, int columnCount)
        {
            if (columnCount < 0) columnCount = 0;
            var next = new TxtColumnFormat[columnCount];
            for (int i = 0; i < columnCount; i++)
                next[i] = GetFormat(current, i);
            return next;
        }

        public static string FormatName(TxtColumnFormat format)
        {
            switch (format)
            {
                case TxtColumnFormat.General: return "常规";
                case TxtColumnFormat.Number: return "数字";
                case TxtColumnFormat.DateYmd: return "日期(年月日)";
                case TxtColumnFormat.DateMdy: return "日期(月日年)";
                case TxtColumnFormat.DateDmy: return "日期(日月年)";
                case TxtColumnFormat.Skip: return "不导入";
                default: return "文本";
            }
        }

        public static string ColumnHeader(int index, TxtColumnFormat format)
        {
            return "列" + (index + 1) + " · " + FormatName(format);
        }

        public static string FormatsToString(TxtColumnFormat[] formats)
        {
            if (formats == null || formats.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < formats.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)formats[i]);
            }
            return sb.ToString();
        }

        public static TxtColumnFormat[] ParseFormats(string csv, int columnCount)
        {
            var next = EnsureFormats(null, columnCount);
            if (string.IsNullOrWhiteSpace(csv) || columnCount <= 0) return next;
            var parts = csv.Split(',');
            int n = Math.Min(parts.Length, columnCount);
            for (int i = 0; i < n; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                    && v >= 0 && v <= (int)TxtColumnFormat.Skip)
                    next[i] = (TxtColumnFormat)v;
            }
            return next;
        }

        public static TxtResolvedCell Resolve(string raw, TxtColumnFormat format)
        {
            var cell = new TxtResolvedCell();
            cell.Text = raw ?? "";
            if (format == TxtColumnFormat.Skip || format == TxtColumnFormat.Text)
                return cell;

            if (format == TxtColumnFormat.Number)
            {
                double n;
                if (TryParseNumber(cell.Text, out n))
                {
                    cell.HasNumber = true;
                    cell.Number = n;
                }
                return cell;
            }

            if (format == TxtColumnFormat.DateYmd || format == TxtColumnFormat.DateMdy || format == TxtColumnFormat.DateDmy)
            {
                DateTime d;
                if (TryParseDate(cell.Text, format, out d))
                {
                    cell.HasDate = true;
                    cell.Date = d;
                }
                return cell;
            }

            // 常规：与表格软件类似，能认成数字/日期则转换，否则文本
            double gn;
            if (TryParseNumber(cell.Text, out gn))
            {
                cell.HasNumber = true;
                cell.Number = gn;
                return cell;
            }
            DateTime gd;
            if (TryParseDate(cell.Text, TxtColumnFormat.DateYmd, out gd)
                || TryParseDate(cell.Text, TxtColumnFormat.DateMdy, out gd)
                || TryParseDate(cell.Text, TxtColumnFormat.DateDmy, out gd))
            {
                cell.HasDate = true;
                cell.Date = gd;
            }
            return cell;
        }

        /// <summary>制表符分隔文本，粘贴到 Excel/WPS 时按列拆开。跳过「不导入」列；单元格内的制表符/换行改为空格以免错列。</summary>
        public static string ToTsv(DataTable table, TxtColumnFormat[] formats)
        {
            if (table == null || table.Rows.Count == 0) return "";
            var cols = IncludedColumns(table.Columns.Count, formats);
            if (cols.Length == 0) return "";
            var sb = new StringBuilder();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (r > 0) sb.Append('\n');
                for (int i = 0; i < cols.Length; i++)
                {
                    if (i > 0) sb.Append('\t');
                    sb.Append(SanitizeTsvCell(table.Rows[r][cols[i]] as string));
                }
            }
            return sb.ToString();
        }

        /// <summary>CF_HTML 表格。文本列带 mso-number-format:@，粘贴进 Excel/WPS 时保留 00001、日期原文并已分列。</summary>
        public static string ToClipboardHtml(DataTable table, TxtColumnFormat[] formats)
        {
            if (table == null || table.Rows.Count == 0) return "";
            var cols = IncludedColumns(table.Columns.Count, formats);
            if (cols.Length == 0) return "";
            var body = new StringBuilder();
            body.Append("<table>");
            for (int r = 0; r < table.Rows.Count; r++)
            {
                body.Append("<tr>");
                for (int i = 0; i < cols.Length; i++)
                {
                    int c = cols[i];
                    var fmt = GetFormat(formats, c);
                    string raw = table.Rows[r][c] as string ?? "";
                    var resolved = Resolve(raw, fmt);
                    string style = HtmlStyle(fmt, resolved);
                    string inner = HtmlInner(raw, fmt, resolved);
                    if (style.Length > 0)
                        body.Append("<td style=\"").Append(style).Append("\">").Append(inner).Append("</td>");
                    else
                        body.Append("<td>").Append(inner).Append("</td>");
                }
                body.Append("</tr>");
            }
            body.Append("</table>");
            return WrapCfHtml(body.ToString());
        }

        public static bool TryParseDate(string text, TxtColumnFormat format, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            string[] patterns;
            switch (format)
            {
                case TxtColumnFormat.DateMdy:
                    patterns = new[] { "M/d/yyyy", "MM/dd/yyyy", "M-d-yyyy", "MM-dd-yyyy", "M.d.yyyy", "MM.dd.yyyy" };
                    break;
                case TxtColumnFormat.DateDmy:
                    patterns = new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy" };
                    break;
                default:
                    patterns = new[]
                    {
                        "yyyy-M-d", "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd",
                        "yyyy.M.d", "yyyy.MM.dd", "yyyyMMdd"
                    };
                    break;
            }
            return DateTime.TryParseExact(s, patterns, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
        }

        private static string[] SplitLine(string line, TxtToExcelOptions options)
        {
            if (line == null) line = "";
            switch (options.Mode)
            {
                case TxtSplitMode.TextType:
                    return SplitByTextType(line);
                case TxtSplitMode.Keyword:
                    return SplitByKeyword(line, options.Keyword, options.ConsecutiveAsOne);
                case TxtSplitMode.FixedWidth:
                    return SplitByFixedWidth(line, options.FixedWidths);
                default:
                    return SplitByDelimiter(line, options.Separator, options.ConsecutiveAsOne);
            }
        }

        private static string[] SplitByDelimiter(string line, string separator, bool consecutiveAsOne)
        {
            if (string.IsNullOrEmpty(separator)) separator = "\t";
            var opt = consecutiveAsOne ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None;
            string[] parts = line.Split(new[] { separator }, opt);
            return TrimParts(parts);
        }

        private static string[] SplitByKeyword(string line, string keyword, bool consecutiveAsOne)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new[] { line.Trim() };
            var keys = new List<string>();
            foreach (var p in keyword.Split('|'))
            {
                var k = p.Trim();
                if (k.Length > 0) keys.Add(k);
            }
            if (keys.Count == 0) return new[] { line.Trim() };
            var opt = consecutiveAsOne ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None;
            return TrimParts(line.Split(keys.ToArray(), opt));
        }

        private static string[] SplitByFixedWidth(string line, int[] widths)
        {
            if (widths == null || widths.Length == 0)
                return new[] { line.Trim() };
            var list = new List<string>();
            int pos = 0;
            foreach (int w in widths)
            {
                if (w <= 0) continue;
                if (pos >= line.Length)
                {
                    list.Add("");
                    continue;
                }
                int take = Math.Min(w, line.Length - pos);
                list.Add(line.Substring(pos, take).Trim());
                pos += take;
            }
            if (pos < line.Length)
                list.Add(line.Substring(pos).Trim());
            return list.ToArray();
        }

        private static string[] SplitByTextType(string line)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(line))
            {
                parts.Add("");
                return parts.ToArray();
            }
            var cur = new StringBuilder();
            int curClass = -1;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                int k = CharClass(ch);
                if (k == 0)
                {
                    FlushPart(parts, cur);
                    curClass = -1;
                    continue;
                }
                if (k == 4)
                {
                    cur.Append(ch);
                    continue;
                }
                if (cur.Length == 0 || curClass == k || curClass < 0)
                {
                    cur.Append(ch);
                    curClass = k;
                }
                else
                {
                    FlushPart(parts, cur);
                    cur.Append(ch);
                    curClass = k;
                }
            }
            FlushPart(parts, cur);
            if (parts.Count == 0) parts.Add("");
            return parts.ToArray();
        }

        private static void FlushPart(List<string> parts, StringBuilder cur)
        {
            if (cur.Length == 0) return;
            parts.Add(cur.ToString().Trim());
            cur.Clear();
        }

        /// <summary>0 空白（切开）；1 数字；2 拉丁字母；3 汉字等东亚文字；4 其它（附着当前段，避免把 2026-07-22 拆碎）。</summary>
        private static int CharClass(char c)
        {
            if (char.IsWhiteSpace(c)) return 0;
            if (c >= '0' && c <= '9') return 1;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return 2;
            if (IsEastAsian(c)) return 3;
            return 4;
        }

        private static bool IsEastAsian(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0x3400 && c <= 0x4DBF)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0x3040 && c <= 0x30FF)
                || (c >= 0xAC00 && c <= 0xD7AF);
        }

        private static string[] TrimParts(string[] parts)
        {
            if (parts == null || parts.Length == 0) return new[] { "" };
            var result = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = parts[i] == null ? "" : parts[i].Trim();
            return result;
        }

        private static string[] NormalizeLines(string text)
        {
            if (text == null) return new string[0];
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static int[] IncludedColumns(int columnCount, TxtColumnFormat[] formats)
        {
            var list = new List<int>();
            for (int i = 0; i < columnCount; i++)
            {
                if (GetFormat(formats, i) != TxtColumnFormat.Skip)
                    list.Add(i);
            }
            return list.ToArray();
        }

        private static string SanitizeTsvCell(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace('\t', ' ').Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        private static bool TryParseNumber(string text, out double number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return double.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out number);
        }

        private static string HtmlStyle(TxtColumnFormat format, TxtResolvedCell resolved)
        {
            if (format == TxtColumnFormat.Text)
                return "mso-number-format:'\\@'";
            if (format == TxtColumnFormat.DateYmd || format == TxtColumnFormat.DateMdy || format == TxtColumnFormat.DateDmy)
            {
                if (resolved.HasDate) return "mso-number-format:'yyyy-mm-dd'";
                return "mso-number-format:'\\@'";
            }
            if (format == TxtColumnFormat.Number && !resolved.HasNumber)
                return "mso-number-format:'\\@'";
            return "";
        }

        private static string HtmlInner(string raw, TxtColumnFormat format, TxtResolvedCell resolved)
        {
            string s = raw ?? "";
            if ((format == TxtColumnFormat.DateYmd || format == TxtColumnFormat.DateMdy || format == TxtColumnFormat.DateDmy)
                && resolved.HasDate)
                s = resolved.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (format == TxtColumnFormat.Number && resolved.HasNumber)
                s = resolved.Number.ToString(CultureInfo.InvariantCulture);
            else if (format == TxtColumnFormat.General && resolved.HasNumber)
                s = resolved.Number.ToString(CultureInfo.InvariantCulture);
            else if (format == TxtColumnFormat.General && resolved.HasDate)
                s = resolved.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            s = WebUtility.HtmlEncode(s).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
            return s;
        }

        private static string WrapCfHtml(string fragment)
        {
            const string marker = "0000000000";
            string head =
                "Version:0.9\r\n" +
                "StartHTML:" + marker + "\r\n" +
                "EndHTML:" + marker + "\r\n" +
                "StartFragment:" + marker + "\r\n" +
                "EndFragment:" + marker + "\r\n" +
                "\r\n"; // 头与 <html> 之间空一行，符合 CF_HTML，避免个别 Excel/WPS 当纯文本
            const string pre = "<html>\r\n<body>\r\n<!--StartFragment-->";
            const string post = "<!--EndFragment-->\r\n</body>\r\n</html>";
            // CF_HTML 偏移是 UTF-8 字节数，不能用 string.Length（含中文时 UTF-16 字符数偏小）。
            int startHtml = Encoding.UTF8.GetByteCount(head);
            int startFragment = startHtml + Encoding.UTF8.GetByteCount(pre);
            int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(post);
            return head
                .Replace("StartHTML:" + marker, "StartHTML:" + startHtml.ToString("D10"))
                .Replace("EndHTML:" + marker, "EndHTML:" + endHtml.ToString("D10"))
                .Replace("StartFragment:" + marker, "StartFragment:" + startFragment.ToString("D10"))
                .Replace("EndFragment:" + marker, "EndFragment:" + endFragment.ToString("D10"))
                + pre + fragment + post;
        }
    }
}
