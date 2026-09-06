using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class HostsFileTests
    {
        [TestMethod]
        public void 保存_保留注释空行和相对位置()
        {
            string src =
                "# 文件头注释\r\n" +
                "\r\n" +
                "127.0.0.1 localhost\r\n" +
                "\r\n" +
                "# 文件尾\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            Assert.AreEqual(1, maps.Count);
            maps[0].Host = "local.test";
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual(
                "# 文件头注释\r\n" +
                "\r\n" +
                "127.0.0.1 local.test\r\n" +
                "\r\n" +
                "# 文件尾\r\n",
                outText);
        }

        [TestMethod]
        public void 未改映射行_原样写回含多余空格()
        {
            string src = "  127.0.0.1\tlocalhost   # keep\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual(src, outText);
        }

        [TestMethod]
        public void 禁用已有行_只加井号不加空格()
        {
            string src = "127.0.0.1 foo.local\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            maps[0].Enabled = false;
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("#127.0.0.1 foo.local\r\n", outText);
        }

        [TestMethod]
        public void 启用无空格注释行_只去掉井号()
        {
            string src = "#127.0.0.1 foo.local\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            Assert.IsFalse(maps[0].Enabled);
            maps[0].Enabled = true;
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("127.0.0.1 foo.local\r\n", outText);
        }

        [TestMethod]
        public void 新增禁用映射_井号后无空格()
        {
            string src = "# head\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = new System.Collections.Generic.List<HostsEntry>();
            maps.Add(new HostsEntry
            {
                IsMapping = true,
                Enabled = false,
                Ip = "10.0.0.1",
                Host = "b.local",
                Comment = ""
            });
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("# head\r\n#10.0.0.1 b.local\r\n", outText);
        }

        [TestMethod]
        public void 启用_去掉行首空格和井号后空格_保留其余间距()
        {
            string src = "  #    127.0.0.1    foo.local   # keep\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            Assert.IsFalse(maps[0].Enabled);
            maps[0].Enabled = true;
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("127.0.0.1    foo.local   # keep\r\n", outText);
        }

        [TestMethod]
        public void 禁用_去掉行首空格_井号后不加空格_保留其余间距()
        {
            string src = "  127.0.0.1\tlocalhost   # keep\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            maps[0].Enabled = false;
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("#127.0.0.1\tlocalhost   # keep\r\n", outText);
        }

        [TestMethod]
        public void 删除映射_注释空行仍在()
        {
            string src = "# head\r\n127.0.0.1 a.local\r\n# tail\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = new System.Collections.Generic.List<HostsEntry>();
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("# head\r\n# tail\r\n", outText);
        }

        [TestMethod]
        public void 新增映射_追加在文件末尾()
        {
            string src = "# head\r\n127.0.0.1 a.local\r\n";
            var lines = HostsFileHelper.Parse(src);
            var maps = Mappings(lines);
            maps.Add(new HostsEntry
            {
                IsMapping = true,
                Enabled = true,
                Ip = "10.0.0.1",
                Host = "b.local",
                Comment = ""
            });
            string outText = HostsFileHelper.Render(lines, maps, "\r\n");
            Assert.AreEqual("# head\r\n127.0.0.1 a.local\r\n10.0.0.1 b.local\r\n", outText);
        }

        [TestMethod]
        public void 纯注释行_不当成映射()
        {
            var e = HostsFileHelper.ParseLine("# Copyright (c) 1993-2009 Microsoft Corp.");
            Assert.IsFalse(e.IsMapping);
        }

        [TestMethod]
        public void 改动摘要_新增删除修改()
        {
            string oldText =
                "127.0.0.1 keep.local\r\n" +
                "127.0.0.1 gone.local\r\n" +
                "10.0.0.1 edit.local\r\n";
            string newText =
                "127.0.0.1 keep.local\r\n" +
                "10.0.0.2 edit.local\r\n" +
                "127.0.0.1 new.local\r\n";
            string s = HostsFileHelper.DescribeChanges(oldText, newText);
            StringAssert.Contains(s, "删除：127.0.0.1  gone.local");
            StringAssert.Contains(s, "新增：127.0.0.1  new.local");
            StringAssert.Contains(s, "修改：10.0.0.1  edit.local");
            StringAssert.Contains(s, "IP 10.0.0.1 → 10.0.0.2");
        }

        [TestMethod]
        public void 改动摘要_切换启用()
        {
            string s = HostsFileHelper.DescribeChanges(
                "127.0.0.1 foo.local\r\n",
                "# 127.0.0.1 foo.local\r\n");
            StringAssert.Contains(s, "修改：127.0.0.1  foo.local");
            StringAssert.Contains(s, "启用 → 禁用");
        }

        [TestMethod]
        public void 改动摘要_无映射变化()
        {
            string t = "127.0.0.1 localhost\r\n";
            Assert.AreEqual("映射条目无增删改。", HostsFileHelper.DescribeChanges(t, t));
        }

        [TestMethod]
        public void DefaultHostsPath_指向drivers_etc_hosts()
        {
            string p = HostsFileHelper.DefaultHostsPath();
            Assert.IsTrue(p.IndexOf(@"drivers\etc\hosts", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [TestMethod]
        public void 启用勾选相对已保存可标记未保存()
        {
            var e = HostsFileHelper.ParseLine("127.0.0.1 a.local");
            Assert.IsTrue(e.Enabled);
            Assert.IsFalse(e.UnsavedEnabled);
            e.Enabled = false;
            Assert.IsTrue(e.UnsavedEnabled);
            e.Enabled = true;
            Assert.IsFalse(e.UnsavedEnabled);
            e.Enabled = false;
            e.CaptureSavedEnabled();
            Assert.IsFalse(e.UnsavedEnabled);
        }

        private static System.Collections.Generic.List<HostsEntry> Mappings(System.Collections.Generic.List<HostsEntry> lines)
        {
            var maps = new System.Collections.Generic.List<HostsEntry>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].IsMapping) maps.Add(lines[i]);
            }
            return maps;
        }
    }
}
