using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class LauncherTreeTests
    {
        [TestMethod]
        public void Migrate_同名分类合成一个目录()
        {
            var old = new List<LauncherFavorite>
            {
                Fav("a", "Git", "app", @"C:\git.exe", "开发"),
                Fav("b", "Wiki", "url", "http://wiki.local", "开发"),
                Fav("c", "桌面", "folder", @"D:\desk", "")
            };
            var nodes = LauncherTree.MigrateFromFavorites(old);
            int cats = 0, favs = 0;
            string catUid = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].IsCategory)
                {
                    cats++;
                    catUid = nodes[i].Uid;
                    Assert.AreEqual("开发", nodes[i].Name);
                    Assert.AreEqual("", nodes[i].ParentUid);
                }
                else favs++;
            }
            Assert.AreEqual(1, cats);
            Assert.AreEqual(3, favs);
            var git = LauncherTree.Find(nodes, "a");
            var wiki = LauncherTree.Find(nodes, "b");
            var desk = LauncherTree.Find(nodes, "c");
            Assert.AreEqual(catUid, git.ParentUid);
            Assert.AreEqual(catUid, wiki.ParentUid);
            Assert.AreEqual("", desk.ParentUid);
        }

        [TestMethod]
        public void Flatten_折叠目录不展示子项()
        {
            var cat = Cat("p", "", "工具");
            var fav = Item("f", "p", "计算器", LauncherTarget.KindApp, @"C:\calc.exe");
            var nodes = new List<LauncherNode> { cat, fav };
            var open = LauncherTree.Flatten(nodes, new HashSet<string>(), "");
            Assert.AreEqual(2, open.Count);
            Assert.IsTrue(open[0].HasChildren);
            Assert.IsTrue(open[0].Expanded);
            var closed = LauncherTree.Flatten(nodes, new HashSet<string> { "p" }, "");
            Assert.AreEqual(1, closed.Count);
            Assert.AreEqual("p", closed[0].Node.Uid);
            Assert.IsFalse(closed[0].Expanded);
        }

        [TestMethod]
        public void Flatten_搜索带上祖先()
        {
            var cat = Cat("p", "", "开发");
            var fav = Item("f", "p", "GitLab", LauncherTarget.KindUrl, "http://git.local");
            var other = Item("o", "", "记事本", LauncherTarget.KindApp, @"C:\n.exe");
            var nodes = new List<LauncherNode> { cat, fav, other };
            var rows = LauncherTree.Flatten(nodes, new HashSet<string> { "p" }, "Git");
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("p", rows[0].Node.Uid);
            Assert.AreEqual("f", rows[1].Node.Uid);
            Assert.IsTrue(rows[0].Expanded);
        }

        [TestMethod]
        public void UniqueSiblingName_副本递增()
        {
            var nodes = new List<LauncherNode>
            {
                Item("1", "", "笔记 副本", LauncherTarget.KindApp, @"C:\a.exe")
            };
            Assert.AreEqual("笔记 副本 2", LauncherTree.UniqueSiblingName(nodes, "", "笔记 副本", null));
            Assert.AreEqual("其它", LauncherTree.UniqueSiblingName(nodes, "", "其它", null));
        }

        [TestMethod]
        public void Duplicate_目录连子树换新Uid()
        {
            var cat = Cat("p", "", "开发");
            var fav = Item("f", "p", "Git", LauncherTarget.KindApp, @"C:\g.exe");
            var nodes = new List<LauncherNode> { cat, fav };
            var copies = LauncherTree.DuplicateSubtree(nodes, "p");
            Assert.AreEqual(2, copies.Count);
            Assert.AreEqual("开发 副本", copies[0].Name);
            Assert.AreNotEqual("p", copies[0].Uid);
            Assert.AreNotEqual("f", copies[1].Uid);
            Assert.AreEqual(copies[0].Uid, copies[1].ParentUid);
            Assert.AreEqual("Git", copies[1].Name);
            Assert.IsFalse(copies[0].Pinned);
        }

        [TestMethod]
        public void ChildDepth_与上限()
        {
            var nodes = new List<LauncherNode>();
            string parent = "";
            for (int i = 0; i < 8; i++)
            {
                var c = Cat("c" + i, parent, "层" + i);
                nodes.Add(c);
                parent = c.Uid;
            }
            Assert.AreEqual(0, LauncherTree.ChildDepth(nodes, ""));
            Assert.AreEqual(8, LauncherTree.ChildDepth(nodes, "c7"));
            Assert.IsFalse(LauncherTree.CanAddChild(nodes, "c7"));
            Assert.IsTrue(LauncherTree.CanAddChild(nodes, "c6"));
        }

        [TestMethod]
        public void ParentPath_拼接()
        {
            var a = Cat("a", "", "开发");
            var b = Cat("b", "a", "工具");
            var f = Item("f", "b", "Git", LauncherTarget.KindApp, @"C:\g.exe");
            var nodes = new List<LauncherNode> { a, b, f };
            Assert.AreEqual("", LauncherTree.ParentPath(nodes, ""));
            Assert.AreEqual("开发", LauncherTree.ParentPath(nodes, "a"));
            Assert.AreEqual("开发/工具", LauncherTree.ParentPath(nodes, "b"));
        }

        [TestMethod]
        public void CountDescendants_含子目录()
        {
            var a = Cat("a", "", "A");
            var b = Cat("b", "a", "B");
            var f = Item("f", "b", "F", LauncherTarget.KindFile, @"C:\x.txt");
            var nodes = new List<LauncherNode> { a, b, f };
            Assert.AreEqual(2, LauncherTree.CountDescendants(nodes, "a"));
            Assert.AreEqual(1, LauncherTree.CountDescendants(nodes, "b"));
            Assert.AreEqual(0, LauncherTree.CountDescendants(nodes, "f"));
        }

        [TestMethod]
        public void OpenWith_校验与确认()
        {
            string err;
            Assert.IsTrue(LauncherTarget.TryValidateOpenWith(LauncherTarget.KindFile, "", out err));
            Assert.IsTrue(LauncherTarget.TryValidateOpenWith(LauncherTarget.KindApp, @"C:\a.bat", out err));
            Assert.IsFalse(LauncherTarget.TryValidateOpenWith(LauncherTarget.KindFile, @"C:\a.bat", out err));
            Assert.IsFalse(LauncherTarget.TryValidateOpenWith(LauncherTarget.KindFile, @"C:\a.dll", out err));
            Assert.IsTrue(LauncherTarget.TryValidateOpenWith(LauncherTarget.KindFile, @"C:\edit.exe", out err));
            Assert.IsTrue(LauncherTarget.NeedsOpenConfirm(LauncherTarget.KindApp, null));
            Assert.IsTrue(LauncherTarget.NeedsOpenConfirm(LauncherTarget.KindApp, true));
            Assert.IsFalse(LauncherTarget.NeedsOpenConfirm(LauncherTarget.KindApp, false));
            Assert.IsFalse(LauncherTarget.NeedsOpenConfirm(LauncherTarget.KindFile, true));
            Assert.AreEqual("默认", LauncherTarget.OpenWithLabel(LauncherTarget.KindFile, ""));
            Assert.AreEqual("edit.exe", LauncherTarget.OpenWithLabel(LauncherTarget.KindFile, @"C:\edit.exe"));
            Assert.AreEqual("", LauncherTarget.OpenWithLabel(LauncherTarget.KindApp, @"C:\edit.exe"));
            Assert.AreEqual("\"C:\\a b.txt\"", LauncherTarget.QuoteArg(@"C:\a b.txt"));
            Assert.AreEqual("系统默认", LauncherTarget.DefaultHandlerHint(""));
            Assert.AreEqual("系统默认（文件尚不存在）", LauncherTarget.DefaultHandlerHint(@"C:\no-such-intrabox-file-xyz.txt"));
        }

        [TestMethod]
        public void FlattenCategories_只含目录()
        {
            var cat = Cat("p", "", "工具");
            var sub = Cat("s", "p", "子类");
            var fav = Item("f", "p", "计算器", LauncherTarget.KindApp, @"C:\calc.exe");
            var rows = LauncherTree.FlattenCategories(new List<LauncherNode> { cat, sub, fav });
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("p", rows[0].Node.Uid);
            Assert.AreEqual("s", rows[1].Node.Uid);
            Assert.AreEqual(1, rows[1].Depth);
        }

        [TestMethod]
        public void CanMoveFavorite_只能移收藏到目录()
        {
            var cat = Cat("p", "", "工具");
            var fav = Item("f", "", "计算器", LauncherTarget.KindApp, @"C:\calc.exe");
            var nodes = new List<LauncherNode> { cat, fav };
            string err;
            Assert.IsTrue(LauncherTree.CanMoveFavorite(nodes, "f", "p", out err));
            Assert.IsFalse(LauncherTree.CanMoveFavorite(nodes, "p", "", out err));
            Assert.IsFalse(LauncherTree.CanMoveFavorite(nodes, "f", "missing", out err));
            fav.ParentUid = "p";
            Assert.IsTrue(LauncherTree.CanMoveFavorite(nodes, "f", "p", out err));
            var sysFav = Item(LauncherSystemCatalog.ItemUid("calc"), LauncherSystemCatalog.FolderUid, "计算器", LauncherTarget.KindApp, @"C:\Windows\System32\calc.exe");
            var sysCat = Cat(LauncherSystemCatalog.FolderUid, "", LauncherSystemCatalog.FolderName);
            var withSys = new List<LauncherNode> { sysCat, sysFav, cat, fav };
            Assert.IsFalse(LauncherTree.CanMoveFavorite(withSys, sysFav.Uid, "p", out err));
            Assert.IsFalse(LauncherTree.CanMoveFavorite(withSys, "f", LauncherSystemCatalog.FolderUid, out err));
        }

        private static LauncherFavorite Fav(string uid, string name, string kind, string target, string cat)
        {
            return new LauncherFavorite
            {
                Uid = uid,
                Name = name,
                Kind = kind,
                Target = target,
                Category = cat,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        private static LauncherNode Cat(string uid, string parent, string name)
        {
            return LauncherTree.NewCategory(uid, parent, name, false, DateTime.Now, DateTime.Now);
        }

        private static LauncherNode Item(string uid, string parent, string name, string kind, string target)
        {
            return new LauncherNode
            {
                Uid = uid,
                Type = LauncherTree.TypeFavorite,
                ParentUid = parent,
                Name = name,
                Kind = kind,
                Target = target,
                OpenWith = "",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
    }
}
