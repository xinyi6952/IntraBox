using System;
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
    }
}
