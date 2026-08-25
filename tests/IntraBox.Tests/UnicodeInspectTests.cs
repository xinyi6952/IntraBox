using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>Unicode 字符检查纯逻辑测试（UnicodeInspectHelper）。</summary>
    [TestClass]
    public class UnicodeInspectTests
    {
        [TestMethod]
        public void Analyze_ASCII字符_码点与HTML正确()
        {
            var rows = UnicodeInspectHelper.Analyze("A");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("U+0041", rows[0].Code);
            Assert.AreEqual("&#65;", rows[0].Html);
            Assert.AreEqual("41", rows[0].Utf8);
        }

        [TestMethod]
        public void Analyze_中文_UTF8与UTF16编码()
        {
            var rows = UnicodeInspectHelper.Analyze("中");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("U+4E2D", rows[0].Code);
            Assert.AreEqual("E4 B8 AD", rows[0].Utf8);
            Assert.AreEqual("2D 4E", rows[0].Utf16);
            Assert.AreEqual("&#20013;", rows[0].Html);
        }

        [TestMethod]
        public void Analyze_代理对_合并为一个码点()
        {
            var rows = UnicodeInspectHelper.Analyze("😀");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("U+1F600", rows[0].Code);
            Assert.AreEqual("&#128512;", rows[0].Html);
        }

        [TestMethod]
        public void Analyze_多字符_按码点逐个输出()
        {
            var rows = UnicodeInspectHelper.Analyze("AB");
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("U+0041", rows[0].Code);
            Assert.AreEqual("U+0042", rows[1].Code);
        }

        [TestMethod]
        public void Analyze_空或null_返回空()
        {
            Assert.AreEqual(0, UnicodeInspectHelper.Analyze("").Count);
            Assert.AreEqual(0, UnicodeInspectHelper.Analyze(null).Count);
        }

        [TestMethod]
        public void ToHex_字节数组_空格分隔大写()
        {
            Assert.AreEqual("E4 B8 AD", UnicodeInspectHelper.ToHex(new byte[] { 0xE4, 0xB8, 0xAD }));
        }
    }
}
