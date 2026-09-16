using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class AppLogTests
    {
        [TestMethod]
        public void LogsDir_在LocalAppData下IntraBox_logs()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string expect = Path.Combine(local, "IntraBox", "logs");
            Assert.AreEqual(expect, AppLog.LogsDir);
        }

        [TestMethod]
        public void PathFor_按天切分文件名()
        {
            var day = new DateTime(2026, 9, 14);
            string path = AppLog.PathFor(day);
            Assert.AreEqual("2026-09-14.log", Path.GetFileName(path));
            Assert.AreEqual(AppLog.LogsDir, Path.GetDirectoryName(path));
        }

        [TestMethod]
        public void Info_写入当天日志文件()
        {
            string marker = "AppLogTests-" + Guid.NewGuid().ToString("N");
            AppLog.Info(marker);
            string path = AppLog.PathFor(DateTime.Today);
            Assert.IsTrue(File.Exists(path));
            string text = File.ReadAllText(path);
            StringAssert.Contains(text, marker);
            StringAssert.Contains(text, "[INFO]");
        }
    }

    [TestClass]
    public class SearchTextHelperNavTests
    {
        [TestMethod]
        public void Matches_模块名模糊()
        {
            Assert.IsTrue(SearchTextHelper.Matches("日历", "工作日历"));
            Assert.IsTrue(SearchTextHelper.Matches("编排", "任务编排"));
            Assert.IsTrue(SearchTextHelper.Score("job", "任务编排", "jobflow") > 0);
            Assert.IsFalse(SearchTextHelper.Matches("xyz不存在", "工作日历"));
        }
    }
}
