using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class BusyLeaveHelperTests
    {
        [TestMethod]
        public void ResolveMessage_空则默认()
        {
            Assert.AreEqual(BusyLeaveHelper.DefaultMessage, BusyLeaveHelper.ResolveMessage(null));
            Assert.AreEqual(BusyLeaveHelper.DefaultMessage, BusyLeaveHelper.ResolveMessage(""));
            Assert.AreEqual(BusyLeaveHelper.DefaultMessage, BusyLeaveHelper.ResolveMessage("  "));
        }

        [TestMethod]
        public void ResolveMessage_自定义去空白()
        {
            Assert.AreEqual("目录仍在分析。", BusyLeaveHelper.ResolveMessage("  目录仍在分析。  "));
        }

        [TestMethod]
        public void ConfirmIfBusy_不忙直接放行()
        {
            Assert.IsTrue(BusyLeaveHelper.ConfirmIfBusy(false, "不应弹窗"));
        }
    }
}
