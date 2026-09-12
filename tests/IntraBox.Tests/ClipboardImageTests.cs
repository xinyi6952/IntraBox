using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox.Tests
{
    /// <summary>剪贴板位图：DIB/PNG/Alpha/缩略图/DataObject 写回。WPF 编码在 STA 线程跑。</summary>
    [TestClass]
    public class ClipboardImageTests
    {
        [TestMethod]
        public void FixZeroAlpha_全0_补成不透明()
        {
            var bgra = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
            ClipboardImage.FixZeroAlpha(bgra);
            Assert.AreEqual(255, bgra[3]);
            Assert.AreEqual(255, bgra[7]);
            Assert.AreEqual(10, bgra[0]);
            Assert.AreEqual(40, bgra[4]);
        }

        [TestMethod]
        public void FixZeroAlpha_已有透明_不改()
        {
            var bgra = new byte[] { 10, 20, 30, 0, 40, 50, 60, 128 };
            ClipboardImage.FixZeroAlpha(bgra);
            Assert.AreEqual(0, bgra[3]);
            Assert.AreEqual(128, bgra[7]);
        }

        [TestMethod]
        public void FixZeroAlpha_空输入_不抛()
        {
            ClipboardImage.FixZeroAlpha(null);
            ClipboardImage.FixZeroAlpha(new byte[0]);
            ClipboardImage.FixZeroAlpha(new byte[] { 1, 2, 3 });
        }

        [TestMethod]
        public void DIB32_全0Alpha_解码后RGB保留且不透明()
        {
            var src = new byte[]
            {
                255, 0, 0, 0,
                0, 255, 0, 0,
                0, 0, 255, 0,
                10, 20, 30, 0
            };
            byte[] dib = MakeDib32(src, 2, 2, false);
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDecodeDib(dib, out w, out h, out bgra));
            Assert.AreEqual(2, w);
            Assert.AreEqual(2, h);
            Assert.AreEqual(255, bgra[0]);
            Assert.AreEqual(0, bgra[1]);
            Assert.AreEqual(0, bgra[2]);
            Assert.AreEqual(255, bgra[3]);
            Assert.AreEqual(10, bgra[12]);
            Assert.AreEqual(20, bgra[13]);
            Assert.AreEqual(30, bgra[14]);
            Assert.AreEqual(255, bgra[15]);
        }

        [TestMethod]
        public void DIB32_自顶向下_行序正确()
        {
            var src = new byte[]
            {
                1, 0, 0, 255,
                2, 0, 0, 255,
                3, 0, 0, 255,
                4, 0, 0, 255
            };
            byte[] dib = MakeDib32(src, 2, 2, true);
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDecodeDib(dib, out w, out h, out bgra));
            Assert.AreEqual(2, w);
            Assert.AreEqual(2, h);
            for (int i = 0; i < src.Length; i++)
                Assert.AreEqual(src[i], bgra[i]);
        }

        [TestMethod]
        public void DIB24_往返_RGB一致()
        {
            var src = Opaque2x2();
            byte[] dib = ClipboardImage.EncodeDib24(src, 2, 2);
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDecodeDib(dib, out w, out h, out bgra));
            Assert.AreEqual(2, w);
            Assert.AreEqual(2, h);
            for (int i = 0; i < src.Length; i++)
                Assert.AreEqual(src[i], bgra[i]);
        }

        [TestMethod]
        public void DIB24_宽度1_步长填充仍能解码()
        {
            var src = new byte[] { 10, 20, 30, 255 };
            byte[] dib = ClipboardImage.EncodeDib24(src, 1, 1);
            Assert.AreEqual(44, dib.Length);
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDecodeDib(dib, out w, out h, out bgra));
            Assert.AreEqual(1, w);
            Assert.AreEqual(1, h);
            Assert.AreEqual(10, bgra[0]);
            Assert.AreEqual(20, bgra[1]);
            Assert.AreEqual(30, bgra[2]);
            Assert.AreEqual(255, bgra[3]);
        }

        [TestMethod]
        public void BMP文件头_仍能解码()
        {
            var src = new byte[] { 9, 8, 7, 255, 6, 5, 4, 255, 3, 2, 1, 255, 0, 0, 0, 255 };
            byte[] dib = ClipboardImage.EncodeDib24(src, 2, 2);
            var bmp = new byte[14 + dib.Length];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDecodeDib(bmp, out w, out h, out bgra));
            Assert.AreEqual(2, w);
            Assert.AreEqual(7, bgra[2]);
        }

        [TestMethod]
        public void DIB_不支持的位深或尺寸_失败()
        {
            int w, h;
            byte[] bgra;
            Assert.IsFalse(ClipboardImage.TryDecodeDib(null, out w, out h, out bgra));
            Assert.IsFalse(ClipboardImage.TryDecodeDib(new byte[8], out w, out h, out bgra));

            var bpp16 = new byte[40];
            bpp16[0] = 40;
            WriteInt32(bpp16, 4, 2);
            WriteInt32(bpp16, 8, 2);
            bpp16[12] = 1;
            bpp16[14] = 16;
            Assert.IsFalse(ClipboardImage.TryDecodeDib(bpp16, out w, out h, out bgra));

            var zeroW = new byte[40];
            zeroW[0] = 40;
            WriteInt32(zeroW, 8, 2);
            zeroW[12] = 1;
            zeroW[14] = 24;
            Assert.IsFalse(ClipboardImage.TryDecodeDib(zeroW, out w, out h, out bgra));
        }

        [TestMethod]
        public void DIB_像素不足_失败()
        {
            var header = new byte[40];
            header[0] = 40;
            WriteInt32(header, 4, 8);
            WriteInt32(header, 8, 8);
            header[12] = 1;
            header[14] = 32;
            int w, h;
            byte[] bgra;
            Assert.IsTrue(ClipboardImage.TryDibSize(header, out w, out h));
            Assert.AreEqual(8, w);
            Assert.AreEqual(8, h);
            Assert.IsFalse(ClipboardImage.TryDecodeDib(header, out w, out h, out bgra));
        }

        [TestMethod]
        public void Downsample_2x2到1x1_取中心像素()
        {
            var src = new byte[]
            {
                1, 0, 0, 255,
                2, 0, 0, 255,
                3, 0, 0, 255,
                4, 0, 0, 255
            };
            var dst = ClipboardImage.DownsampleBgra(src, 2, 2, 1, 1);
            Assert.AreEqual(4, dst.Length);
            Assert.AreEqual(4, dst[0]);
        }

        [TestMethod]
        public void CreateThumb_超过最长边_缩小并冻结()
        {
            RunSta(() =>
            {
                var src = new byte[200 * 100 * 4];
                for (int i = 3; i < src.Length; i += 4) src[i] = 255;
                src[0] = 77;
                var thumb = ClipboardImage.CreateThumb(src, 200, 100, 128, 96, 96);
                Assert.IsNotNull(thumb);
                Assert.IsTrue(thumb.IsFrozen);
                Assert.AreEqual(128, thumb.PixelWidth);
                Assert.AreEqual(64, thumb.PixelHeight);
            });
        }

        [TestMethod]
        public void CreateThumb_已小于上限_保持原尺寸()
        {
            RunSta(() =>
            {
                var src = Opaque2x2();
                var thumb = ClipboardImage.CreateThumb(src, 2, 2, 128, 96, 96);
                Assert.AreEqual(2, thumb.PixelWidth);
                Assert.AreEqual(2, thumb.PixelHeight);
            });
        }

        [TestMethod]
        public void CreateThumb_非法参数_返回null()
        {
            RunSta(() =>
            {
                Assert.IsNull(ClipboardImage.CreateThumb(null, 2, 2, 128, 96, 96));
                Assert.IsNull(ClipboardImage.CreateThumb(Opaque2x2(), 2, 2, 0, 96, 96));
                Assert.IsNull(ClipboardImage.ToFrozenBgra32(null, 2, 2, 96, 96));
            });
        }

        [TestMethod]
        public void ScreenDipSize_96DPI_等于像素()
        {
            var s = ClipboardImage.ScreenDipSize(1920, 1080, 96, 96);
            Assert.AreEqual(1920, s.Width, 0.01);
            Assert.AreEqual(1080, s.Height, 0.01);
        }

        [TestMethod]
        public void ScreenDipSize_150百分_按屏幕像素一对一()
        {
            var s = ClipboardImage.ScreenDipSize(1920, 1080, 144, 144);
            Assert.AreEqual(1280, s.Width, 0.01);
            Assert.AreEqual(720, s.Height, 0.01);
        }

        [TestMethod]
        public void ScreenDipSize_DPI无效_回退96()
        {
            var s = ClipboardImage.ScreenDipSize(100, 50, 0, -1);
            Assert.AreEqual(100, s.Width, 0.01);
            Assert.AreEqual(50, s.Height, 0.01);
        }

        [TestMethod]
        public void ScreenDipSize_非法像素_至少为1()
        {
            var s = ClipboardImage.ScreenDipSize(0, -3, 96, 96);
            Assert.AreEqual(1, s.Width, 0.01);
            Assert.AreEqual(1, s.Height, 0.01);
        }

        [TestMethod]
        public void ToFrozenBgra32_DPI无效_回退96()
        {
            RunSta(() =>
            {
                var bmp = ClipboardImage.ToFrozenBgra32(Opaque2x2(), 2, 2, 0, -1);
                Assert.IsTrue(bmp.IsFrozen);
                Assert.AreEqual(96, bmp.DpiX, 0.01);
                Assert.AreEqual(96, bmp.DpiY, 0.01);
            });
        }

        [TestMethod]
        public void CopyBgra32_全0Alpha_补不透明()
        {
            RunSta(() =>
            {
                var raw = new byte[] { 9, 8, 7, 0, 6, 5, 4, 0, 3, 2, 1, 0, 0, 0, 0, 0 };
                var src = BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, raw, 8);
                int w, h;
                byte[] bgra = ClipboardImage.CopyBgra32(src, out w, out h);
                Assert.AreEqual(2, w);
                Assert.AreEqual(9, bgra[0]);
                Assert.AreEqual(255, bgra[3]);
                Assert.AreEqual(255, bgra[7]);
            });
        }

        [TestMethod]
        public void Signature_相同像素_相同指纹()
        {
            var a = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 };
            var b = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 };
            Assert.AreEqual(ClipboardImage.Signature(2, 1, a), ClipboardImage.Signature(2, 1, b));
            b[0] = 9;
            Assert.AreNotEqual(ClipboardImage.Signature(2, 1, a), ClipboardImage.Signature(2, 1, b));
        }

        [TestMethod]
        public void Signature_空或尺寸不同_可区分()
        {
            Assert.IsNull(ClipboardImage.Signature(2, 2, null));
            Assert.IsNull(ClipboardImage.Signature(0, 2, Opaque2x2()));
            var px = Opaque2x2();
            Assert.AreNotEqual(ClipboardImage.Signature(2, 2, px), ClipboardImage.Signature(4, 1, px));
        }

        [TestMethod]
        public void PNG头_读宽高()
        {
            var png = PngHeader(12, 8);
            int w, h;
            Assert.IsTrue(ClipboardImage.TryPngSize(png, out w, out h));
            Assert.AreEqual(12, w);
            Assert.AreEqual(8, h);
        }

        [TestMethod]
        public void PNG头_非法_失败()
        {
            int w, h;
            Assert.IsFalse(ClipboardImage.TryPngSize(null, out w, out h));
            Assert.IsFalse(ClipboardImage.TryPngSize(new byte[10], out w, out h));
            var bad = PngHeader(12, 8);
            bad[1] = 0;
            Assert.IsFalse(ClipboardImage.TryPngSize(bad, out w, out h));
            Assert.IsFalse(ClipboardImage.TryPngSize(PngHeader(0, 8), out w, out h));
        }

        [TestMethod]
        public void HasImage_空或纯文本_为false()
        {
            RunSta(() =>
            {
                Assert.IsFalse(ClipboardImage.HasImage(null));
                var text = new DataObject();
                text.SetData(DataFormats.UnicodeText, "hello");
                Assert.IsFalse(ClipboardImage.HasImage(text));
            });
        }

        [TestMethod]
        public void CreateDataObject_写回含PNG与24位DIB_解码不黑()
        {
            RunSta(() =>
            {
                var src = Opaque2x2();
                var data = ClipboardImage.CreateDataObject(src, 2, 2, 96, 96);
                Assert.IsNotNull(data);
                Assert.IsTrue(ClipboardImage.HasImage(data));
                Assert.IsTrue(data.GetDataPresent("PNG", false));
                Assert.IsTrue(data.GetDataPresent(DataFormats.Dib, false));
                Assert.IsTrue(data.GetDataPresent(DataFormats.Bitmap, true));

                int w, h;
                byte[] bgra;
                Assert.IsTrue(ClipboardImage.TryDecode(data, out w, out h, out bgra));
                Assert.AreEqual(2, w);
                Assert.AreEqual(2, h);
                AssertPixel(src, bgra, 0);
                Assert.AreEqual(255, bgra[3]);

                byte[] dib = AsBytes(data.GetData(DataFormats.Dib, false));
                Assert.IsTrue(ClipboardImage.TryDecodeDib(dib, out w, out h, out bgra));
                Assert.AreEqual(24, BitConverter.ToInt16(dib, 14));
                AssertPixel(src, bgra, 0);
                Assert.AreEqual(255, bgra[3]);
            });
        }

        [TestMethod]
        public void TryDecode_仅DIB_也能还原()
        {
            RunSta(() =>
            {
                var src = Opaque2x2();
                var data = new DataObject();
                data.SetData(DataFormats.Dib, new MemoryStream(ClipboardImage.EncodeDib24(src, 2, 2)), true);
                int w, h;
                byte[] bgra;
                Assert.IsTrue(ClipboardImage.TryDecode(data, out w, out h, out bgra));
                AssertPixel(src, bgra, 0);
                Assert.AreEqual(255, bgra[3]);
            });
        }

        [TestMethod]
        public void TryDecode_仅PNG_也能还原()
        {
            RunSta(() =>
            {
                var src = Opaque2x2();
                var data = new DataObject();
                data.SetData("PNG", ClipboardImage.EncodePng(src, 2, 2), true);
                int w, h;
                byte[] bgra;
                Assert.IsTrue(ClipboardImage.TryDecode(data, out w, out h, out bgra));
                Assert.AreEqual(2, w);
                Assert.AreEqual(255, bgra[0]);
                Assert.AreEqual(255, bgra[3]);
            });
        }

        [TestMethod]
        public void TryDecode_PNG超限_只读头不解码像素()
        {
            RunSta(() =>
            {
                var data = new DataObject();
                data.SetData("PNG", PngHeader(4000, 3000), true);
                int w, h;
                byte[] bgra;
                Assert.IsFalse(ClipboardImage.TryDecode(data, 100, out w, out h, out bgra));
                Assert.AreEqual(4000, w);
                Assert.AreEqual(3000, h);
                Assert.IsNull(bgra);
            });
        }

        [TestMethod]
        public void TryDecode_DIB超限_只读头不解码像素()
        {
            RunSta(() =>
            {
                var header = new byte[40];
                header[0] = 40;
                WriteInt32(header, 4, 2000);
                WriteInt32(header, 8, 2000);
                header[12] = 1;
                header[14] = 32;
                var data = new DataObject();
                data.SetData(DataFormats.Dib, new MemoryStream(header), true);
                int w, h;
                byte[] bgra;
                Assert.IsFalse(ClipboardImage.TryDecode(data, 100, out w, out h, out bgra));
                Assert.AreEqual(2000, w);
                Assert.AreEqual(2000, h);
                Assert.IsNull(bgra);
            });
        }

        [TestMethod]
        public void TryDecode_空对象_失败()
        {
            int w, h;
            byte[] bgra;
            Assert.IsFalse(ClipboardImage.TryDecode(null, out w, out h, out bgra));
            Assert.AreEqual(0, w);
            Assert.IsNull(bgra);
        }

        [TestMethod]
        public void EncodePng_往返_RGB保留()
        {
            RunSta(() =>
            {
                var src = Opaque2x2();
                var png = ClipboardImage.EncodePng(src, 2, 2);
                var data = new DataObject();
                data.SetData("PNG", png, true);
                int w, h;
                byte[] bgra;
                Assert.IsTrue(ClipboardImage.TryDecode(data, out w, out h, out bgra));
                Assert.AreEqual(255, bgra[0]);
                Assert.AreEqual(0, bgra[1]);
                Assert.AreEqual(0, bgra[2]);
                Assert.AreEqual(255, bgra[3]);
            });
        }

        [TestMethod]
        public void TryDecodeOriginal_合法PNG_返回原图像素尺寸()
        {
            RunSta(() =>
            {
                byte[] png = ClipboardImage.EncodePngBytes(Opaque2x2(), 2, 2);
                var item = new ClipItem { IsImage = true, ImagePng = png };
                BitmapSource src;
                string err;
                Assert.IsTrue(ClipboardStore.TryDecodeOriginal(item, out src, out err));
                Assert.IsNull(err);
                Assert.AreEqual(2, src.PixelWidth);
                Assert.AreEqual(2, src.PixelHeight);
            });
        }

        private static void RunSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static byte[] Opaque2x2()
        {
            return new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                10, 20, 30, 255
            };
        }

        private static void AssertPixel(byte[] expected, byte[] actual, int pixelIndex)
        {
            int i = pixelIndex * 4;
            Assert.AreEqual(expected[i], actual[i]);
            Assert.AreEqual(expected[i + 1], actual[i + 1]);
            Assert.AreEqual(expected[i + 2], actual[i + 2]);
        }

        private static byte[] PngHeader(int width, int height)
        {
            var png = new byte[24];
            png[0] = 0x89;
            png[1] = 0x50;
            png[2] = 0x4E;
            png[3] = 0x47;
            WriteInt32Be(png, 16, width);
            WriteInt32Be(png, 20, height);
            return png;
        }

        private static byte[] AsBytes(object raw)
        {
            var bytes = raw as byte[];
            if (bytes != null) return bytes;
            var ms = raw as MemoryStream;
            if (ms != null) return ms.ToArray();
            var stream = raw as Stream;
            if (stream == null) return null;
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static byte[] MakeDib32(byte[] bgra, int width, int height, bool topDown)
        {
            int stride = width * 4;
            var dib = new byte[40 + stride * height];
            dib[0] = 40;
            WriteInt32(dib, 4, width);
            WriteInt32(dib, 8, topDown ? -height : height);
            dib[12] = 1;
            dib[14] = 32;
            for (int y = 0; y < height; y++)
            {
                int src = topDown ? y * stride : (height - 1 - y) * stride;
                Buffer.BlockCopy(bgra, src, dib, 40 + y * stride, stride);
            }
            return dib;
        }

        private static void WriteInt32(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt32Be(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }
    }
}
