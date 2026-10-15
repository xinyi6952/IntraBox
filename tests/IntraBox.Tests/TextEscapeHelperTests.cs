using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class TextEscapeHelperTests
    {
        [TestMethod]
        public void Json去转义_反斜杠与引号()
        {
            string raw = "{\\\"a\\\":1}";
            Assert.AreEqual("{\"a\":1}", TextEscapeHelper.Unescape(raw, TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json去转义_双层反斜杠停在一层()
        {
            Assert.AreEqual("a\\nb", TextEscapeHelper.Unescape("a\\\\nb", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("a\nb", TextEscapeHelper.Unescape("a\\nb", TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json去转义_再包一层引号时继续剥()
        {
            Assert.AreEqual("{\"a\":1}", TextEscapeHelper.Unescape("{\\\\\"a\\\\\":1}", TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json去转义_整段JSON字符串()
        {
            string raw = "\"{\\\"a\\\":1}\"";
            Assert.AreEqual("{\"a\":1}", TextEscapeHelper.Unescape(raw, TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json去转义_Unicode与代理对与花括号()
        {
            Assert.AreEqual("你好", TextEscapeHelper.Unescape("\\u4f60\\u597d", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("\U0001F600", TextEscapeHelper.Unescape("\\uD83D\\uDE00", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("\U0001F600", TextEscapeHelper.Unescape("\\u{1F600}", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("你", TextEscapeHelper.Unescape("\\U00004F60", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("A", TextEscapeHelper.Unescape("\\x41", TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json去转义_百分号与U加号()
        {
            Assert.AreEqual("你", TextEscapeHelper.Unescape("%E4%BD%A0", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("你", TextEscapeHelper.Unescape("%u4F60", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("你", TextEscapeHelper.Unescape("U+4F60", TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Json转义_往返()
        {
            string src = "a\"b\\c\n\t";
            string escaped = TextEscapeHelper.Escape(src, TextEscapeHelper.Kind.Json);
            Assert.AreEqual("a\\\"b\\\\c\\n\\t", escaped);
            Assert.AreEqual(src, TextEscapeHelper.Unescape(escaped, TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void 普通JSON_去转义不改写()
        {
            Assert.AreEqual("{\"a\":1}", TextEscapeHelper.Unescape("{\"a\":1}", TextEscapeHelper.Kind.Json));
        }

        [TestMethod]
        public void Markup去转义_实体与不带分号()
        {
            string nbsp = ">\u00A0<";
            Assert.AreEqual(nbsp, TextEscapeHelper.Unescape("&gt;&nbsp;&lt;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual(">", TextEscapeHelper.Unescape("&amp;gt;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual(">", TextEscapeHelper.Unescape("&gt", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual("©", TextEscapeHelper.Unescape("&copy;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual("'", TextEscapeHelper.Unescape("&apos;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual("…", TextEscapeHelper.Unescape("&hellip;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual("<", TextEscapeHelper.Unescape("&#60;", TextEscapeHelper.Kind.Markup));
            Assert.AreEqual("A", TextEscapeHelper.Unescape("&#x41;", TextEscapeHelper.Kind.Markup));
        }

        [TestMethod]
        public void Markup转义_往返()
        {
            string src = "<a href=\"x\">'&'\u00A0</a>";
            string escaped = TextEscapeHelper.Escape(src, TextEscapeHelper.Kind.Markup);
            StringAssert.Contains(escaped, "&lt;");
            StringAssert.Contains(escaped, "&gt;");
            StringAssert.Contains(escaped, "&quot;");
            StringAssert.Contains(escaped, "&apos;");
            StringAssert.Contains(escaped, "&amp;");
            StringAssert.Contains(escaped, "&nbsp;");
            Assert.AreEqual(src, TextEscapeHelper.Unescape(escaped, TextEscapeHelper.Kind.Markup));
        }

        [TestMethod]
        public void Sql转义_单引号与反斜杠往返()
        {
            string src = "it's a\\b";
            string escaped = TextEscapeHelper.Escape(src, TextEscapeHelper.Kind.Sql);
            Assert.AreEqual("it''s a\\\\b", escaped);
            Assert.AreEqual(src, TextEscapeHelper.Unescape(escaped, TextEscapeHelper.Kind.Sql));
        }

        [TestMethod]
        public void Css去转义_十六进制()
        {
            Assert.AreEqual("a", TextEscapeHelper.Unescape("\\61 ", TextEscapeHelper.Kind.Css));
            Assert.AreEqual("\n", TextEscapeHelper.Unescape("\\n", TextEscapeHelper.Kind.Css));
        }

        [TestMethod]
        public void 空输入_原样()
        {
            Assert.AreEqual("", TextEscapeHelper.Unescape("", TextEscapeHelper.Kind.Json));
            Assert.AreEqual("", TextEscapeHelper.Escape(null, TextEscapeHelper.Kind.Markup));
        }

        [TestMethod]
        public void 格式映射()
        {
            Assert.AreEqual(TextEscapeHelper.Kind.Json, TextEscapeHelper.KindForFormat("JSON"));
            Assert.AreEqual(TextEscapeHelper.Kind.Json, TextEscapeHelper.KindForFormat("YAML"));
            Assert.AreEqual(TextEscapeHelper.Kind.Markup, TextEscapeHelper.KindForFormat("XML"));
            Assert.AreEqual(TextEscapeHelper.Kind.Markup, TextEscapeHelper.KindForFormat("HTML"));
            Assert.AreEqual(TextEscapeHelper.Kind.Sql, TextEscapeHelper.KindForFormat("SQL"));
            Assert.AreEqual(TextEscapeHelper.Kind.Css, TextEscapeHelper.KindForFormat("CSS"));
        }
    }
}
