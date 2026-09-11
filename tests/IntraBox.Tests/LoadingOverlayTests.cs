using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Controls;

namespace IntraBox.Tests
{
    [TestClass]
    public class LoadingOverlayTests
    {
        [TestMethod]
        public void NextGen_从0递增且负值按0()
        {
            Assert.AreEqual(1, LoadingOverlay.NextGen(0));
            Assert.AreEqual(2, LoadingOverlay.NextGen(1));
            Assert.AreEqual(1, LoadingOverlay.NextGen(-3));
        }

        [TestMethod]
        public void IsCallbackCurrent_表里没有或号不对则作废()
        {
            Assert.IsFalse(LoadingOverlay.IsCallbackCurrent(false, 0, 1));
            Assert.IsFalse(LoadingOverlay.IsCallbackCurrent(true, 3, 1));
            Assert.IsTrue(LoadingOverlay.IsCallbackCurrent(true, 3, 3));
        }

        [TestMethod]
        public void Hide后再Show_世代单调旧回调对不上()
        {
            // Show A → gen=1；Hide 只 Bump → gen=2（不清表）；Show B → gen=3。
            // 若 Hide 把条目删掉，B 又从 1 起算，A 的延迟回调会误当成当前任务。
            int gen = 0;
            int showA = LoadingOverlay.NextGen(gen);
            gen = showA;
            int afterHide = LoadingOverlay.NextGen(gen);
            gen = afterHide;
            int showB = LoadingOverlay.NextGen(gen);
            Assert.AreEqual(1, showA);
            Assert.AreEqual(2, afterHide);
            Assert.AreEqual(3, showB);
            Assert.IsFalse(LoadingOverlay.IsCallbackCurrent(true, showB, showA));
            Assert.IsTrue(LoadingOverlay.IsCallbackCurrent(true, showB, showB));
        }
    }
}
