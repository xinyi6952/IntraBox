using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;
using Newtonsoft.Json.Linq;

namespace IntraBox.Tests
{
    /// <summary>JSON 转实体类纯逻辑测试（JsonToClassHelper / ClassEmitter）。</summary>
    [TestClass]
    public class JsonToClassTests
    {
        [TestMethod]
        public void SanitizeIdent_空或数字开头()
        {
            Assert.AreEqual("Root", JsonToClassHelper.SanitizeIdent("", "Root"));
            Assert.AreEqual("_123abc", JsonToClassHelper.SanitizeIdent("123abc", "X"));
            Assert.AreEqual("abc", JsonToClassHelper.SanitizeIdent("a b-c", "X"));
        }

        [TestMethod]
        public void Emit_对象_生成CSharp类()
        {
            var s = new ClassEmitter(false).Emit(JObject.Parse("{\"name\":\"a\",\"age\":1}"), "User");
            Assert.IsTrue(s.IndexOf("public class User", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("public string Name { get; set; }", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("public int Age { get; set; }", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void Emit_数组_生成列表与元素类()
        {
            var s = new ClassEmitter(false).Emit(JArray.Parse("[{\"id\":1}]"), "Root");
            Assert.IsTrue(s.IndexOf("public class Root", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("List<", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void Emit_Java模式_生成Java类()
        {
            var s = new ClassEmitter(true).Emit(JObject.Parse("{\"name\":\"a\"}"), "User");
            Assert.IsTrue(s.IndexOf("public class User", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(s.IndexOf("public String name;", StringComparison.Ordinal) >= 0);
        }
    }
}
