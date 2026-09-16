using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class OcrBusyHelperTests
    {
        [TestInitialize]
        public void Reset()
        {
            OcrBusyHelper.End();
        }

        [TestCleanup]
        public void Cleanup()
        {
            OcrBusyHelper.End();
        }

        [TestMethod]
        public void TryBegin_第二次失败_End后可再进()
        {
            Assert.IsTrue(OcrBusyHelper.TryBegin());
            Assert.IsTrue(OcrBusyHelper.IsBusy);
            Assert.IsFalse(OcrBusyHelper.TryBegin());
            OcrBusyHelper.End();
            Assert.IsFalse(OcrBusyHelper.IsBusy);
            Assert.IsTrue(OcrBusyHelper.TryBegin());
        }

        [TestMethod]
        public void End_未Begin无效果()
        {
            Assert.IsFalse(OcrBusyHelper.IsBusy);
            OcrBusyHelper.End();
            Assert.IsFalse(OcrBusyHelper.IsBusy);
            Assert.IsTrue(OcrBusyHelper.TryBegin());
        }

        [TestMethod]
        public void BusyChanged_Begin与End各一次()
        {
            int n = 0;
            EventHandler h = (s, e) => { n++; };
            OcrBusyHelper.BusyChanged += h;
            try
            {
                Assert.IsTrue(OcrBusyHelper.TryBegin());
                Assert.IsFalse(OcrBusyHelper.TryBegin());
                OcrBusyHelper.End();
                OcrBusyHelper.End();
                Assert.AreEqual(2, n);
            }
            finally
            {
                OcrBusyHelper.BusyChanged -= h;
            }
        }
    }
}
