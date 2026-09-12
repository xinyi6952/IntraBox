using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class LauncherSystemCatalogTests
    {
        [TestMethod]
        public void Build_只收录存在的目标并建系统目录()
        {
            string sys = @"C:\Windows\System32";
            string desk = @"C:\Users\a\Desktop";
            var exist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(sys, "calc.exe"),
                Path.Combine(sys, "control.exe"),
                desk
            };
            var nodes = LauncherSystemCatalog.Build(null, sys, desk, exist.Contains, exist.Contains);
            Assert.IsNotNull(LauncherTree.Find(nodes, LauncherSystemCatalog.FolderUid));
            Assert.AreEqual(LauncherSystemCatalog.FolderName, LauncherTree.Find(nodes, LauncherSystemCatalog.FolderUid).Name);
            var calc = LauncherTree.Find(nodes, LauncherSystemCatalog.ItemUid("calc"));
            Assert.IsNotNull(calc);
            Assert.AreEqual("计算器", calc.Name);
            Assert.AreEqual(false, calc.ConfirmOpen);
            Assert.IsFalse(calc.Pinned);
            Assert.IsNotNull(LauncherTree.Find(nodes, LauncherSystemCatalog.ItemUid("control")));
            Assert.IsNull(LauncherTree.Find(nodes, LauncherSystemCatalog.ItemUid("notepad")));
            Assert.IsNotNull(LauncherTree.Find(nodes, LauncherSystemCatalog.ItemUid("desktop")));
            Assert.AreEqual(4, nodes.Count);
        }

        [TestMethod]
        public void Build_全部缺失则不建空目录()
        {
            var nodes = LauncherSystemCatalog.Build(null, @"C:\Windows\System32", @"C:\Users\a\Desktop", p => false, p => false);
            Assert.AreEqual(0, nodes.Count);
        }

        [TestMethod]
        public void Build_置顶只作用于系统条目()
        {
            string sys = @"C:\Windows\System32";
            var exist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(sys, "calc.exe")
            };
            var pin = new[] { LauncherSystemCatalog.ItemUid("calc"), LauncherSystemCatalog.FolderUid };
            var nodes = LauncherSystemCatalog.Build(pin, sys, "", exist.Contains, exist.Contains);
            Assert.IsTrue(LauncherTree.Find(nodes, LauncherSystemCatalog.ItemUid("calc")).Pinned);
            Assert.IsFalse(LauncherTree.Find(nodes, LauncherSystemCatalog.FolderUid).Pinned);
        }

        [TestMethod]
        public void Flatten_系统目录默认折叠不展示子项()
        {
            string sys = @"C:\Windows\System32";
            var exist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(sys, "calc.exe")
            };
            var built = LauncherSystemCatalog.Build(null, sys, "", exist.Contains, exist.Contains);
            var collapsed = new HashSet<string> { LauncherSystemCatalog.FolderUid };
            var rows = LauncherTree.Flatten(built, collapsed, "");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(LauncherSystemCatalog.FolderUid, rows[0].Node.Uid);
            Assert.IsTrue(rows[0].HasChildren);
            Assert.IsFalse(rows[0].Expanded);
        }

        [TestMethod]
        public void IsNotepadItem_仅系统记事本()
        {
            Assert.IsTrue(LauncherSystemCatalog.IsNotepadItem(LauncherSystemCatalog.ItemUid(LauncherSystemCatalog.NotepadId)));
            Assert.IsFalse(LauncherSystemCatalog.IsNotepadItem(LauncherSystemCatalog.ItemUid("calc")));
            Assert.IsFalse(LauncherSystemCatalog.IsNotepadItem(LauncherSystemCatalog.FolderUid));
        }

        [TestMethod]
        public void Flatten_系统目录排在根第一且不可作移动目标()
        {
            string sys = @"C:\Windows\System32";
            var exist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(sys, "calc.exe")
            };
            var built = LauncherSystemCatalog.Build(null, sys, "", exist.Contains, exist.Contains);
            built.Add(LauncherTree.NewCategory("u1", "", "开发", true, DateTime.Now, DateTime.Now));
            var rows = LauncherTree.Flatten(built, new HashSet<string>(), "");
            Assert.AreEqual(LauncherSystemCatalog.FolderUid, rows[0].Node.Uid);
            var cats = LauncherTree.FlattenCategories(built);
            for (int i = 0; i < cats.Count; i++)
                Assert.AreNotEqual(LauncherSystemCatalog.FolderUid, cats[i].Node.Uid);
        }

        [TestMethod]
        public void TryCreateEmptyDocument_空白未命名()
        {
            string path = LauncherNotepad.TryCreateEmptyDocument();
            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(LauncherNotepad.EmptyName, Path.GetFileName(path));
            Assert.AreEqual(0, new FileInfo(path).Length);
            try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { }
        }

        [TestMethod]
        public void IsSystemUid_前缀判断()
        {
            Assert.IsTrue(LauncherSystemCatalog.IsSystemFolder(LauncherSystemCatalog.FolderUid));
            Assert.IsTrue(LauncherSystemCatalog.IsSystemItem(LauncherSystemCatalog.ItemUid("calc")));
            Assert.IsFalse(LauncherSystemCatalog.IsSystemItem(LauncherSystemCatalog.FolderUid));
            Assert.IsFalse(LauncherSystemCatalog.IsSystemUid("abc"));
            Assert.IsFalse(LauncherSystemCatalog.IsSystemUid(""));
        }
    }
}
