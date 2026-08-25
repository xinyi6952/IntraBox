using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class KeepAwakeTests
    {
        [TestMethod]
        public void TryParseDuration_分钟()
        {
            TimeSpan span;
            string err;
            Assert.IsTrue(KeepAwakeService.TryParseDuration("10", "分钟", out span, out err));
            Assert.AreEqual(10, span.TotalMinutes);
        }

        [TestMethod]
        public void TryParseDuration_小时()
        {
            TimeSpan span;
            string err;
            Assert.IsTrue(KeepAwakeService.TryParseDuration("1", "小时", out span, out err));
            Assert.AreEqual(60, span.TotalMinutes);
        }

        [TestMethod]
        public void TryParseDuration_非法()
        {
            TimeSpan span;
            string err;
            Assert.IsFalse(KeepAwakeService.TryParseDuration("0", "分钟", out span, out err));
            Assert.IsFalse(KeepAwakeService.TryParseDuration("abc", "分钟", out span, out err));
            Assert.IsFalse(KeepAwakeService.TryParseDuration("", "小时", out span, out err));
        }
    }
}
