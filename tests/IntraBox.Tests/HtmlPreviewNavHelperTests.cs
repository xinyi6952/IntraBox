using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class HtmlPreviewNavHelperTests
    {
        [TestMethod]
        public void TryGetInPageFragment_aboutBlank与裸井号()
        {
            string id;
            Assert.IsTrue(HtmlPreviewNavHelper.TryGetInPageFragment("about:blank#hello", out id));
            Assert.AreEqual("hello", id);
            Assert.IsTrue(HtmlPreviewNavHelper.TryGetInPageFragment("#13-平台能力", out id));
            Assert.AreEqual("13-平台能力", id);
            Assert.IsTrue(HtmlPreviewNavHelper.TryGetInPageFragment("about:blank#%E6%B5%8B", out id));
            Assert.AreEqual("测", id);
            Assert.IsFalse(HtmlPreviewNavHelper.TryGetInPageFragment("about:blank", out id));
            Assert.IsFalse(HtmlPreviewNavHelper.TryGetInPageFragment("about:blank#", out id));
            Assert.IsFalse(HtmlPreviewNavHelper.TryGetInPageFragment("https://example.com#x", out id));
            Assert.IsFalse(HtmlPreviewNavHelper.TryGetInPageFragment("data:text/html,#fff#x", out id));
        }

        [TestMethod]
        public void ShouldCancelUserBlank_仅用户点空链()
        {
            Assert.IsTrue(HtmlPreviewNavHelper.ShouldCancelUserBlank("about:blank", true));
            Assert.IsFalse(HtmlPreviewNavHelper.ShouldCancelUserBlank("about:blank", false));
            Assert.IsFalse(HtmlPreviewNavHelper.ShouldCancelUserBlank("data:text/html,x", true));
        }

        [TestMethod]
        public void IsEngineLoadUri_首次NavigateToString()
        {
            Assert.IsTrue(HtmlPreviewNavHelper.IsEngineLoadUri("about:blank"));
            Assert.IsTrue(HtmlPreviewNavHelper.IsEngineLoadUri("about:srcdoc"));
            Assert.IsTrue(HtmlPreviewNavHelper.IsEngineLoadUri("data:text/html,hi"));
            Assert.IsTrue(HtmlPreviewNavHelper.IsEngineLoadUri(""));
            Assert.IsFalse(HtmlPreviewNavHelper.IsEngineLoadUri("https://example.com"));
        }

        [TestMethod]
        public void ShouldBlockExternal_http与file()
        {
            Assert.IsTrue(HtmlPreviewNavHelper.ShouldBlockExternal("https://example.com"));
            Assert.IsTrue(HtmlPreviewNavHelper.ShouldBlockExternal("http://a"));
            Assert.IsTrue(HtmlPreviewNavHelper.ShouldBlockExternal("file:///c:/a.md"));
            Assert.IsFalse(HtmlPreviewNavHelper.ShouldBlockExternal("about:blank"));
            Assert.IsTrue(HtmlPreviewNavHelper.IsVirtualFilesUri(
                "https://intrabox.files/a.png", HtmlPreviewHostVirtual));
        }

        [TestMethod]
        public void WithInPageNavScript_插入且幂等()
        {
            string html = "<html><body><h1 id='a'>A</h1></body></html>";
            string once = HtmlPreviewNavHelper.WithInPageNavScript(html);
            Assert.IsTrue(once.IndexOf(HtmlPreviewNavHelper.ScriptMark, StringComparison.Ordinal) >= 0);
            Assert.IsTrue(once.IndexOf("ibxScroll", StringComparison.Ordinal) >= 0);
            int at = once.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            int js = once.IndexOf(HtmlPreviewNavHelper.ScriptMark, StringComparison.Ordinal);
            Assert.IsTrue(js < at);
            string twice = HtmlPreviewNavHelper.WithInPageNavScript(once);
            Assert.AreEqual(once, twice);
        }

        private const string HtmlPreviewHostVirtual = "intrabox.files";
    }
}
