using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>URL / HTML / Unicode 编解码纯逻辑测试（CodecHelper）。</summary>
    [TestClass]
    public class CodecTests
    {
        [TestMethod]
        public void UrlEncode_中文_百分号编码()
        {
            Assert.AreEqual("%E4%BD%A0%E5%A5%BD", CodecHelper.UrlEncode("你好"));
        }

        [TestMethod]
        public void UrlDecode_百分号与加号_还原()
        {
            Assert.AreEqual("你好", CodecHelper.UrlDecode("%E4%BD%A0%E5%A5%BD"));
            Assert.AreEqual("a b", CodecHelper.UrlDecode("a+b"));
        }

        [TestMethod]
        public void HtmlEncode_尖括号_转义()
        {
            Assert.AreEqual("&lt;a&gt;", CodecHelper.Transform("<a>", 2));
        }

        [TestMethod]
        public void HtmlDecode_实体_还原()
        {
            Assert.AreEqual("<a>", CodecHelper.Transform("&lt;a&gt;", 3));
        }

        [TestMethod]
        public void UnicodeEscape_非ASCII_转义为反斜杠u()
        {
            Assert.AreEqual("\\u4f60\\u597d", CodecHelper.UnicodeEscape("你好"));
        }

        [TestMethod]
        public void UnicodeUnescape_反斜杠u_还原()
        {
            Assert.AreEqual("你好", CodecHelper.UnicodeUnescape("\\u4f60\\u597d"));
        }

        [TestMethod]
        public void Transform_默认_URL编码()
        {
            Assert.AreEqual("a%20b", CodecHelper.Transform("a b", 0));
        }
    }
}
