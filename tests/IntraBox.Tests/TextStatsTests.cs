using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>文本统计纯逻辑测试（TextStatsHelper）。</summary>
    [TestClass]
    public class TextStatsTests
    {
        [TestMethod]
        public void SplitWords_按分隔符分词()
        {
            var words = TextStatsHelper.SplitWords("hello world, foo_bar");
            Assert.AreEqual(3, words.Count);
            Assert.AreEqual("hello", words[0]);
            Assert.AreEqual("world", words[1]);
            Assert.AreEqual("foo_bar", words[2]);
        }

        [TestMethod]
        public void Analyze_字符词行段落数()
        {
            var r = TextStatsHelper.Analyze("a b\nc");
            Assert.AreEqual(5, r.Chars);
            Assert.AreEqual(3, r.CharsNoWs);
            Assert.AreEqual(3, r.Words);
            Assert.AreEqual(2, r.Lines);
            Assert.AreEqual(1, r.Paragraphs);
        }

        [TestMethod]
        public void Analyze_词频_忽略大小写()
        {
            var r = TextStatsHelper.Analyze("Go go GO");
            Assert.AreEqual(3, r.Words);
            Assert.AreEqual(3, r.WordFreq["go"]);
        }

        [TestMethod]
        public void Analyze_空行分隔段落()
        {
            var r = TextStatsHelper.Analyze("a\n\nb");
            Assert.AreEqual(2, r.Paragraphs);
            Assert.AreEqual(3, r.Lines);
        }

        [TestMethod]
        public void Analyze_空文本_全零()
        {
            var r = TextStatsHelper.Analyze("");
            Assert.AreEqual(0, r.Chars);
            Assert.AreEqual(0, r.Words);
            Assert.AreEqual(0, r.Lines);
            Assert.AreEqual(0, r.Paragraphs);
        }
    }
}
