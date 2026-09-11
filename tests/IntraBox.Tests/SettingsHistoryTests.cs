using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
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
        public void ClampClipboardMaxImages_缺省回落20_夹取到1至50()
        {
            Assert.AreEqual(20, AppSettings.ClampClipboardMaxImages(0));
            Assert.AreEqual(20, AppSettings.ClampClipboardMaxImages(-3));
            Assert.AreEqual(1, AppSettings.ClampClipboardMaxImages(1));
            Assert.AreEqual(20, AppSettings.ClampClipboardMaxImages(20));
            Assert.AreEqual(50, AppSettings.ClampClipboardMaxImages(50));
            Assert.AreEqual(50, AppSettings.ClampClipboardMaxImages(51));
            Assert.AreEqual(50, AppSettings.ClampClipboardMaxImages(200));
        }

        [TestMethod]
        public void EffectiveMaxImageItems_不超过总条数()
        {
            Assert.AreEqual(20, ClipboardStore.EffectiveMaxImageItems(200, 20));
            Assert.AreEqual(10, ClipboardStore.EffectiveMaxImageItems(10, 50));
            Assert.AreEqual(1, ClipboardStore.EffectiveMaxImageItems(1, 20));
        }

        [TestMethod]
        public void ClampHistoryPersistDelayMs_夹取到300至1000()
        {
            Assert.AreEqual(300, AppSettings.ClampHistoryPersistDelayMs(0));
            Assert.AreEqual(300, AppSettings.ClampHistoryPersistDelayMs(299));
            Assert.AreEqual(300, AppSettings.ClampHistoryPersistDelayMs(300));
            Assert.AreEqual(500, AppSettings.ClampHistoryPersistDelayMs(500));
            Assert.AreEqual(1000, AppSettings.ClampHistoryPersistDelayMs(1000));
            Assert.AreEqual(1000, AppSettings.ClampHistoryPersistDelayMs(1001));
            Assert.AreEqual(1000, AppSettings.ClampHistoryPersistDelayMs(9999));
        }

        [TestMethod]
        public void ApplyMemorySettings_版本1缺bool补true并夹取默认水位()
        {
            var s = new AppSettings();
            s.ConfigVersion = 1;
            s.TrayIdleUnloadModule = false;
            s.ClipboardSkipImagesWhenHidden = false;
            s.RestoreLastModuleOnStartup = false;
            s.TrayIdleReleaseSec = 0;
            s.MemoryTrimHighMb = 0;
            s.MemoryTrimLowMb = 0;
            Assert.IsTrue(ConfigManager.ApplyMemorySettings(s));
            Assert.AreEqual(2, s.ConfigVersion);
            Assert.IsTrue(s.TrayIdleUnloadModule);
            Assert.IsTrue(s.ClipboardSkipImagesWhenHidden);
            Assert.IsTrue(s.RestoreLastModuleOnStartup);
            Assert.AreEqual(MemoryTrimPolicy.DefaultIdleSec, s.TrayIdleReleaseSec);
            Assert.AreEqual(MemoryTrimPolicy.DefaultHighMb, s.MemoryTrimHighMb);
            Assert.AreEqual(MemoryTrimPolicy.DefaultLowMb, s.MemoryTrimLowMb);
        }

        [TestMethod]
        public void ApplyMemorySettings_版本2不把用户关闭改回true()
        {
            var s = new AppSettings();
            s.ConfigVersion = 2;
            s.TrayIdleUnloadModule = false;
            s.ClipboardSkipImagesWhenHidden = false;
            s.RestoreLastModuleOnStartup = false;
            s.TrayIdleReleaseSec = 90;
            s.MemoryTrimHighMb = 220;
            s.MemoryTrimLowMb = 120;
            Assert.IsFalse(ConfigManager.ApplyMemorySettings(s));
            Assert.AreEqual(2, s.ConfigVersion);
            Assert.IsFalse(s.TrayIdleUnloadModule);
            Assert.IsFalse(s.ClipboardSkipImagesWhenHidden);
            Assert.IsFalse(s.RestoreLastModuleOnStartup);
        }

        [TestMethod]
        public void ApplyMemorySettings_越界水位被夹取()
        {
            var s = new AppSettings();
            s.ConfigVersion = 2;
            s.TrayIdleUnloadModule = true;
            s.ClipboardSkipImagesWhenHidden = true;
            s.RestoreLastModuleOnStartup = true;
            s.TrayIdleReleaseSec = 10;
            s.MemoryTrimHighMb = 99999;
            s.MemoryTrimLowMb = 1;
            Assert.IsTrue(ConfigManager.ApplyMemorySettings(s));
            Assert.AreEqual(MemoryTrimPolicy.MinIdleSec, s.TrayIdleReleaseSec);
            Assert.AreEqual(MemoryTrimPolicy.MaxHighMb, s.MemoryTrimHighMb);
            Assert.AreEqual(MemoryTrimPolicy.MinLowMb, s.MemoryTrimLowMb);
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
        public void ShouldSkipDuplicateImage_仅相同指纹跳过()
        {
            Assert.IsTrue(ClipboardStore.ShouldSkipDuplicateImage("same", "same"));
            Assert.IsFalse(ClipboardStore.ShouldSkipDuplicateImage("a", "b"));
            Assert.IsFalse(ClipboardStore.ShouldSkipDuplicateImage(null, "b"));
            Assert.IsFalse(ClipboardStore.ShouldSkipDuplicateImage(null, null));
        }

        [TestMethod]
        public void ClipboardRecordImages_默认关闭()
        {
            Assert.IsFalse(new AppSettings().ClipboardRecordImages);
        }

        [TestMethod]
        public void TodoAutoSave_默认关闭()
        {
            Assert.IsFalse(new AppSettings().TodoAutoSave);
        }

        [TestMethod]
        public void ShouldCaptureImage_跟随记录开关()
        {
            Assert.IsFalse(ClipboardStore.ShouldCaptureImage(false));
            Assert.IsTrue(ClipboardStore.ShouldCaptureImage(true));
        }

        [TestMethod]
        public void ClipItem_MakeShortPreview_图片用Preview_文本压成单行()
        {
            Assert.AreEqual("[图片] 1920×1080", ClipItem.MakeShortPreview(true, "ignored", "[图片] 1920×1080"));
            Assert.AreEqual("[图片]", ClipItem.MakeShortPreview(true, null, null));
            Assert.AreEqual("hello world", ClipItem.MakeShortPreview(false, "hello\nworld", "fallback"));
            Assert.IsNull(ClipItem.TruncateOneLine(null, 10));
            Assert.AreEqual("", ClipItem.TruncateOneLine("", 10));
            Assert.AreEqual("a b", ClipItem.TruncateOneLine("  a  \r\n  b  ", 10));
            Assert.AreEqual("hello", ClipItem.TruncateOneLine("hello", 5));
            Assert.AreEqual("hello…", ClipItem.TruncateOneLine("hello!", 5));
        }

        [TestMethod]
        public void ClipboardStore_Add空项_拒绝()
        {
            Assert.IsFalse(ClipboardStore.Add(null));
        }

        [TestMethod]
        public void ClipboardStore_空白文本不入库_重复以最后一次为准()
        {
            Assert.IsTrue(ClipboardStore.IsBlankText(null));
            Assert.IsTrue(ClipboardStore.IsBlankText(""));
            Assert.IsTrue(ClipboardStore.IsBlankText("  \t\r\n  "));
            Assert.IsFalse(ClipboardStore.IsBlankText("a"));

            ClipboardStore.Items.Clear();
            try
            {
                Assert.IsFalse(ClipboardStore.Add(new ClipItem { Text = "   " }));
                Assert.AreEqual(0, ClipboardStore.Items.Count);

                var first = new ClipItem { Text = "hello", Time = new DateTime(2026, 9, 1, 10, 0, 0) };
                Assert.IsTrue(ClipboardStore.Add(first));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { Text = "other", Time = new DateTime(2026, 9, 1, 11, 0, 0) }));
                var again = new ClipItem { Text = "hello", Time = new DateTime(2026, 9, 1, 12, 0, 0) };
                Assert.IsTrue(ClipboardStore.Add(again));
                Assert.AreEqual(2, ClipboardStore.Items.Count);
                Assert.AreEqual("hello", ClipboardStore.Items[0].Text);
                Assert.AreEqual(new DateTime(2026, 9, 1, 12, 0, 0), ClipboardStore.Items[0].Time);
                Assert.AreEqual("other", ClipboardStore.Items[1].Text);

                var pinned = new ClipItem { Text = "hello", Time = new DateTime(2026, 9, 1, 13, 0, 0) };
                ClipboardStore.Items[0].IsPinned = true;
                Assert.IsTrue(ClipboardStore.Add(pinned));
                Assert.AreEqual(2, ClipboardStore.Items.Count);
                Assert.AreEqual("hello", ClipboardStore.Items[0].Text);
                Assert.IsTrue(ClipboardStore.Items[0].IsPinned);
                Assert.AreEqual(new DateTime(2026, 9, 1, 13, 0, 0), ClipboardStore.Items[0].Time);
            }
            finally
            {
                ClipboardStore.Items.Clear();
            }
        }

        [TestMethod]
        public void ClipboardStore_重复图片_以最后一次为准()
        {
            ClipboardStore.Items.Clear();
            try
            {
                var oldImg = new ClipItem
                {
                    IsImage = true,
                    ImageSig = "sig-a",
                    Preview = "[图片] 1×1",
                    Time = new DateTime(2026, 9, 1, 10, 0, 0)
                };
                Assert.IsTrue(ClipboardStore.Add(oldImg));
                var neu = new ClipItem
                {
                    IsImage = true,
                    ImageSig = "sig-a",
                    Preview = "[图片] 1×1",
                    Time = new DateTime(2026, 9, 1, 12, 0, 0)
                };
                Assert.IsTrue(ClipboardStore.Add(neu));
                Assert.AreEqual(1, ClipboardStore.Items.Count);
                Assert.AreEqual(new DateTime(2026, 9, 1, 12, 0, 0), ClipboardStore.Items[0].Time);
            }
            finally
            {
                ClipboardStore.Items.Clear();
            }
        }

        [TestMethod]
        public void ClipboardStore_图片条数上限_挤掉最旧未固定图_固定图保留()
        {
            var s = ConfigManager.Instance.Settings;
            int oldMax = s.ClipboardMaxItems;
            int oldImg = s.ClipboardMaxImageItems;
            ClipboardStore.Items.Clear();
            try
            {
                s.ClipboardMaxItems = 200;
                s.ClipboardMaxImageItems = 2;

                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "a", Preview = "a" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "b", Preview = "b" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { Text = "keep-text" }));
                Assert.AreEqual(2, ClipboardStore.CountImages());
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "c", Preview = "c" }));
                Assert.AreEqual(2, ClipboardStore.CountImages());
                Assert.AreEqual("c", ClipboardStore.Items[0].ImageSig);
                Assert.IsFalse(HasImageSig("a"));
                Assert.IsTrue(HasImageSig("b"));
                Assert.AreEqual(1, CountText("keep-text"));

                ClipboardStore.Items.Clear();
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "pin", Preview = "pin" }));
                ClipboardStore.Items[0].IsPinned = true;
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "x", Preview = "x" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "y", Preview = "y" }));
                Assert.AreEqual(2, ClipboardStore.CountImages());
                Assert.IsTrue(HasImageSig("pin"));
                Assert.IsTrue(HasImageSig("y"));
                Assert.IsFalse(HasImageSig("x"));
            }
            finally
            {
                s.ClipboardMaxItems = oldMax;
                s.ClipboardMaxImageItems = oldImg;
                ClipboardStore.Items.Clear();
            }
        }

        [TestMethod]
        public void ClipboardStore_TrimToLimit_先裁图片再裁总条数()
        {
            var s = ConfigManager.Instance.Settings;
            int oldMax = s.ClipboardMaxItems;
            int oldImg = s.ClipboardMaxImageItems;
            ClipboardStore.Items.Clear();
            try
            {
                s.ClipboardMaxItems = 200;
                s.ClipboardMaxImageItems = 50;
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "i1", Preview = "i1" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "i2", Preview = "i2" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { IsImage = true, ImageSig = "i3", Preview = "i3" }));
                Assert.IsTrue(ClipboardStore.Add(new ClipItem { Text = "t1" }));
                s.ClipboardMaxImageItems = 1;
                ClipboardStore.TrimToLimit();
                Assert.AreEqual(1, ClipboardStore.CountImages());
                Assert.AreEqual(1, CountText("t1"));
            }
            finally
            {
                s.ClipboardMaxItems = oldMax;
                s.ClipboardMaxImageItems = oldImg;
                ClipboardStore.Items.Clear();
            }
        }

        private static bool HasImageSig(string sig)
        {
            for (int i = 0; i < ClipboardStore.Items.Count; i++)
            {
                if (ClipboardStore.Items[i].IsImage && ClipboardStore.Items[i].ImageSig == sig)
                    return true;
            }
            return false;
        }

        private static int CountText(string text)
        {
            int n = 0;
            for (int i = 0; i < ClipboardStore.Items.Count; i++)
            {
                if (!ClipboardStore.Items[i].IsImage && ClipboardStore.Items[i].Text == text) n++;
            }
            return n;
        }

        [TestMethod]
        public void TryDecodeOriginal_无PNG或损坏_失败且不返回图()
        {
            BitmapSource src;
            string err;
            Assert.IsFalse(ClipboardStore.TryDecodeOriginal(null, out src, out err));
            Assert.IsNull(src);
            StringAssert.Contains(err, "缩略图");

            var noPng = new ClipItem { IsImage = true, ImagePng = null, Preview = "x" };
            Assert.IsFalse(ClipboardStore.TryDecodeOriginal(noPng, out src, out err));
            Assert.IsNull(src);
            StringAssert.Contains(err, "缩略图");

            var empty = new ClipItem { IsImage = true, ImagePng = new byte[0] };
            Assert.IsFalse(ClipboardStore.TryDecodeOriginal(empty, out src, out err));
            Assert.IsNull(src);

            var bad = new ClipItem { IsImage = true, ImagePng = new byte[] { 1, 2, 3, 4 } };
            Assert.IsFalse(ClipboardStore.TryDecodeOriginal(bad, out src, out err));
            Assert.IsNull(src);
            StringAssert.Contains(err, "缩略图");

            var text = new ClipItem { IsImage = false, Text = "hi" };
            Assert.IsFalse(ClipboardStore.TryDecodeOriginal(text, out src, out err));
            Assert.IsNull(src);
        }
    }
}
