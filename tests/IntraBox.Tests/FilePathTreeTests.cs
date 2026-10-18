using System.Collections.Generic;
using IntraBox.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class FilePathTreeTests
    {
        [TestMethod]
        public void Build_同一目录的文件挂在同一节点下()
        {
            var leaves = new List<FilePathTree.Leaf>
            {
                new FilePathTree.Leaf { FullPath = @"D:\work\a\1.txt", Detail = "1" },
                new FilePathTree.Leaf { FullPath = @"D:\work\a\2.txt", Detail = "2" },
                new FilePathTree.Leaf { FullPath = @"D:\work\b.txt", Detail = "b" }
            };
            List<FilePathTree.Node> top = FilePathTree.Build(@"D:\work", leaves);
            Assert.AreEqual(2, top.Count);
            Assert.IsFalse(top[0].IsFile);
            Assert.AreEqual("a", top[0].Name);
            Assert.AreEqual(2, top[0].Children.Count);
            Assert.IsTrue(top[0].Children[0].IsFile);
            Assert.AreEqual("1", top[0].Children[0].Detail);
            Assert.IsTrue(top[1].IsFile);
            Assert.AreEqual("b.txt", top[1].Name);
        }

        [TestMethod]
        public void Build_目录排在文件前_重复路径只保留第一条()
        {
            var leaves = new List<FilePathTree.Leaf>
            {
                new FilePathTree.Leaf { FullPath = @"C:\z.txt", Detail = "first" },
                new FilePathTree.Leaf { FullPath = @"C:\a\m.txt" },
                new FilePathTree.Leaf { FullPath = @"C:\z.txt", Detail = "second" }
            };
            List<FilePathTree.Node> top = FilePathTree.Build(@"C:\", leaves);
            Assert.AreEqual(2, top.Count);
            Assert.AreEqual("a", top[0].Name);
            Assert.AreEqual("z.txt", top[1].Name);
            Assert.AreEqual("first", top[1].Detail);
        }

        [TestMethod]
        public void Build_空列表_返回空()
        {
            Assert.AreEqual(0, FilePathTree.Build(@"D:\work", null).Count);
            Assert.AreEqual(0, FilePathTree.Build(@"D:\work", new List<FilePathTree.Leaf>()).Count);
        }

        [TestMethod]
        public void Flatten_默认全部展开_子节点带深度()
        {
            List<FilePathTree.Node> top = Sample();
            List<FilePathTree.FlatRow> rows = FilePathTree.Flatten(top, null);
            Assert.AreEqual(4, rows.Count);
            Assert.AreEqual("d:a", rows[0].Key);
            Assert.AreEqual(0, rows[0].Depth);
            Assert.IsTrue(rows[0].Expanded);
            Assert.AreEqual("1.txt", rows[1].Node.Name);
            Assert.AreEqual(1, rows[1].Depth);
            Assert.AreEqual("size", rows[1].Node.Detail);
            Assert.AreEqual("time", rows[1].Node.Extra);
            Assert.AreEqual("b.txt", rows[3].Node.Name);
        }

        [TestMethod]
        public void Flatten_折叠目录_不列出其子节点()
        {
            List<FilePathTree.Node> top = Sample();
            var collapsed = new HashSet<string>();
            collapsed.Add("d:a");
            List<FilePathTree.FlatRow> rows = FilePathTree.Flatten(top, collapsed);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("a", rows[0].Node.Name);
            Assert.IsFalse(rows[0].Expanded);
            Assert.AreEqual("b.txt", rows[1].Node.Name);
        }

        private static List<FilePathTree.Node> Sample()
        {
            var leaves = new List<FilePathTree.Leaf>
            {
                new FilePathTree.Leaf { FullPath = @"D:\work\a\1.txt", Detail = "size", Extra = "time" },
                new FilePathTree.Leaf { FullPath = @"D:\work\a\2.txt" },
                new FilePathTree.Leaf { FullPath = @"D:\work\b.txt" }
            };
            return FilePathTree.Build(@"D:\work", leaves);
        }
    }
}
