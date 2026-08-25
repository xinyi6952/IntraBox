using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>命名转换纯逻辑测试（NameCaseHelper）。</summary>
    [TestClass]
    public class NameCaseTests
    {
        [TestMethod]
        public void SplitWords_下划线短横线空格_拆成单词()
        {
            CollectionAssert.AreEqual(new List<string> { "hello", "world" }, NameCaseHelper.SplitWords("hello_world"));
            CollectionAssert.AreEqual(new List<string> { "hello", "world" }, NameCaseHelper.SplitWords("hello-world"));
            CollectionAssert.AreEqual(new List<string> { "hello", "world" }, NameCaseHelper.SplitWords("hello world"));
        }

        [TestMethod]
        public void SplitWords_驼峰边界_拆成单词()
        {
            CollectionAssert.AreEqual(new List<string> { "hello", "World" }, NameCaseHelper.SplitWords("helloWorld"));
        }

        [TestMethod]
        public void SplitWords_连续大写接小写_拆最后一个大写()
        {
            CollectionAssert.AreEqual(new List<string> { "XML", "Http", "Request" }, NameCaseHelper.SplitWords("XMLHttpRequest"));
        }

        [TestMethod]
        public void SplitWords_空或空白_返回空()
        {
            Assert.AreEqual(0, NameCaseHelper.SplitWords("").Count);
            Assert.AreEqual(0, NameCaseHelper.SplitWords("   ").Count);
            Assert.AreEqual(0, NameCaseHelper.SplitWords(null).Count);
        }

        [TestMethod]
        public void 各命名风格_输出正确()
        {
            var words = NameCaseHelper.SplitWords("hello world");
            Assert.AreEqual("helloWorld", NameCaseHelper.ToCamel(words));
            Assert.AreEqual("HelloWorld", NameCaseHelper.ToPascal(words));
            Assert.AreEqual("hello_world", NameCaseHelper.ToSnake(words));
            Assert.AreEqual("HELLO_WORLD", NameCaseHelper.ToScreamingSnake(words));
            Assert.AreEqual("hello-world", NameCaseHelper.ToKebab(words));
        }

        [TestMethod]
        public void FormatAll_包含全部风格标签()
        {
            string s = NameCaseHelper.FormatAll("hello world");
            Assert.IsTrue(s.IndexOf("camelCase", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("PascalCase", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("snake_case", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("SNAKE_CASE", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("kebab-case", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("helloWorld", StringComparison.Ordinal) >= 0);
        }
    }
}
