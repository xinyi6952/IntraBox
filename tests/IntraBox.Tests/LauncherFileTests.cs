using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class LauncherFileTests
    {
        [TestMethod]
        public void TryLoad_旧Favorites迁成目录树并需回写()
        {
            string json = @"{
  ""Favorites"": [
    { ""Uid"": ""a"", ""Name"": ""Git"", ""Kind"": ""app"", ""Target"": ""C:\\git.exe"", ""Category"": ""开发"", ""Pinned"": true },
    { ""Uid"": ""b"", ""Name"": ""Wiki"", ""Kind"": ""url"", ""Target"": ""http://wiki.local"", ""Category"": ""开发"" }
  ],
  ""Recents"": [""a"", ""a"", """"]
}";
            var loaded = LauncherFile.TryLoad(json);
            Assert.IsTrue(loaded.Ok);
            Assert.IsTrue(loaded.NeedsRewrite);
            Assert.AreEqual(3, loaded.Nodes.Count);
            Assert.IsNotNull(LauncherTree.Find(loaded.Nodes, "a"));
            Assert.AreEqual("开发", LauncherTree.Find(loaded.Nodes, LauncherTree.Find(loaded.Nodes, "a").ParentUid).Name);
            Assert.AreEqual(1, loaded.Recents.Count);
            Assert.AreEqual("a", loaded.Recents[0]);
            Assert.AreEqual(0, loaded.SystemPinned.Count);
        }

        [TestMethod]
        public void TryLoad_缺FormatVersion的树需回写缺省字段()
        {
            string json = @"{
  ""Nodes"": [
    { ""Uid"": ""u1"", ""Type"": ""favorite"", ""Name"": ""记事本"", ""Kind"": ""app"", ""Target"": ""C:\\n.exe"" }
  ],
  ""Recents"": [""u1""]
}";
            var loaded = LauncherFile.TryLoad(json);
            Assert.IsTrue(loaded.Ok);
            Assert.IsTrue(loaded.NeedsRewrite);
            var n = LauncherTree.Find(loaded.Nodes, "u1");
            Assert.IsNotNull(n);
            Assert.IsNull(n.ConfirmOpen);
            Assert.AreEqual(1, loaded.Recents.Count);
            Assert.AreEqual(0, loaded.SystemPinned.Count);
        }

        [TestMethod]
        public void TryLoad_当前格式不回写()
        {
            string json = LauncherFile.Save(
                new System.Collections.Generic.List<LauncherNode> { Fav("u1", "Git", LauncherTarget.KindApp, @"C:\git.exe") },
                new[] { "u1" },
                new[] { LauncherSystemCatalog.ItemUid("calc") });
            var loaded = LauncherFile.TryLoad(json);
            Assert.IsTrue(loaded.Ok);
            Assert.IsFalse(loaded.NeedsRewrite);
            Assert.AreEqual(1, loaded.Nodes.Count);
            Assert.AreEqual(1, loaded.SystemPinned.Count);
        }

        [TestMethod]
        public void TryLoad_用户数据里的sys前缀被剥离()
        {
            string json = @"{
  ""Nodes"": [
    { ""Uid"": ""sys:calc"", ""Type"": ""favorite"", ""Name"": ""计算器"", ""Kind"": ""app"", ""Target"": ""C:\\Windows\\System32\\calc.exe"" },
    { ""Uid"": ""u1"", ""Type"": ""favorite"", ""ParentUid"": ""sys:folder"", ""Name"": ""Git"", ""Kind"": ""app"", ""Target"": ""C:\\git.exe"" }
  ]
}";
            var loaded = LauncherFile.TryLoad(json);
            Assert.IsTrue(loaded.Ok);
            Assert.IsTrue(loaded.NeedsRewrite);
            Assert.IsNull(LauncherTree.Find(loaded.Nodes, "sys:calc"));
            var git = LauncherTree.Find(loaded.Nodes, "u1");
            Assert.IsNotNull(git);
            Assert.AreEqual("", git.ParentUid);
        }

        [TestMethod]
        public void TryLoad_非法JSON失败()
        {
            var loaded = LauncherFile.TryLoad("{ not json");
            Assert.IsFalse(loaded.Ok);
            Assert.AreEqual(0, loaded.Nodes.Count);
        }

        [TestMethod]
        public void TryLoad_空白视为空树需回写()
        {
            var loaded = LauncherFile.TryLoad("   ");
            Assert.IsTrue(loaded.Ok);
            Assert.IsTrue(loaded.NeedsRewrite);
            Assert.AreEqual(0, loaded.Nodes.Count);
        }

        [TestMethod]
        public void Save_含FormatVersion且不含Favorites()
        {
            string json = LauncherFile.Save(null, null, null);
            StringAssert.Contains(json, "\"FormatVersion\": " + LauncherFile.CurrentFormat);
            Assert.IsFalse(json.IndexOf("Favorites", StringComparison.OrdinalIgnoreCase) >= 0);
            StringAssert.Contains(json, "Nodes");
            StringAssert.Contains(json, "Recents");
            StringAssert.Contains(json, "SystemPinned");
        }

        [TestMethod]
        public void Save后再TryLoad_保持当前格式()
        {
            var nodes = new System.Collections.Generic.List<LauncherNode>
            {
                Fav("u1", "Wiki", LauncherTarget.KindUrl, "http://wiki.local")
            };
            string json = LauncherFile.Save(nodes, new[] { "u1" }, null);
            var loaded = LauncherFile.TryLoad(json);
            Assert.IsTrue(loaded.Ok);
            Assert.IsFalse(loaded.NeedsRewrite);
            Assert.AreEqual("Wiki", LauncherTree.Find(loaded.Nodes, "u1").Name);
        }

        private static LauncherNode Fav(string uid, string name, string kind, string target)
        {
            return new LauncherNode
            {
                Uid = uid,
                Type = LauncherTree.TypeFavorite,
                ParentUid = "",
                Name = name,
                Kind = kind,
                Target = target,
                ConfirmOpen = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
    }

    [TestClass]
    public class DataFileGuardTests
    {
        [TestMethod]
        public void TryQuarantine_不存在视为成功()
        {
            string path = Path.Combine(Path.GetTempPath(), "intrabox-missing-" + Guid.NewGuid().ToString("N") + ".json");
            Assert.IsTrue(DataFileGuard.TryQuarantine(path));
        }

        [TestMethod]
        public void TryQuarantine_挪成bak且第二次带时间戳()
        {
            string dir = Path.Combine(Path.GetTempPath(), "intrabox-bak-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "launcher.json");
            try
            {
                File.WriteAllText(path, "{broken");
                Assert.IsTrue(DataFileGuard.TryQuarantine(path));
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".bak"));
                Assert.AreEqual("{broken", File.ReadAllText(path + ".bak"));

                File.WriteAllText(path, "{again");
                Assert.IsTrue(DataFileGuard.TryQuarantine(path));
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".bak"));
                string[] extras = Directory.GetFiles(dir, "launcher.json.*.bak");
                Assert.AreEqual(1, extras.Length);
                Assert.AreEqual("{again", File.ReadAllText(extras[0]));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
