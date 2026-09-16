using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class EnvVarHelperTests
    {
        [TestMethod]
        public void TryValidateName_空与等号()
        {
            string err;
            Assert.IsFalse(EnvVarHelper.TryValidateName("", out err));
            Assert.IsFalse(EnvVarHelper.TryValidateName("  ", out err));
            Assert.IsFalse(EnvVarHelper.TryValidateName("A=B", out err));
            Assert.IsTrue(EnvVarHelper.TryValidateName("JAVA_HOME", out err));
            Assert.IsTrue(string.IsNullOrEmpty(err));
        }

        [TestMethod]
        public void SplitJoinPath_去空段()
        {
            var parts = EnvVarHelper.SplitPath(@"C:\a;; D:\b ;");
            Assert.AreEqual(2, parts.Length);
            Assert.AreEqual(@"C:\a", parts[0]);
            Assert.AreEqual(@"D:\b", parts[1]);
            Assert.AreEqual(@"C:\a;D:\b", EnvVarHelper.JoinPath(parts));
        }

        [TestMethod]
        public void TryMove_上下移()
        {
            var list = new List<string> { "a", "b", "c" };
            Assert.IsFalse(EnvVarHelper.TryMove(list, 0, -1));
            Assert.IsTrue(EnvVarHelper.TryMove(list, 0, 1));
            Assert.AreEqual("b", list[0]);
            Assert.AreEqual("a", list[1]);
            Assert.IsTrue(EnvVarHelper.TryMove(list, 2, -1));
            Assert.AreEqual("c", list[1]);
            Assert.AreEqual("a", list[2]);
        }

        [TestMethod]
        public void IsPathName_忽略大小写()
        {
            Assert.IsTrue(EnvVarHelper.IsPathName("Path"));
            Assert.IsTrue(EnvVarHelper.IsPathName("PATH"));
            Assert.IsFalse(EnvVarHelper.IsPathName("PATHEXT"));
        }

        [TestMethod]
        public void Preview_截断换行()
        {
            Assert.AreEqual("a b", EnvVarHelper.Preview("a\nb"));
            var longText = new string('x', EnvVarHelper.PreviewChars + 5);
            string p = EnvVarHelper.Preview(longText);
            Assert.IsTrue(p.EndsWith("…", StringComparison.Ordinal));
            Assert.AreEqual(EnvVarHelper.PreviewChars + 1, p.Length);
        }

        [TestMethod]
        public void CanEdit_仅用户()
        {
            Assert.IsTrue(EnvVarHelper.CanEdit(EnvVarHelper.Scope.User));
            Assert.IsFalse(EnvVarHelper.CanEdit(EnvVarHelper.Scope.Machine));
            Assert.IsFalse(EnvVarHelper.CanEdit(EnvVarHelper.Scope.Process));
        }

        [TestMethod]
        public void List_进程变量非空()
        {
            var list = EnvVarHelper.List(EnvVarHelper.Scope.Process);
            Assert.IsTrue(list.Count > 0);
            string path = EnvVarHelper.FindInScope(list, "PATH");
            if (path == null) path = EnvVarHelper.FindInScope(list, "Path");
            Assert.IsFalse(string.IsNullOrEmpty(path));
        }

        [TestMethod]
        public void OtherScopeHint_含PATH拼接说明()
        {
            string hint = EnvVarHelper.OtherScopeHint("PATH", "u", "m", "p");
            StringAssert.Contains(hint, "系统 + 用户");
            StringAssert.Contains(hint, "新开的终端");
        }
    }
}
