using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using IntraBox.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class ColorBlindHelperTests
    {
        [TestMethod]
        public void Map_红绿色盲红_纯红会混入绿通道()
        {
            byte[] t = ColorBlindHelper.Map(255, 0, 0, 0);
            Assert.AreEqual(145, t[0]);
            Assert.AreEqual(142, t[1]);
            Assert.AreEqual(0, t[2]);
        }

        [TestMethod]
        public void Map_全色盲_三通道相等()
        {
            byte[] t = ColorBlindHelper.Map(255, 0, 0, 3);
            Assert.AreEqual(t[0], t[1]);
            Assert.AreEqual(t[1], t[2]);
            Assert.AreEqual(76, t[0]);
        }

        [TestMethod]
        public void TryParseHex_带井号与纯六位()
        {
            byte r, g, b;
            Assert.IsTrue(ColorBlindHelper.TryParseHex("#E74C3C", out r, out g, out b));
            Assert.AreEqual(0xE7, r);
            Assert.AreEqual(0x4C, g);
            Assert.AreEqual(0x3C, b);
            Assert.IsTrue(ColorBlindHelper.TryParseHex("27AE60", out r, out g, out b));
            Assert.AreEqual(0x27, r);
            Assert.AreEqual(0xAE, g);
            Assert.AreEqual(0x60, b);
        }

        [TestMethod]
        public void TryParseHex_非法格式_失败()
        {
            byte r, g, b;
            Assert.IsFalse(ColorBlindHelper.TryParseHex("", out r, out g, out b));
            Assert.IsFalse(ColorBlindHelper.TryParseHex("#FFF", out r, out g, out b));
            Assert.IsFalse(ColorBlindHelper.TryParseHex("red", out r, out g, out b));
        }

        [TestMethod]
        public void ToHex_大写六位()
        {
            Assert.AreEqual("#E74C3C", ColorBlindHelper.ToHex(0xE7, 0x4C, 0x3C));
        }

        [TestMethod]
        public void Map_红绿色盲_红与绿距离变近()
        {
            int before = Dist(0xE7, 0x4C, 0x3C, 0x27, 0xAE, 0x60);
            byte[] r = ColorBlindHelper.Map(0xE7, 0x4C, 0x3C, 0);
            byte[] g = ColorBlindHelper.Map(0x27, 0xAE, 0x60, 0);
            int after = Dist(r[0], r[1], r[2], g[0], g[1], g[2]);
            Assert.IsTrue(after < before);
        }

        [TestMethod]
        public void RelationHint_红绿色盲提到红和绿()
        {
            StringAssert.Contains(ColorBlindHelper.RelationHint(0), "红");
            StringAssert.Contains(ColorBlindHelper.RelationHint(1), "绿");
            StringAssert.Contains(ColorBlindHelper.RelationHint(2), "黄");
            StringAssert.Contains(ColorBlindHelper.RelationHint(3), "灰");
        }

        [TestMethod]
        public void CreateDemoBitmap_红块是红色()
        {
            using (var bmp = ColorBlindHelper.CreateDemoBitmap())
            {
                Assert.AreEqual(ColorBlindHelper.DemoWidth, bmp.Width);
                Assert.AreEqual(ColorBlindHelper.DemoHeight, bmp.Height);
                int x = ColorBlindHelper.SwatchX0 + ColorBlindHelper.SwatchW / 2;
                int y = ColorBlindHelper.SwatchY + ColorBlindHelper.SwatchH / 2;
                var p = bmp.GetPixel(x, y);
                Assert.AreEqual(0xE7, p.R);
                Assert.AreEqual(0x4C, p.G);
                Assert.AreEqual(0x3C, p.B);
            }
        }

        [TestMethod]
        public void Transform_纯红像素_与Map一致且保留Alpha()
        {
            using (var src = new System.Drawing.Bitmap(2, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                src.SetPixel(0, 0, System.Drawing.Color.FromArgb(200, 255, 0, 0));
                src.SetPixel(1, 0, System.Drawing.Color.FromArgb(255, 0, 255, 0));
                using (var dst = ColorBlindHelper.Transform(src, 0))
                {
                    byte[] e0 = ColorBlindHelper.Map(255, 0, 0, 0);
                    byte[] e1 = ColorBlindHelper.Map(0, 255, 0, 0);
                    var p0 = dst.GetPixel(0, 0);
                    var p1 = dst.GetPixel(1, 0);
                    Assert.AreEqual(200, p0.A);
                    Assert.AreEqual(e0[0], p0.R);
                    Assert.AreEqual(e0[1], p0.G);
                    Assert.AreEqual(e0[2], p0.B);
                    Assert.AreEqual(255, p1.A);
                    Assert.AreEqual(e1[0], p1.R);
                    Assert.AreEqual(e1[1], p1.G);
                    Assert.AreEqual(e1[2], p1.B);
                }
            }
        }

        [TestMethod]
        public void Transform_负stride底向上位图_像素与Map一致()
        {
            const int w = 2, h = 2;
            int rowBytes = w * 4;
            byte[] buf = new byte[rowBytes * h];
            WriteBgraBottomUp(buf, rowBytes, h, 0, 0, 200, 255, 0, 0);
            WriteBgraBottomUp(buf, rowBytes, h, 1, 0, 255, 0, 255, 0);
            WriteBgraBottomUp(buf, rowBytes, h, 0, 1, 128, 0, 0, 255);
            WriteBgraBottomUp(buf, rowBytes, h, 1, 1, 255, 255, 255, 255);

            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                IntPtr top = Marshal.UnsafeAddrOfPinnedArrayElement(buf, (h - 1) * rowBytes);
                using (var src = new Bitmap(w, h, -rowBytes, PixelFormat.Format32bppArgb, top))
                using (var dst = ColorBlindHelper.Transform(src, 0))
                {
                    AssertPixelMapped(dst, 0, 0, 200, 255, 0, 0, 0);
                    AssertPixelMapped(dst, 1, 0, 255, 0, 255, 0, 0);
                    AssertPixelMapped(dst, 0, 1, 128, 0, 0, 255, 0);
                    AssertPixelMapped(dst, 1, 1, 255, 255, 255, 255, 0);
                }
            }
            finally
            {
                pin.Free();
            }
        }

        private static void WriteBgraBottomUp(byte[] buf, int rowBytes, int h, int x, int y, byte a, byte r, byte g, byte b)
        {
            int i = (h - 1 - y) * rowBytes + x * 4;
            buf[i] = b;
            buf[i + 1] = g;
            buf[i + 2] = r;
            buf[i + 3] = a;
        }

        private static void AssertPixelMapped(Bitmap dst, int x, int y, byte a, byte r, byte g, byte b, int mode)
        {
            byte[] e = ColorBlindHelper.Map(r, g, b, mode);
            var p = dst.GetPixel(x, y);
            Assert.AreEqual(a, p.A);
            Assert.AreEqual(e[0], p.R);
            Assert.AreEqual(e[1], p.G);
            Assert.AreEqual(e[2], p.B);
        }

        private static int Dist(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return dr * dr + dg * dg + db * db;
        }
    }
}
