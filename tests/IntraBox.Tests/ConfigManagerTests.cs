using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class ConfigManagerTests
    {
        [TestMethod]
        public void AppendNewVisibleToolKeys_空或默认_不改动()
        {
            Assert.IsNull(ConfigManager.AppendNewVisibleToolKeys(null));
            var empty = new string[0];
            Assert.AreSame(empty, ConfigManager.AppendNewVisibleToolKeys(empty));
        }

        [TestMethod]
        public void AppendNewVisibleToolKeys_非空白名单_追加新工具且不重复()
        {
            var current = new[] { "clipboard", "screenshot", "notes" };
            var next = ConfigManager.AppendNewVisibleToolKeys(current);
            Assert.AreNotSame(current, next);
            Assert.AreEqual("clipboard", next[0]);
            Assert.AreEqual("screenshot", next[1]);
            Assert.AreEqual("notes", next[2]);
            Assert.AreEqual("launcher", next[3]);
            Assert.AreEqual("todo", next[4]);
            Assert.AreEqual("vault", next[5]);
            Assert.AreEqual(6, next.Length);

            int notes = 0, todo = 0, vault = 0, launcher = 0;
            for (int i = 0; i < next.Length; i++)
            {
                if (next[i] == "notes") notes++;
                if (next[i] == "todo") todo++;
                if (next[i] == "vault") vault++;
                if (next[i] == "launcher") launcher++;
            }
            Assert.AreEqual(1, notes);
            Assert.AreEqual(1, todo);
            Assert.AreEqual(1, vault);
            Assert.AreEqual(1, launcher);
        }

        [TestMethod]
        public void AppendNewVisibleToolKeys_已含全部新key_原样返回()
        {
            var current = new[] { "clipboard", "launcher", "todo", "notes", "vault", "screenshot" };
            Assert.AreSame(current, ConfigManager.AppendNewVisibleToolKeys(current));
        }

        [TestMethod]
        public void AppendNewVisibleToolKeys_追加顺序与效率工具默认一致()
        {
            Assert.AreEqual("launcher", NavOrder.DefaultToolKeys[1]);
            Assert.AreEqual("todo", NavOrder.DefaultToolKeys[2]);
            Assert.AreEqual("notes", NavOrder.DefaultToolKeys[3]);
            Assert.AreEqual("vault", NavOrder.DefaultToolKeys[4]);
            var next = ConfigManager.AppendNewVisibleToolKeys(new[] { "clipboard" });
            Assert.AreEqual("clipboard", next[0]);
            Assert.AreEqual("launcher", next[1]);
            Assert.AreEqual("todo", next[2]);
            Assert.AreEqual("notes", next[3]);
            Assert.AreEqual("vault", next[4]);
        }

        [TestMethod]
        public void EnsureNewToolsVisible_空白名单_不改Settings也不写盘()
        {
            var cm = ConfigManager.Instance;
            var before = cm.Settings.VisibleToolKeys;
            try
            {
                cm.Settings.VisibleToolKeys = null;
                cm.EnsureNewToolsVisible();
                Assert.IsNull(cm.Settings.VisibleToolKeys);

                cm.Settings.VisibleToolKeys = new string[0];
                cm.EnsureNewToolsVisible();
                Assert.AreEqual(0, cm.Settings.VisibleToolKeys.Length);
            }
            finally
            {
                cm.Settings.VisibleToolKeys = before;
            }
        }
    }
}
