using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>行处理纯逻辑测试（LineProcessHelper）。</summary>
    [TestClass]
    public class LineProcessTests
    {
        [TestMethod]
        public void NaturalCompare_数字段按数值比较()
        {
            Assert.IsTrue(LineProcessHelper.NaturalCompare("file2", "file10") < 0);
            Assert.IsTrue(LineProcessHelper.NaturalCompare("file10", "file2") > 0);
            Assert.AreEqual(0, LineProcessHelper.NaturalCompare("file2", "file2"));
        }

        [TestMethod]
        public void NaturalCompare_不区分大小写()
        {
            Assert.AreEqual(0, LineProcessHelper.NaturalCompare("abc", "ABC"));
        }

        [TestMethod]
        public void SplitLines_统一换行符()
        {
            CollectionAssert.AreEqual(
                new[] { "a", "b", "c", "d" },
                LineProcessHelper.SplitLines("a\r\nb\rc\nd"));
        }

        [TestMethod]
        public void Deduplicate_保序去重()
        {
            CollectionAssert.AreEqual(
                new List<string> { "a", "b", "c" },
                LineProcessHelper.Deduplicate(new[] { "a", "b", "a", "c" }));
        }

        [TestMethod]
        public void RemoveBlank_删除空白行()
        {
            CollectionAssert.AreEqual(
                new List<string> { "a", "b" },
                LineProcessHelper.RemoveBlank(new[] { "a", "", " ", "b" }));
        }

        [TestMethod]
        public void AddAffix_加前后缀()
        {
            CollectionAssert.AreEqual(
                new List<string> { "[x]", "[y]" },
                LineProcessHelper.AddAffix(new[] { "x", "y" }, "[", "]"));
        }

        [TestMethod]
        public void TrimLines_删首尾空白()
        {
            CollectionAssert.AreEqual(
                new List<string> { "a", "b" },
                LineProcessHelper.TrimLines(new[] { " a ", "b" }));
        }

        [TestMethod]
        public void SortNatural_数字按数值排序()
        {
            CollectionAssert.AreEqual(
                new List<string> { "f1", "f2", "f10" },
                LineProcessHelper.SortNatural(new[] { "f10", "f2", "f1" }));
        }
    }
}
