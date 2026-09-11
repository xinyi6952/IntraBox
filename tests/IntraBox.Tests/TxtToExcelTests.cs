using System;
using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>文本转 Excel 解析纯逻辑测试（TxtToExcelHelper）。</summary>
    [TestClass]
    public class TxtToExcelTests
    {
        [TestMethod]
        public void DetectSeparator_逗号居多()
        {
            Assert.AreEqual(',', TxtToExcelHelper.DetectSeparator("a,b,c\n1,2,3"));
        }

        [TestMethod]
        public void DetectSeparator_制表符()
        {
            Assert.AreEqual('\t', TxtToExcelHelper.DetectSeparator("a\tb\n1\t2"));
        }

        [TestMethod]
        public void DetectSeparator_无分隔符_回退制表符()
        {
            Assert.AreEqual('\t', TxtToExcelHelper.DetectSeparator("abc"));
        }

        [TestMethod]
        public void Parse_逗号_行列与值正确()
        {
            var dt = TxtToExcelHelper.Parse("a,b\n1,2", ',');
            Assert.AreEqual(2, dt.Columns.Count);
            Assert.AreEqual(2, dt.Rows.Count);
            Assert.AreEqual("a", dt.Rows[0][0]);
            Assert.AreEqual("b", dt.Rows[0][1]);
            Assert.AreEqual("1", dt.Rows[1][0]);
            Assert.AreEqual("2", dt.Rows[1][1]);
        }

        [TestMethod]
        public void Parse_跳过空行()
        {
            var dt = TxtToExcelHelper.Parse("a,b\n\n1,2", ',');
            Assert.AreEqual(2, dt.Rows.Count);
        }

        [TestMethod]
        public void Parse_单元格去除首尾空白()
        {
            var dt = TxtToExcelHelper.Parse(" a , b ", ',');
            Assert.AreEqual("a", dt.Rows[0][0]);
            Assert.AreEqual("b", dt.Rows[0][1]);
        }

        [TestMethod]
        public void Parse_保留前导零与日期原文()
        {
            var dt = TxtToExcelHelper.Parse("00001,2024-01-02", ',');
            Assert.AreEqual("00001", dt.Rows[0][0]);
            Assert.AreEqual("2024-01-02", dt.Rows[0][1]);
        }

        [TestMethod]
        public void Parse_自定义多字符分隔符()
        {
            var opt = new TxtToExcelOptions();
            opt.Separator = "||";
            var dt = TxtToExcelHelper.Parse("a||b||c", opt);
            Assert.AreEqual(3, dt.Columns.Count);
            Assert.AreEqual("b", dt.Rows[0][1]);
        }

        [TestMethod]
        public void Parse_连续分隔符视为一个()
        {
            var opt = new TxtToExcelOptions();
            opt.Separator = " ";
            opt.ConsecutiveAsOne = true;
            var dt = TxtToExcelHelper.Parse("a   b  c", opt);
            Assert.AreEqual(3, dt.Columns.Count);
            Assert.AreEqual("c", dt.Rows[0][2]);
        }

        [TestMethod]
        public void Parse_按文本类型_中文与数字()
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.TextType;
            var dt = TxtToExcelHelper.Parse("张三19900101\n李四20250315", opt);
            Assert.AreEqual(2, dt.Columns.Count);
            Assert.AreEqual("张三", dt.Rows[0][0]);
            Assert.AreEqual("19900101", dt.Rows[0][1]);
            Assert.AreEqual("李四", dt.Rows[1][0]);
        }

        [TestMethod]
        public void Parse_按文本类型_日期横线不拆碎()
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.TextType;
            var dt = TxtToExcelHelper.Parse("张三2026-07-22", opt);
            Assert.AreEqual(2, dt.Columns.Count);
            Assert.AreEqual("张三", dt.Rows[0][0]);
            Assert.AreEqual("2026-07-22", dt.Rows[0][1]);
        }

        [TestMethod]
        public void Parse_按关键字()
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.Keyword;
            opt.Keyword = "区";
            var dt = TxtToExcelHelper.Parse("北京市朝阳区望京街道", opt);
            Assert.AreEqual(2, dt.Columns.Count);
            Assert.AreEqual("北京市朝阳", dt.Rows[0][0]);
            Assert.AreEqual("望京街道", dt.Rows[0][1]);
        }

        [TestMethod]
        public void Parse_按多个关键字()
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.Keyword;
            opt.Keyword = "市|区";
            var dt = TxtToExcelHelper.Parse("北京市朝阳区望京街道", opt);
            Assert.AreEqual(3, dt.Columns.Count);
            Assert.AreEqual("北京", dt.Rows[0][0]);
            Assert.AreEqual("朝阳", dt.Rows[0][1]);
            Assert.AreEqual("望京街道", dt.Rows[0][2]);
        }

        [TestMethod]
        public void Parse_固定宽度()
        {
            var opt = new TxtToExcelOptions();
            opt.Mode = TxtSplitMode.FixedWidth;
            opt.FixedWidths = TxtToExcelHelper.ParseFixedWidths("6,8,3,1");
            var dt = TxtToExcelHelper.Parse("410102199001012345", opt);
            Assert.AreEqual(4, dt.Columns.Count);
            Assert.AreEqual("410102", dt.Rows[0][0]);
            Assert.AreEqual("19900101", dt.Rows[0][1]);
            Assert.AreEqual("234", dt.Rows[0][2]);
            Assert.AreEqual("5", dt.Rows[0][3]);
        }

        [TestMethod]
        public void Resolve_文本不把前导零当数字()
        {
            var cell = TxtToExcelHelper.Resolve("00001", TxtColumnFormat.Text);
            Assert.IsFalse(cell.HasNumber);
            Assert.AreEqual("00001", cell.Text);
        }

        [TestMethod]
        public void Resolve_数字列解析()
        {
            var cell = TxtToExcelHelper.Resolve("00001", TxtColumnFormat.Number);
            Assert.IsTrue(cell.HasNumber);
            Assert.AreEqual(1.0, cell.Number);
        }

        [TestMethod]
        public void Resolve_日期年月日()
        {
            var cell = TxtToExcelHelper.Resolve("2024-01-02", TxtColumnFormat.DateYmd);
            Assert.IsTrue(cell.HasDate);
            Assert.AreEqual(new DateTime(2024, 1, 2), cell.Date);
        }

        [TestMethod]
        public void ToTsv_按列且保留原文_跳过不导入()
        {
            var dt = TxtToExcelHelper.Parse("00001,skip,2024-01-02", ',');
            var formats = new[] { TxtColumnFormat.Text, TxtColumnFormat.Skip, TxtColumnFormat.Text };
            string tsv = TxtToExcelHelper.ToTsv(dt, formats);
            Assert.AreEqual("00001\t2024-01-02", tsv);
        }

        [TestMethod]
        public void ToClipboardHtml_文本列带文本格式并已分列()
        {
            var dt = TxtToExcelHelper.Parse("00001,2024-01-02", ',');
            string html = TxtToExcelHelper.ToClipboardHtml(dt, null);
            StringAssert.Contains(html, "StartHTML:");
            StringAssert.Contains(html, "<table>");
            StringAssert.Contains(html, "00001");
            StringAssert.Contains(html, "mso-number-format:");
            StringAssert.Contains(html, "</td><td");
        }

        [TestMethod]
        public void ToClipboardHtml_含中文_CF_HTML偏移为UTF8字节数()
        {
            var dt = TxtToExcelHelper.Parse("00001,2024-01-02,ascii\n00002,2024-12-31,中文内容", ',');
            string html = TxtToExcelHelper.ToClipboardHtml(dt, null);
            AssertCfHtmlUtf8Offsets(html);
            int tableStart = html.IndexOf("<table>", StringComparison.Ordinal);
            int tableEnd = html.IndexOf("</table>", StringComparison.Ordinal) + "</table>".Length;
            string fragment = html.Substring(tableStart, tableEnd - tableStart);
            Assert.IsTrue(Encoding.UTF8.GetByteCount(fragment) > fragment.Length,
                "本用例须含中文，使 UTF-8 字节数大于 UTF-16 字符数");
            StringAssert.Contains(html, "00001");
            StringAssert.Contains(html, "中文内容");
        }

        [TestMethod]
        public void ToClipboardHtml_仅ASCII_CF_HTML偏移仍正确()
        {
            var dt = TxtToExcelHelper.Parse("00001,2024-01-02", ',');
            string html = TxtToExcelHelper.ToClipboardHtml(dt, null);
            AssertCfHtmlUtf8Offsets(html);
            int tableStart = html.IndexOf("<table>", StringComparison.Ordinal);
            int tableEnd = html.IndexOf("</table>", StringComparison.Ordinal) + "</table>".Length;
            string fragment = html.Substring(tableStart, tableEnd - tableStart);
            Assert.AreEqual(fragment.Length, Encoding.UTF8.GetByteCount(fragment));
        }

        private static void AssertCfHtmlUtf8Offsets(string html)
        {
            int htmlAt = html.IndexOf("<html>", StringComparison.Ordinal);
            Assert.IsTrue(htmlAt >= 4, "缺少 <html>");
            Assert.AreEqual("\r\n\r\n", html.Substring(htmlAt - 4, 4), "CF_HTML 头与正文之间须空一行");
            int startHtml = ParseCfHtmlOffset(html, "StartHTML:");
            Assert.AreEqual(Encoding.UTF8.GetByteCount(html.Substring(0, htmlAt)), startHtml);
            int startFragment = ParseCfHtmlOffset(html, "StartFragment:");
            int endFragment = ParseCfHtmlOffset(html, "EndFragment:");
            int tableStart = html.IndexOf("<table>", StringComparison.Ordinal);
            int tableEnd = html.IndexOf("</table>", StringComparison.Ordinal) + "</table>".Length;
            Assert.IsTrue(tableStart >= 0 && tableEnd > tableStart, "缺少 table 片段");
            string fragment = html.Substring(tableStart, tableEnd - tableStart);
            string headAndPre = html.Substring(0, tableStart);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(fragment), endFragment - startFragment);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(headAndPre), startFragment);
        }

        private static int ParseCfHtmlOffset(string html, string key)
        {
            int i = html.IndexOf(key, StringComparison.Ordinal);
            Assert.IsTrue(i >= 0, "缺少 " + key);
            return int.Parse(html.Substring(i + key.Length, 10), CultureInfo.InvariantCulture);
        }

        [TestMethod]
        public void Formats_往返()
        {
            var src = new[] { TxtColumnFormat.Text, TxtColumnFormat.Number, TxtColumnFormat.Skip };
            string csv = TxtToExcelHelper.FormatsToString(src);
            var back = TxtToExcelHelper.ParseFormats(csv, 3);
            Assert.AreEqual(TxtColumnFormat.Number, back[1]);
            Assert.AreEqual(TxtColumnFormat.Skip, back[2]);
        }

        [TestMethod]
        public void EnsureFormats_默认文本且保留已有列()
        {
            var cur = new[] { TxtColumnFormat.Number };
            var next = TxtToExcelHelper.EnsureFormats(cur, 3);
            Assert.AreEqual(TxtColumnFormat.Number, next[0]);
            Assert.AreEqual(TxtColumnFormat.Text, next[1]);
            Assert.AreEqual(TxtColumnFormat.Text, next[2]);
        }
    }
}
