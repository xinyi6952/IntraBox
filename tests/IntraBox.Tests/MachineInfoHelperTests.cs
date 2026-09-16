using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class MachineInfoHelperTests
    {
        [TestMethod]
        public void ShouldCopyFamily_按开关过滤()
        {
            Assert.IsTrue(MachineInfoHelper.ShouldCopyFamily(AddressFamily.InterNetwork, true, false));
            Assert.IsFalse(MachineInfoHelper.ShouldCopyFamily(AddressFamily.InterNetwork, false, true));
            Assert.IsTrue(MachineInfoHelper.ShouldCopyFamily(AddressFamily.InterNetworkV6, false, true));
            Assert.IsFalse(MachineInfoHelper.ShouldCopyFamily(AddressFamily.InterNetworkV6, true, false));
            Assert.IsFalse(MachineInfoHelper.ShouldCopyFamily(AddressFamily.Unspecified, true, true));
        }

        [TestMethod]
        public void JoinLines_空或空白_返回空()
        {
            Assert.AreEqual("", MachineInfoHelper.JoinLines(null));
            Assert.AreEqual("", MachineInfoHelper.JoinLines(new string[0]));
            Assert.AreEqual("", MachineInfoHelper.JoinLines(new[] { "", null }));
        }

        [TestMethod]
        public void JoinLines_多地址_一行一个()
        {
            string text = MachineInfoHelper.JoinLines(new[] { "192.168.1.2", "", "10.0.0.1" });
            Assert.AreEqual("192.168.1.2" + System.Environment.NewLine + "10.0.0.1", text);
        }

        [TestMethod]
        public void CollectUnicastIps_开关关闭_返回空()
        {
            Assert.AreEqual("", MachineInfoHelper.CollectUnicastIps(false, false));
        }

        [TestMethod]
        public void IsActiveCopyNic_null_返回假()
        {
            Assert.IsFalse(MachineInfoHelper.IsActiveCopyNic(null));
        }
    }
}
