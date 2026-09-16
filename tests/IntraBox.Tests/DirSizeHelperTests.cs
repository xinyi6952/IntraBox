using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class DirSizeHelperTests
    {
        [TestMethod]
        public void FormatSize_字节到TB()
        {
            Assert.AreEqual("0 B", DirSizeHelper.FormatSize(0));
            Assert.AreEqual("512 B", DirSizeHelper.FormatSize(512));
            Assert.AreEqual("1.0 KB", DirSizeHelper.FormatSize(1024));
            Assert.AreEqual("1.0 MB", DirSizeHelper.FormatSize(1024 * 1024));
            Assert.AreEqual("1.00 GB", DirSizeHelper.FormatSize(1024L * 1024 * 1024));
        }

        [TestMethod]
        public void Percent_边界()
        {
            Assert.AreEqual("0%", DirSizeHelper.Percent(0, 100));
            Assert.AreEqual("0%", DirSizeHelper.Percent(10, 0));
            Assert.AreEqual("50.0%", DirSizeHelper.Percent(50, 100));
            Assert.AreEqual("<0.1%", DirSizeHelper.Percent(1, 10000));
        }

        [TestMethod]
        public void ParentPath_盘符根为null()
        {
            Assert.IsNull(DirSizeHelper.ParentPath(@"C:\"));
            string tmp = Path.GetTempPath();
            Assert.IsFalse(string.IsNullOrEmpty(DirSizeHelper.ParentPath(tmp)));
        }

        [TestMethod]
        public void ScanLevel_临时目录含文件与子目录()
        {
            string root = Path.Combine(Path.GetTempPath(), "intrabox-dirsize-" + Guid.NewGuid().ToString("N"));
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            try
            {
                File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
                File.WriteAllBytes(Path.Combine(sub, "b.bin"), new byte[40]);
                var result = DirSizeHelper.ScanLevel(root, CancellationToken.None);
                Assert.AreEqual(140, result.Bytes);
                Assert.AreEqual(2, result.FileCount);
                Assert.AreEqual(2, result.Children.Count);
                Assert.AreEqual("a.bin", result.Children[0].Name);
                Assert.IsFalse(result.Children[0].IsDirectory);
                Assert.AreEqual(100, result.Children[0].Bytes);
                Assert.AreEqual("sub", result.Children[1].Name);
                Assert.IsTrue(result.Children[1].IsDirectory);
                Assert.AreEqual(40, result.Children[1].Bytes);
                StringAssert.Contains(DirSizeHelper.Summarize(result), "不删除");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [TestMethod]
        public void ScanLevel_不存在_抛出()
        {
            try
            {
                DirSizeHelper.ScanLevel(Path.Combine(Path.GetTempPath(), "intrabox-no-such-" + Guid.NewGuid().ToString("N")), CancellationToken.None);
                Assert.Fail("应抛出");
            }
            catch (DirectoryNotFoundException) { }
        }
    }
}
