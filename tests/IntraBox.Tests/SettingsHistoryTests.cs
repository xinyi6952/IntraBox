using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox.Tests
{
    [TestClass]
    public class SettingsHistoryTests
    {
        [TestMethod]
        public void ClampClipboardMax_夹取到1至200()
        {
            Assert.AreEqual(1, AppSettings.ClampClipboardMax(0));
            Assert.AreEqual(1, AppSettings.ClampClipboardMax(-5));
            Assert.AreEqual(200, AppSettings.ClampClipboardMax(201));
            Assert.AreEqual(200, AppSettings.ClampClipboardMax(999));
            Assert.AreEqual(1, AppSettings.ClampClipboardMax(1));
            Assert.AreEqual(200, AppSettings.ClampClipboardMax(200));
            Assert.AreEqual(20, AppSettings.ClampClipboardMax(20));
        }

        [TestMethod]
        public void HistoryGetInt_兼容long与double()
        {
            var map = new Dictionary<string, object>
            {
                { "a", 3L },
                { "b", 4.9 },
                { "c", "12" }
            };
            Assert.AreEqual(3, HistoryManager.GetInt(map, "a", 0));
            Assert.AreEqual(4, HistoryManager.GetInt(map, "b", 0));
            Assert.AreEqual(12, HistoryManager.GetInt(map, "c", 0));
            Assert.AreEqual(7, HistoryManager.GetInt(map, "missing", 7));
        }

        [TestMethod]
        public void HistoryManager_超长字符串不入库()
        {
            var huge = new string('x', HistoryManager.MaxTextChars + 1);
            HistoryManager.Save("memopt-test", new Dictionary<string, object>
            {
                { "small", "ok" },
                { "huge", huge }
            });
            Dictionary<string, object> loaded;
            Assert.IsTrue(HistoryManager.TryLoad("memopt-test", out loaded));
            Assert.AreEqual("ok", HistoryManager.GetString(loaded, "small"));
            Assert.AreEqual("", HistoryManager.GetString(loaded, "huge"));
        }

        [TestMethod]
        public void ClipItem_MakeShortPreview_图片用Preview_文本压成单行()
        {
            Assert.AreEqual("[图片] 1920×1080", ClipItem.MakeShortPreview(true, "ignored", "[图片] 1920×1080"));
            Assert.AreEqual("[图片]", ClipItem.MakeShortPreview(true, null, null));
            Assert.AreEqual("hello world", ClipItem.MakeShortPreview(false, "hello\nworld", "fallback"));
        }
    }
}
