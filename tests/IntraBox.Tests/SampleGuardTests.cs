using IntraBox.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class SampleGuardTests
    {
        [TestMethod]
        public void ShouldRefresh_缺文件_允许刷新()
        {
            Assert.IsTrue(SampleGuard.ShouldRefresh("已改标题", null, "出厂正文"));
        }

        [TestMethod]
        public void ShouldRefresh_标题去掉示例前缀_跳过()
        {
            Assert.IsFalse(SampleGuard.ShouldRefresh("我的联调纪要", "出厂正文", "出厂正文"));
        }

        [TestMethod]
        public void ShouldRefresh_正文已改_跳过()
        {
            Assert.IsFalse(SampleGuard.ShouldRefresh("【示例】联调纪要", "用户自己写的", "出厂正文"));
        }

        [TestMethod]
        public void ShouldRefresh_标题前缀与正文均未改_允许刷新()
        {
            Assert.IsTrue(SampleGuard.ShouldRefresh("【示例】联调纪要", "出厂正文", "出厂正文"));
            Assert.IsTrue(SampleGuard.ShouldRefresh("【示例】联调纪要", "出厂正文\r\n", "出厂正文\n"));
        }

        [TestMethod]
        public void ShouldRefresh_纯文本相同但格式戳不同_跳过()
        {
            Assert.IsFalse(SampleGuard.ShouldRefresh(
                "【示例】联调纪要", "出厂正文", "出厂正文", "P14#Bold", "P14#Normal"));
        }

        [TestMethod]
        public void ShouldRefresh_格式戳相同_允许刷新()
        {
            Assert.IsTrue(SampleGuard.ShouldRefresh(
                "【示例】联调纪要", "出厂正文", "出厂正文", "P14", "P14"));
        }

        [TestMethod]
        public void HasExamplePrefix_仅认书名号前缀()
        {
            Assert.IsTrue(SampleGuard.HasExamplePrefix("【示例】周五提交"));
            Assert.IsFalse(SampleGuard.HasExamplePrefix("示例周五提交"));
            Assert.IsFalse(SampleGuard.HasExamplePrefix(""));
            Assert.IsFalse(SampleGuard.HasExamplePrefix(null));
        }
    }
}
