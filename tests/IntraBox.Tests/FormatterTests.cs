using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.Formatter;

namespace IntraBox.Tests
{
    /// <summary>JSON / SQL 格式化测试（Formatter 是现成纯逻辑类，可直接测）。</summary>
    [TestClass]
    public class FormatterTests
    {
        // ==================== JSON ====================

        [TestClass]
        public class Json
        {
            private readonly JsonFormatter _f = new JsonFormatter();

            [TestMethod]
            public void 美化_紧凑JSON_输出含缩进换行()
            {
                string err;
                var r = _f.Beautify("{\"a\":1,\"b\":[1,2]}", "    ", out err);
                Assert.IsNull(err);
                StringAssert.Contains(r, "\n");
                StringAssert.Contains(r, "    \"a\": 1");
            }

            [TestMethod]
            public void 压缩_含空白JSON_输出单行无空格()
            {
                string err;
                var r = _f.Minify("{ \"a\" : 1 , \"b\" : 2 }", out err);
                Assert.IsNull(err);
                Assert.AreEqual("{\"a\":1,\"b\":2}", r);
            }

            [TestMethod]
            public void 非法JSON_返回错误且不崩溃()
            {
                string err;
                var r = _f.Beautify("{invalid json", "    ", out err);
                Assert.IsNull(r);
                Assert.IsNotNull(err);
            }

            [TestMethod]
            public void 空输入_原样返回()
            {
                string err;
                Assert.AreEqual("", _f.Beautify("", "    ", out err));
                Assert.IsNull(err);
            }
        }

        // ==================== SQL ====================

        [TestClass]
        public class Sql
        {
            private readonly SqlFormatter _f = new SqlFormatter();

            [TestMethod]
            public void 美化_关键字统一大写()
            {
                string err;
                var r = _f.Beautify("select id from users", "    ", out err);
                Assert.IsNull(err);
                StringAssert.Contains(r, "SELECT");
                StringAssert.Contains(r, "FROM");
            }

            [TestMethod]
            public void 美化_子句独占一行且缩进()
            {
                string err;
                var r = _f.Beautify("select a,b from t where x=1", "    ", out err);
                StringAssert.Contains(r, "SELECT\n    a,\n    b\nFROM\n    t\nWHERE\n    x = 1");
            }

            [TestMethod]
            public void 美化_AND放行首()
            {
                string err;
                var r = _f.Beautify("select * from t where a=1 and b=2", "    ", out err);
                StringAssert.Contains(r, "\n    AND b = 2");
            }
        }
    }
}
