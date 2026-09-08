using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.Notes;

namespace IntraBox.Tests
{
    [TestClass]
    public class NoteTreeTests
    {
        [TestMethod]
        public void Normalize_缺Type视为笔记()
        {
            var n = new NoteItem { Uid = "a", Title = "纪要", Kind = NoteKind.Markdown };
            var got = NoteTree.Normalize(n);
            Assert.IsFalse(got.IsFolder);
            Assert.AreEqual(NoteNodeType.Note, got.Type);
            Assert.AreEqual("纪要", got.Title);
        }

        [TestMethod]
        public void Flatten_折叠目录不展示子项()
        {
            var folder = Folder("p", "", "工作");
            var note = Note("n", "p", "纪要", NoteKind.Rich);
            var items = new List<NoteItem> { folder, note };
            var open = NoteTree.Flatten(items, new HashSet<string>(), "", 0, "title", true);
            Assert.AreEqual(2, open.Count);
            Assert.IsTrue(open[0].HasChildren);
            Assert.IsTrue(open[0].Expanded);
            var closed = NoteTree.Flatten(items, new HashSet<string> { "p" }, "", 0, "title", true);
            Assert.AreEqual(1, closed.Count);
            Assert.AreEqual("p", closed[0].Item.Uid);
            Assert.IsFalse(closed[0].Expanded);
        }

        [TestMethod]
        public void Flatten_搜索带上祖先()
        {
            var folder = Folder("p", "", "开发");
            var note = Note("n", "p", "GitLab", NoteKind.Rich);
            var other = Note("o", "", "备忘", NoteKind.Markdown);
            var items = new List<NoteItem> { folder, note, other };
            var rows = NoteTree.Flatten(items, new HashSet<string> { "p" }, "Git", 0, "title", true);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("p", rows[0].Item.Uid);
            Assert.AreEqual("n", rows[1].Item.Uid);
            Assert.IsTrue(rows[0].Expanded);
        }

        [TestMethod]
        public void Flatten_类型筛选带上祖先目录()
        {
            var folder = Folder("p", "", "工作");
            var rich = Note("r", "p", "纪要", NoteKind.Rich);
            var md = Note("m", "p", "手册", NoteKind.Markdown);
            var items = new List<NoteItem> { folder, rich, md };
            var rows = NoteTree.Flatten(items, new HashSet<string>(), "", 2, "title", true);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("p", rows[0].Item.Uid);
            Assert.AreEqual("m", rows[1].Item.Uid);
        }

        [TestMethod]
        public void UniqueSiblingName_同目录递增()
        {
            var items = new List<NoteItem>
            {
                Note("1", "", "无标题笔记", NoteKind.Rich)
            };
            Assert.AreEqual("无标题笔记 2", NoteTree.UniqueSiblingName(items, "", "无标题笔记", null));
        }

        [TestMethod]
        public void CanMoveNote_不能移动目录()
        {
            var folder = Folder("p", "", "工作");
            var items = new List<NoteItem> { folder };
            string err;
            Assert.IsFalse(NoteTree.CanMoveNote(items, "p", "", out err));
        }

        [TestMethod]
        public void CanMoveNote_笔记可移到目录()
        {
            var folder = Folder("p", "", "工作");
            var note = Note("n", "", "纪要", NoteKind.Rich);
            var items = new List<NoteItem> { folder, note };
            string err;
            Assert.IsTrue(NoteTree.CanMoveNote(items, "n", "p", out err));
            Assert.AreEqual("", err);
        }

        [TestMethod]
        public void Sanitize_坏父节点提到根()
        {
            var note = Note("n", "missing", "纪要", NoteKind.Rich);
            var items = NoteTree.Sanitize(new List<NoteItem> { note });
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("", items[0].ParentUid);
        }

        private static NoteItem Folder(string uid, string parent, string title)
        {
            return new NoteItem
            {
                Uid = uid,
                Title = title,
                Type = NoteNodeType.Folder,
                ParentUid = parent,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        private static NoteItem Note(string uid, string parent, string title, int kind)
        {
            return new NoteItem
            {
                Uid = uid,
                Title = title,
                Kind = kind,
                Type = NoteNodeType.Note,
                ParentUid = parent,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
    }
}
