using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>URL 解析纯逻辑测试（UrlParseHelper）。</summary>
    [TestClass]
    public class UrlParseTests
    {
        [TestMethod]
        public void TryParse_完整URL_解析出各字段()
        {
            List<UrlPart> parts;
            string err;
            Assert.IsTrue(UrlParseHelper.TryParse("https://user:pw@example.com:8080/a/b?x=1&y=%E4%BD%A0#frag", out parts, out err));
            Assert.AreEqual("https", Find(parts, "协议"));
            Assert.AreEqual("user:pw", Find(parts, "用户信息"));
            Assert.AreEqual("example.com", Find(parts, "主机"));
            Assert.AreEqual("8080", Find(parts, "端口"));
            Assert.AreEqual("/a/b", Find(parts, "路径"));
            Assert.AreEqual("?x=1&y=%E4%BD%A0", Find(parts, "Query"));
            Assert.AreEqual("#frag", Find(parts, "Fragment"));
        }

        [TestMethod]
        public void TryParse_query参数_解码()
        {
            List<UrlPart> parts;
            string err;
            Assert.IsTrue(UrlParseHelper.TryParse("http://a.com/?name=%E4%BD%A0%E5%A5%BD", out parts, out err));
            Assert.AreEqual("你好", Find(parts, "query.name"));
        }

        [TestMethod]
        public void TryParse_hash字段_去掉井号()
        {
            List<UrlPart> parts;
            string err;
            Assert.IsTrue(UrlParseHelper.TryParse("http://a.com/#top", out parts, out err));
            Assert.AreEqual("top", Find(parts, "hash"));
        }

        [TestMethod]
        public void TryParse_默认端口_显示默认标注()
        {
            List<UrlPart> parts;
            string err;
            Assert.IsTrue(UrlParseHelper.TryParse("http://a.com/", out parts, out err));
            Assert.AreEqual("80（默认）", Find(parts, "端口"));
        }

        [TestMethod]
        public void TryParse_空或非法_返回false并给出错误()
        {
            List<UrlPart> parts;
            string err;
            Assert.IsFalse(UrlParseHelper.TryParse("", out parts, out err));
            Assert.IsFalse(string.IsNullOrEmpty(err));
            Assert.IsFalse(UrlParseHelper.TryParse("不是url", out parts, out err));
            Assert.IsFalse(string.IsNullOrEmpty(err));
        }

        private static string Find(List<UrlPart> parts, string key)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Key == key) return parts[i].Value;
            return null;
        }
    }
}
