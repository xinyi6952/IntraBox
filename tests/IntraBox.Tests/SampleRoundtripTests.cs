using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using IntraBox.Core;
using IntraBox.Modules.Notes;
using IntraBox.Modules.Todo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class SampleRoundtripTests
    {
        [TestMethod]
        public void 待办与笔记示例_XAML往返后纯文本与格式戳稳定()
        {
            RunSta(delegate
            {
                AssertRoundtrip(
                    TodoRichText.CreateSampleDocument(),
                    TodoRichText.ToPlain,
                    TodoRichText.CreateSampleDocument);
                AssertRoundtrip(
                    NoteRichText.CreateSampleDocument(),
                    NoteRichText.ToPlain,
                    NoteRichText.CreateSampleDocument);
            });
        }

        [TestMethod]
        public void 笔记示例只改高亮_格式戳不同则不刷新()
        {
            RunSta(delegate
            {
                var stock = FlowDocStamp.Roundtrip(NoteRichText.CreateSampleDocument());
                Assert.IsNotNull(stock);
                string stockPlain = NoteRichText.ToPlain(stock);
                string stockStamp = FlowDocStamp.From(stock);
                HighlightFirstRun(stock);
                string changedPlain = NoteRichText.ToPlain(stock);
                string changedStamp = FlowDocStamp.From(stock);
                Assert.IsTrue(SampleGuard.SamePlain(stockPlain, changedPlain));
                Assert.AreNotEqual(stockStamp, changedStamp);
                Assert.IsFalse(SampleGuard.ShouldRefresh(
                    "【示例】本周联调纪要（普通笔记）",
                    changedPlain, stockPlain, changedStamp, stockStamp));
            });
        }

        private static void AssertRoundtrip(
            FlowDocument original,
            Func<FlowDocument, string> toPlain,
            Func<FlowDocument> recreate)
        {
            var loaded = FlowDocStamp.Roundtrip(original);
            Assert.IsNotNull(loaded);
            Assert.IsTrue(SampleGuard.SamePlain(toPlain(original), toPlain(loaded)));
            var loadedAgain = FlowDocStamp.Roundtrip(recreate());
            Assert.IsNotNull(loadedAgain);
            Assert.AreEqual(FlowDocStamp.From(loaded), FlowDocStamp.From(loadedAgain));
        }

        private static void HighlightFirstRun(FlowDocument doc)
        {
            foreach (var block in doc.Blocks)
            {
                var p = block as Paragraph;
                if (p == null) continue;
                foreach (var inline in p.Inlines)
                {
                    var run = inline as Run;
                    if (run == null || string.IsNullOrEmpty(run.Text)) continue;
                    run.Background = new SolidColorBrush(Color.FromRgb(255, 235, 59));
                    return;
                }
            }
            Assert.Fail("示例文档没有可高亮的 Run");
        }

        private static void RunSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }
            Exception error = null;
            var thread = new Thread(delegate()
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
