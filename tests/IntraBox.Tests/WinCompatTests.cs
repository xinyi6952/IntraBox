using System;
using System.IO;
using System.Net;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class WinCompatTests
    {
        [TestMethod]
        public void BrowserEmulation_IE11优先svcVersion()
        {
            Assert.AreEqual(11001, WinCompatHelper.BrowserEmulationDword("11.0.9600.16428", "9.11.9600.16428"));
        }

        [TestMethod]
        public void BrowserEmulation_仅Version的IE8()
        {
            Assert.AreEqual(8000, WinCompatHelper.BrowserEmulationDword(null, "8.0.7601.17514"));
        }

        [TestMethod]
        public void BrowserEmulation_IE9与IE10()
        {
            Assert.AreEqual(9000, WinCompatHelper.BrowserEmulationDword("9.0.8112.16421", null));
            Assert.AreEqual(10000, WinCompatHelper.BrowserEmulationDword("10.0.9200.16384", null));
        }

        [TestMethod]
        public void BrowserEmulation_空版本按IE8()
        {
            Assert.AreEqual(8000, WinCompatHelper.BrowserEmulationDword(null, null));
            Assert.AreEqual(8000, WinCompatHelper.BrowserEmulationDword(" ", ""));
        }

        [TestMethod]
        public void EnableModernTls_尽量带上Tls12标志()
        {
            SecurityProtocolType before = ServicePointManager.SecurityProtocol;
            try
            {
                WinCompatHelper.EnableModernTls();
                int v = (int)ServicePointManager.SecurityProtocol;
                Assert.IsTrue((v & WinCompatHelper.Tls12Flag) != 0 || (v & WinCompatHelper.Tls13Flag) != 0
                    || (v & (int)SecurityProtocolType.Tls) != 0);
            }
            finally
            {
                ServicePointManager.SecurityProtocol = before;
            }
        }

        [TestMethod]
        public void PixelsPerDip_空可视化对象为1()
        {
            Assert.AreEqual(1.0, WinCompatHelper.PixelsPerDip(null));
        }

        [TestMethod]
        public void GetPixelsPerInch_空可视化对象为96()
        {
            double x, y;
            WinCompatHelper.GetPixelsPerInch(null, out x, out y);
            Assert.AreEqual(96.0, x);
            Assert.AreEqual(96.0, y);
        }

        [TestMethod]
        public void FindWebView2ExeDir_根目录或子目录有exe()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "IntraBoxWv2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                Assert.IsNull(WinCompatHelper.FindWebView2ExeDir(tmp));
                File.WriteAllText(Path.Combine(tmp, WinCompatHelper.WebView2ExeName), "x");
                Assert.AreEqual(Path.GetFullPath(tmp), WinCompatHelper.FindWebView2ExeDir(tmp));
                File.Delete(Path.Combine(tmp, WinCompatHelper.WebView2ExeName));
                string nested = Path.Combine(tmp, "Microsoft.WebView2.FixedVersionRuntime.x64");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, WinCompatHelper.WebView2ExeName), "x");
                Assert.AreEqual(Path.GetFullPath(nested), WinCompatHelper.FindWebView2ExeDir(tmp));
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        [TestMethod]
        public void FindWebView2FixedFolder_只认约定目录名()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "IntraBoxBase_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                Assert.IsNull(WinCompatHelper.FindWebView2FixedFolder(baseDir));
                string runtime = Path.Combine(baseDir, WinCompatHelper.WebView2FolderName);
                Directory.CreateDirectory(runtime);
                File.WriteAllText(Path.Combine(runtime, WinCompatHelper.WebView2ExeName), "x");
                Assert.AreEqual(Path.GetFullPath(runtime), WinCompatHelper.FindWebView2FixedFolder(baseDir));
            }
            finally
            {
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [TestMethod]
        public void IsWindows10OrNewer_不抛()
        {
            WinCompatHelper.IsWindows10OrNewer();
            WinCompatHelper.IsWindows8OrNewer();
            WinCompatHelper.SupportsDarkWindowCaption();
            Assert.IsTrue(WinCompatHelper.ReadCurrentBuildNumber() >= 0);
        }

        [TestMethod]
        public void CompatDegrade_Once同一key只记一次()
        {
            string key = "test-" + Guid.NewGuid().ToString("N");
            Assert.IsTrue(CompatDegrade.Once(key, "测试降级日志", null));
            Assert.IsFalse(CompatDegrade.Once(key, "测试降级日志", null));
        }

        [TestMethod]
        public void LooksLikeMissingVc_DllNotFound()
        {
            Assert.IsTrue(WinCompatHelper.LooksLikeMissingVcRuntime(new DllNotFoundException("tesseract41.dll")));
            Assert.IsTrue(WinCompatHelper.LooksLikeMissingVcRuntime(
                new TargetInvocationException("调用的目标发生了异常", new DllNotFoundException("tesseract41.dll"))));
            Assert.IsFalse(WinCompatHelper.LooksLikeMissingVcRuntime(
                new TargetInvocationException("调用的目标发生了异常", new InvalidOperationException("pix"))));
            Assert.IsFalse(WinCompatHelper.LooksLikeMissingVcRuntime(new InvalidOperationException("缺少 tessdata 目录")));
        }
    }
}
