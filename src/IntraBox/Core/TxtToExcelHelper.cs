using System;
using System.Collections.Generic;
using System.Data;

namespace IntraBox.Core
{
    /// <summary>文本转 Excel：分隔符探测与表格解析。纯逻辑，便于单元测试。</summary>
    public static class TxtToExcelHelper
    {
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
            var dt = new DataTable();
            var nonEmpty = new List<string>();
            foreach (var l in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(l)) nonEmpty.Add(l);
            }
            if (nonEmpty.Count == 0) return dt;

            int maxCols = 0;
            foreach (var l in nonEmpty) maxCols = Math.Max(maxCols, l.Split(sep).Length);
            if (maxCols == 0) return dt;

            for (int c = 0; c < maxCols; c++) dt.Columns.Add("列" + (c + 1));

            int rows = 0;
            foreach (var l in nonEmpty)
            {
                if (rows >= 5000) break;
                var cells = l.Split(sep);
                var row = dt.NewRow();
                for (int c = 0; c < cells.Length; c++) row[c] = cells[c].Trim();
                dt.Rows.Add(row);
                rows++;
            }
            return dt;
        }
    }
}
