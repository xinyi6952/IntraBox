using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class AppVersionTests
    {
        [TestMethod]
        public void FormatDisplay_修订为0时写成三段()
        {
            Assert.AreEqual("1.0.312", AppVersion.FormatDisplay(new Version(1, 0, 0, 312)));
            Assert.AreEqual("1.0.0", AppVersion.FormatDisplay(new Version(1, 0, 0, 0)));
        }

        [TestMethod]
        public void FormatDisplay_修订非0时保留四段()
        {
            Assert.AreEqual("1.1.2.3", AppVersion.FormatDisplay(new Version(1, 1, 2, 3)));
        }

        [TestMethod]
        public void FormatFull_始终四段()
        {
            Assert.AreEqual("1.0.0.312", AppVersion.FormatFull(new Version(1, 0, 0, 312)));
            Assert.AreEqual("1.0.0.0", AppVersion.FormatFull(new Version(1, 0)));
        }

        [TestMethod]
        public void BumpPack_最后一段加一()
        {
            Assert.AreEqual(new Version(1, 0, 0, 1), AppVersion.BumpPack(new Version(1, 0, 0, 0)));
            Assert.AreEqual(new Version(1, 0, 0, 10), AppVersion.BumpPack(new Version(1, 0, 0, 9)));
        }

        [TestMethod]
        public void BumpPack_65535进位()
        {
            Assert.AreEqual(new Version(1, 0, 1, 0), AppVersion.BumpPack(new Version(1, 0, 0, 65535)));
        }

        [TestMethod]
        public void Compare_按数值而不是字符串()
        {
            Assert.IsTrue(AppVersion.Compare(new Version(1, 0, 0, 10), new Version(1, 0, 0, 9)) > 0);
            Assert.IsTrue(AppVersion.Compare(new Version(1, 1, 0, 0), new Version(1, 0, 0, 99)) > 0);
            Assert.AreEqual(0, AppVersion.Compare(new Version(1, 0, 0, 1), new Version(1, 0, 0, 1)));
        }

        [TestMethod]
        public void TryParse_空白失败()
        {
            Version v;
            Assert.IsFalse(AppVersion.TryParse("  ", out v));
            Assert.IsTrue(AppVersion.TryParse("1.0.0.2", out v));
            Assert.AreEqual("1.0.2", AppVersion.FormatDisplay(v));
        }

        [TestMethod]
        public void ProductTitle_含IntraBox与展示号()
        {
            StringAssert.StartsWith(AppVersion.ProductTitle, "IntraBox ");
            Assert.AreEqual(AppVersion.Display, AppVersion.FormatDisplay(AppVersion.Current));
        }
    }
}
