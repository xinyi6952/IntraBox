using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.FileOrganize;

namespace IntraBox.Tests
{
    [TestClass]
    public class OrganizeEngineTests
    {
        [TestMethod]
        public void ExpandFolder_含扩展名变量()
        {
            string folder = OrganizeEngine.ExpandFolder("图片/{ext}", @"C:\tmp\shot.JPG");
            Assert.IsTrue(folder.IndexOf("图片") >= 0);
            Assert.IsTrue(folder.IndexOf("JPG") >= 0 || folder.IndexOf("jpg") >= 0);
        }

        [TestMethod]
        public void Matches_扩展名忽略大小写()
        {
            string note;
            var rule = new OrganizeRule { Kind = 0, Pattern = "*.jpg", TargetTemplate = "图片/{date}" };
            Assert.IsTrue(OrganizeEngine.Matches(rule, @"C:\a\b.JPG", out note));
            Assert.IsFalse(OrganizeEngine.Matches(rule, @"C:\a\b.png", out note));
        }

        [TestMethod]
        public void FileCategory_常见类型()
        {
            Assert.AreEqual("图片", OrganizeEngine.FileCategory("png"));
            Assert.AreEqual("文档", OrganizeEngine.FileCategory("pdf"));
            Assert.AreEqual("其他", OrganizeEngine.FileCategory("xyz"));
        }

        [TestMethod]
        public void ExpandFolder_日期变量_含年月日()
        {
            string folder = OrganizeEngine.ExpandFolder("{type}/{date}", @"C:\tmp\a.png");
            Assert.IsTrue(folder.IndexOf("图片") >= 0);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(folder, @"\d{4}-\d{2}-\d{2}"));
        }

        [TestMethod]
        public void Matches_文件名包含()
        {
            string note;
            var rule = new OrganizeRule { Kind = 1, Pattern = "shot", TargetTemplate = "截图" };
            Assert.IsTrue(OrganizeEngine.Matches(rule, @"C:\a\my_shot.png", out note));
            Assert.IsFalse(OrganizeEngine.Matches(rule, @"C:\a\other.png", out note));
        }

        [TestMethod]
        public void DestFolder_当天日期为根_规则目录为子目录()
        {
            var now = new DateTime(2026, 8, 24);
            string folder = OrganizeEngine.DestFolder("图片", @"C:\tmp\a.jpg", now);
            Assert.AreEqual(System.IO.Path.Combine("2026-08-24", "图片"), folder);
        }

        [TestMethod]
        public void DestFolder_去掉规则里的日期变量避免重复()
        {
            var now = new DateTime(2026, 8, 24);
            string folder = OrganizeEngine.DestFolder("图片/{date}", @"C:\tmp\a.jpg", now);
            Assert.AreEqual(System.IO.Path.Combine("2026-08-24", "图片"), folder);
        }

        [TestMethod]
        public void OrganizeRule_缺省Enabled为启用()
        {
            Assert.IsTrue(new OrganizeRule().Enabled);
        }

        [TestMethod]
        public void FileOrganizeStore_往返保留启用状态()
        {
            string path = Path.Combine(Path.GetTempPath(), "intrabox-fo-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var state = new FileOrganizeState
                {
                    Dir = @"D:\桌面",
                    Sub = true,
                    Rules = new List<OrganizeRule>
                    {
                        new OrganizeRule { Kind = 0, Pattern = "jpg", TargetTemplate = "图片", Enabled = true },
                        new OrganizeRule { Kind = 0, Pattern = "zip", TargetTemplate = "压缩包", Enabled = false }
                    }
                };
                FileOrganizeStore.Save(path, state);
                FileOrganizeStore.Flush();
                var loaded = FileOrganizeStore.Load(path);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(@"D:\桌面", loaded.Dir);
                Assert.IsTrue(loaded.Sub);
                Assert.AreEqual(2, loaded.Rules.Count);
                Assert.IsTrue(loaded.Rules[0].Enabled);
                Assert.IsFalse(loaded.Rules[1].Enabled);
                Assert.AreEqual("zip", loaded.Rules[1].Pattern);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void FileOrganizeStore_旧JSON无Enabled视为启用()
        {
            string path = Path.Combine(Path.GetTempPath(), "intrabox-fo-old-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path,
                    "{\"Dir\":\"C:\\\\a\",\"Sub\":false,\"Rules\":[{\"Kind\":0,\"Pattern\":\"png\",\"TargetTemplate\":\"图片\"}]}");
                var loaded = FileOrganizeStore.Load(path);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(1, loaded.Rules.Count);
                Assert.IsTrue(loaded.Rules[0].Enabled);
                Assert.AreEqual("png", loaded.Rules[0].Pattern);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void BuildPlan_跳过未启用规则()
        {
            string dir = Path.Combine(Path.GetTempPath(), "intrabox-org-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.jpg"), "x");
                var rules = new List<OrganizeRule>
                {
                    new OrganizeRule { Kind = 0, Pattern = "jpg", TargetTemplate = "图片", Enabled = false },
                    new OrganizeRule { Kind = 0, Pattern = "jpg", TargetTemplate = "相册", Enabled = true }
                };
                var plan = OrganizeEngine.BuildPlan(dir, false, rules);
                Assert.AreEqual(1, plan.Count);
                Assert.IsTrue(plan[0].Folder.IndexOf("相册") >= 0);
                Assert.IsTrue(plan[0].Folder.IndexOf("图片") < 0);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
