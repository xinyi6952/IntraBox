using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox.Tests
{
    [TestClass]
    public class MemoryTrimPolicyTests
    {
        [TestMethod]
        public void IsHeavyModule_名单内为重_轻工具为否()
        {
            Assert.IsTrue(MemoryTrimPolicy.IsHeavyModule("formatter"));
            Assert.IsTrue(MemoryTrimPolicy.IsHeavyModule("diff"));
            Assert.IsTrue(MemoryTrimPolicy.IsHeavyModule("mdpreview"));
            Assert.IsTrue(MemoryTrimPolicy.IsHeavyModule("notes"));
            Assert.IsFalse(MemoryTrimPolicy.IsHeavyModule("timestamp"));
            Assert.IsFalse(MemoryTrimPolicy.IsHeavyModule("namecase"));
            Assert.IsFalse(MemoryTrimPolicy.IsHeavyModule(""));
            Assert.IsFalse(MemoryTrimPolicy.IsHeavyModule(null));
        }

        [TestMethod]
        public void ClampIdleSec_零回落默认_夹取30至600()
        {
            Assert.AreEqual(MemoryTrimPolicy.DefaultIdleSec, MemoryTrimPolicy.ClampIdleSec(0));
            Assert.AreEqual(MemoryTrimPolicy.DefaultIdleSec, MemoryTrimPolicy.ClampIdleSec(-1));
            Assert.AreEqual(30, MemoryTrimPolicy.ClampIdleSec(10));
            Assert.AreEqual(90, MemoryTrimPolicy.ClampIdleSec(90));
            Assert.AreEqual(600, MemoryTrimPolicy.ClampIdleSec(600));
            Assert.AreEqual(600, MemoryTrimPolicy.ClampIdleSec(9999));
        }

        [TestMethod]
        public void ClampHighLowMb_滞回且低水位低于高水位()
        {
            Assert.AreEqual(MemoryTrimPolicy.DefaultHighMb, MemoryTrimPolicy.ClampHighMb(0));
            Assert.AreEqual(80, MemoryTrimPolicy.ClampHighMb(50));
            Assert.AreEqual(2048, MemoryTrimPolicy.ClampHighMb(99999));
            Assert.AreEqual(MemoryTrimPolicy.MaxHighMb - 20, MemoryTrimPolicy.MaxLowMb);
            Assert.AreEqual(MemoryTrimPolicy.DefaultLowMb, MemoryTrimPolicy.ClampLowMb(0, 220));
            Assert.AreEqual(200, MemoryTrimPolicy.ClampLowMb(500, 220));
            Assert.AreEqual(50, MemoryTrimPolicy.ClampLowMb(10, 220));
        }

        [TestMethod]
        public void ShouldThresholdTrim_高水位触发_低水位停止()
        {
            Assert.IsFalse(MemoryTrimPolicy.ShouldThresholdTrim(100, 220, 120));
            Assert.IsFalse(MemoryTrimPolicy.ShouldThresholdTrim(120, 220, 120));
            Assert.IsTrue(MemoryTrimPolicy.ShouldThresholdTrim(220, 220, 120));
            Assert.IsTrue(MemoryTrimPolicy.ShouldThresholdTrim(400, 220, 120));
        }

        [TestMethod]
        public void ShouldIdleTrim_满空闲且过冷却()
        {
            Assert.IsFalse(MemoryTrimPolicy.ShouldIdleTrim(30, 90, 120, 60));
            Assert.IsFalse(MemoryTrimPolicy.ShouldIdleTrim(90, 90, 10, 60));
            Assert.IsTrue(MemoryTrimPolicy.ShouldIdleTrim(90, 90, 60, 60));
            Assert.IsTrue(MemoryTrimPolicy.ShouldIdleTrim(180, 90, 120, 60));
        }

        [TestMethod]
        public void CanAutoAct_仅停泊且不忙()
        {
            Assert.IsTrue(MemoryTrimPolicy.CanAutoAct(true, false, false, false));
            Assert.IsFalse(MemoryTrimPolicy.CanAutoAct(false, false, false, false));
            Assert.IsFalse(MemoryTrimPolicy.CanAutoAct(true, true, false, false));
            Assert.IsFalse(MemoryTrimPolicy.CanAutoAct(true, false, true, false));
            Assert.IsFalse(MemoryTrimPolicy.CanAutoAct(true, false, false, true));
        }

        [TestMethod]
        public void ShouldCaptureImage_停泊且跳过则不记图()
        {
            Assert.IsFalse(ClipboardStore.ShouldCaptureImage(false, false, true));
            Assert.IsTrue(ClipboardStore.ShouldCaptureImage(true, false, true));
            Assert.IsFalse(ClipboardStore.ShouldCaptureImage(true, true, true));
            Assert.IsTrue(ClipboardStore.ShouldCaptureImage(true, true, false));
        }
    }
}
