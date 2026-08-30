using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.FileOrganize;

namespace IntraBox.Tests
{
    [TestClass]
    public class FileOrganizeStoreTests
    {
        [TestMethod]
        public void FormatUndoSummary_列出文件与路径()
        {
            var moves = new List<OrganizePlanItem>
            {
                new OrganizePlanItem
                {
                    Name = "a.jpg",
                    FromPath = @"C:\desk\a.jpg",
                    ToPath = @"C:\desk\2026-08-30\图片\a.jpg"
                }
            };
            string text = FileOrganizeStore.FormatUndoSummary(moves, 8);
            Assert.IsTrue(text.IndexOf("a.jpg", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(text.IndexOf(@"C:\desk\a.jpg", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(text.IndexOf(@"C:\desk\2026-08-30\图片\a.jpg", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(text.IndexOf("1 个文件", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void SaveFlush_规则保存不会丢掉撤销记录()
        {
            string dir = Path.Combine(Path.GetTempPath(), "intrabox-fo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "fileorganize.json");
            try
            {
                FileOrganizeStore.Save(path, new FileOrganizeState
                {
                    Dir = dir,
                    Sub = false,
                    Rules = new List<OrganizeRule>
                    {
                        new OrganizeRule { Kind = 0, Pattern = "png", TargetTemplate = "图片", Enabled = true }
                    }
                });
                FileOrganizeStore.SetLastUndo(new List<OrganizePlanItem>
                {
                    new OrganizePlanItem
                    {
                        Name = "shot.png",
                        FromPath = @"D:\桌面\shot.png",
                        ToPath = @"D:\桌面\2026-08-30\图片\shot.png"
                    }
                });
                FileOrganizeStore.Flush();

                string json = File.ReadAllText(path);
                Assert.IsTrue(json.IndexOf("shot.png", StringComparison.Ordinal) < 0);

                string undoPath = FileOrganizeStore.UndoPathFor(path);
                Assert.IsTrue(File.Exists(undoPath));
                string undoJson = File.ReadAllText(undoPath);
                Assert.IsTrue(undoJson.IndexOf("shot.png", StringComparison.Ordinal) >= 0);
                Assert.AreEqual(1, FileOrganizeStore.CopyLastUndo().Count);

                FileOrganizeStore.ClearLastUndo();
                Assert.AreEqual(0, FileOrganizeStore.CopyLastUndo().Count);
            }
            finally
            {
                FileOrganizeStore.ClearLastUndo();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void ClearLastUndo_不从规则JSON读回撤销()
        {
            string dir = Path.Combine(Path.GetTempPath(), "intrabox-fo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "fileorganize.json");
            try
            {
                FileOrganizeStore.SetLastUndo(new List<OrganizePlanItem>
                {
                    new OrganizePlanItem
                    {
                        Name = "keep.png",
                        FromPath = @"D:\a\keep.png",
                        ToPath = @"D:\b\keep.png"
                    }
                });
                File.WriteAllText(path,
                    "{" +
                    "\"Dir\":\"" + dir.Replace("\\", "\\\\") + "\"," +
                    "\"Sub\":false," +
                    "\"LastUndo\":[{\"Name\":\"zombie.png\",\"FromPath\":\"D:\\\\z\\\\zombie.png\",\"ToPath\":\"D:\\\\b\\\\zombie.png\"}]" +
                    "}");
                FileOrganizeStore.ClearLastUndo();
                Assert.AreEqual(0, FileOrganizeStore.CopyLastUndo().Count);
            }
            finally
            {
                FileOrganizeStore.ClearLastUndo();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
