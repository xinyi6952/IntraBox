using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class LauncherSearchTests
    {
        [TestMethod]
        public void NameScore_开头优先于包含()
        {
            Assert.AreEqual(100, LauncherSearch.NameScore("剪", "剪贴板历史"));
            Assert.AreEqual(50, LauncherSearch.NameScore("贴", "剪贴板历史"));
            Assert.AreEqual(0, LauncherSearch.NameScore("xyz", "剪贴板历史"));
            Assert.AreEqual(1, LauncherSearch.NameScore("", "剪贴板历史"));
            Assert.AreEqual(0, LauncherSearch.NameScore("a", ""));
        }

        [TestMethod]
        public void Rank_置顶先于最近先于开头()
        {
            var hits = new List<LauncherHit>
            {
                Hit("tool:notes", "笔记", false),
                Hit("fav:1", "工作笔记", true),
                Hit("tool:clipboard", "剪贴板历史", false)
            };
            var recents = new List<string> { "tool:notes" };
            var ranked = LauncherSearch.Rank(hits, "记", recents);
            Assert.AreEqual(2, ranked.Count);
            Assert.AreEqual("fav:1", ranked[0].Id);
            Assert.AreEqual("tool:notes", ranked[1].Id);
        }

        [TestMethod]
        public void Rank_最近优先于开头匹配()
        {
            var hits = new List<LauncherHit>
            {
                Hit("tool:a", "本机信息", false),
                Hit("tool:b", "笔记本", false)
            };
            var recents = new List<string> { "tool:b" };
            var ranked = LauncherSearch.Rank(hits, "本", recents);
            Assert.AreEqual("tool:b", ranked[0].Id);
            Assert.AreEqual("tool:a", ranked[1].Id);
        }

        [TestMethod]
        public void Rank_最近优先于仅包含()
        {
            var hits = new List<LauncherHit>
            {
                Hit("tool:a", "主机信息", false),
                Hit("tool:b", "本机信息", false)
            };
            var recents = new List<string> { "tool:a" };
            var ranked = LauncherSearch.Rank(hits, "机", recents);
            Assert.AreEqual("tool:a", ranked[0].Id);
            Assert.AreEqual("tool:b", ranked[1].Id);
        }

        [TestMethod]
        public void EmptyQuery_置顶收藏_最近_工具()
        {
            var catalog = new List<LauncherHit>
            {
                HitFav("fav:u", "未置顶", false),
                HitFav("fav:p", "置顶项", true),
                Hit("tool:clipboard", "剪贴板历史", false),
                Hit("tool:todo", "任务计划", false),
                Hit("todo:x", "不该出现", false)
            };
            var recents = new List<string> { "fav:u", "todo:x" };
            var empty = LauncherSearch.EmptyQuery(catalog, recents, 1);
            Assert.AreEqual(3, empty.Count);
            Assert.AreEqual("fav:p", empty[0].Id);
            Assert.AreEqual("fav:u", empty[1].Id);
            Assert.AreEqual("tool:clipboard", empty[2].Id);
        }

        [TestMethod]
        public void Rank_上限50()
        {
            var hits = new List<LauncherHit>();
            for (int i = 0; i < 60; i++)
                hits.Add(Hit("tool:" + i, "工具" + i, false));
            var ranked = LauncherSearch.Rank(hits, "工具", new List<string>());
            Assert.AreEqual(LauncherSearch.MaxResults, ranked.Count);
        }

        [TestMethod]
        public void Rank_可按收藏分类匹配()
        {
            var hits = new List<LauncherHit>
            {
                new LauncherHit { Id = "fav:1", Kind = LauncherHit.KindFavorite, Title = "GitLab", Category = "开发工具", Pinned = false },
                new LauncherHit { Id = "tool:notes", Kind = LauncherHit.KindTool, Title = "笔记", Category = "", Pinned = false }
            };
            var ranked = LauncherSearch.Rank(hits, "开发", new List<string>());
            Assert.AreEqual(1, ranked.Count);
            Assert.AreEqual("fav:1", ranked[0].Id);
        }

        [TestMethod]
        public void CategoryLabel_空为未分类()
        {
            Assert.AreEqual("未分类", LauncherTarget.CategoryLabel(null));
            Assert.AreEqual("未分类", LauncherTarget.CategoryLabel("  "));
            Assert.AreEqual("内网", LauncherTarget.CategoryLabel(" 内网 "));
            Assert.AreEqual("", LauncherTarget.NormalizeCategory("  "));
        }

        [TestMethod]
        public void Target_拒绝脚本与非法URL()
        {
            string err;
            Assert.IsTrue(LauncherTarget.IsBlockedScript(@"C:\a.bat"));
            Assert.IsTrue(LauncherTarget.IsBlockedScript(@"C:\a.PS1"));
            Assert.IsFalse(LauncherTarget.IsBlockedScript(@"C:\a.exe"));
            Assert.IsTrue(LauncherTarget.IsAllowedUrl("http://git.local"));
            Assert.IsTrue(LauncherTarget.IsAllowedUrl("HTTPS://wiki"));
            Assert.IsFalse(LauncherTarget.IsAllowedUrl("file://c:/a"));
            Assert.IsFalse(LauncherTarget.IsAllowedUrl("javascript:alert(1)"));
            Assert.IsFalse(LauncherTarget.TryValidate(LauncherTarget.KindUrl, "ftp://x", out err));
            Assert.IsFalse(LauncherTarget.TryValidate(LauncherTarget.KindApp, @"C:\run.bat", out err));
            Assert.IsFalse(LauncherTarget.TryValidate(LauncherTarget.KindFile, @"C:\run.cmd", out err));
            Assert.IsTrue(LauncherTarget.TryValidate(LauncherTarget.KindApp, @"C:\app.exe", out err));
            Assert.IsTrue(LauncherTarget.TryValidate(LauncherTarget.KindFolder, @"D:\share", out err));
        }

        private static LauncherHit Hit(string id, string title, bool pinned)
        {
            return new LauncherHit
            {
                Id = id,
                Kind = id.StartsWith("fav:") ? LauncherHit.KindFavorite : (id.StartsWith("todo:") ? LauncherHit.KindTodo : LauncherHit.KindTool),
                Title = title,
                Payload = id,
                Pinned = pinned
            };
        }

        private static LauncherHit HitFav(string id, string title, bool pinned)
        {
            return new LauncherHit
            {
                Id = id,
                Kind = LauncherHit.KindFavorite,
                Title = title,
                Payload = id,
                Pinned = pinned
            };
        }
    }
}
