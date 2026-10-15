using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class WebView2PackTests
    {
        [TestMethod]
        public void NormalizeChoice_越界回到再问()
        {
            Assert.AreEqual(WebView2Pack.ChoiceAsk, WebView2Pack.NormalizeChoice(-1));
            Assert.AreEqual(WebView2Pack.ChoiceAsk, WebView2Pack.NormalizeChoice(9));
            Assert.AreEqual(WebView2Pack.ChoiceUse, WebView2Pack.NormalizeChoice(WebView2Pack.ChoiceUse));
            Assert.AreEqual(WebView2Pack.ChoiceSkip, WebView2Pack.NormalizeChoice(WebView2Pack.ChoiceSkip));
        }

        [TestMethod]
        public void FindCab_只认程序目录旁的压缩包()
        {
            string baseDir = TempDir();
            try
            {
                Assert.IsNull(WebView2Pack.FindCab(baseDir));
                string cab = Path.Combine(baseDir, WebView2Pack.CabFileName);
                File.WriteAllText(cab, "cab");
                Assert.AreEqual(Path.GetFullPath(cab), WebView2Pack.FindCab(baseDir));
                Assert.IsNull(WebView2Pack.FindCab(null));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        [TestMethod]
        public void CompareVersion_按段比较()
        {
            Assert.AreEqual(0, WebView2Pack.CompareVersion("140.0.3485.54", "140.0.3485.54"));
            Assert.AreEqual(1, WebView2Pack.CompareVersion("140.0.3485.55", "140.0.3485.54"));
            Assert.AreEqual(-1, WebView2Pack.CompareVersion("139.0.0.0", "140.0.0.0"));
            Assert.AreEqual(0, WebView2Pack.CompareVersion("", null));
        }

        [TestMethod]
        public void ShouldOfferUpgrade_仅在更新且未拒绝时询问()
        {
            Assert.IsFalse(WebView2Pack.ShouldOfferUpgrade("", "140.0.0.2", null));
            Assert.IsFalse(WebView2Pack.ShouldOfferUpgrade("140.0.0.1", "", null));
            Assert.IsFalse(WebView2Pack.ShouldOfferUpgrade("140.0.0.2", "140.0.0.2", null));
            Assert.IsTrue(WebView2Pack.ShouldOfferUpgrade("140.0.0.1", "140.0.0.2", null));
            Assert.IsFalse(WebView2Pack.ShouldOfferUpgrade("140.0.0.1", "140.0.0.2", "140.0.0.2"));
            Assert.IsTrue(WebView2Pack.ShouldOfferUpgrade("140.0.0.1", "140.0.0.3", "140.0.0.2"));
        }

        [TestMethod]
        public void AdoptStampIfMissing_只补标记不改内核文件()
        {
            string baseDir = TempDir();
            try
            {
                string runtime = Path.Combine(baseDir, WinCompatHelper.WebView2FolderName);
                Directory.CreateDirectory(runtime);
                string exe = Path.Combine(runtime, WinCompatHelper.WebView2ExeName);
                File.WriteAllText(exe, "keep");
                File.WriteAllText(Path.Combine(baseDir, WebView2Pack.CabVersionFileName), "140.0.0.9\r\n");

                WebView2Pack.AdoptStampIfMissing(baseDir, WebView2Pack.ReadCabVersion(baseDir));

                Assert.AreEqual("keep", File.ReadAllText(exe));
                Assert.AreEqual("140.0.0.9", WebView2Pack.ReadStamp(baseDir));
                File.WriteAllText(Path.Combine(runtime, WebView2Pack.StampFileName), "140.0.0.1");
                WebView2Pack.AdoptStampIfMissing(baseDir, "140.0.0.9");
                Assert.AreEqual("140.0.0.1", WebView2Pack.ReadStamp(baseDir));
            }
            finally
            {
                TryDelete(baseDir);
            }
        }

        private static string TempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "IntraBoxWv2Pack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
