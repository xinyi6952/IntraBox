using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using IntraBox.Core;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace IntraBox.Core
{
    /// <summary>
    /// GDI 冻结截屏。参考 Snapture / AquaShot：先 BitBlt 虚拟桌面，再在覆盖层上框选，避免半透明窗口漏底。
    /// 兼容 Windows 7（不使用 Win10 GraphicsCapture）。
    /// </summary>
    public static class ScreenCapture
    {
        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCxVirtualScreen = 78;
        private const int SmCyVirtualScreen = 79;
        private const int SrcCopy = 0x00CC0020;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
            IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        public static Rectangle VirtualScreenPixels()
        {
            int x = GetSystemMetrics(SmXVirtualScreen);
            int y = GetSystemMetrics(SmYVirtualScreen);
            int w = GetSystemMetrics(SmCxVirtualScreen);
            int h = GetSystemMetrics(SmCyVirtualScreen);
            if (w <= 0 || h <= 0)
                return new Rectangle(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            return new Rectangle(x, y, w, h);
        }

        public static Bitmap CaptureVirtualScreen()
        {
            var r = VirtualScreenPixels();
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdcDest = g.GetHdc();
                IntPtr hdcSrc = GetDC(IntPtr.Zero);
                try
                {
                    BitBlt(hdcDest, 0, 0, r.Width, r.Height, hdcSrc, r.X, r.Y, SrcCopy);
                }
                finally
                {
                    g.ReleaseHdc(hdcDest);
                    ReleaseDC(IntPtr.Zero, hdcSrc);
                }
            }
            return bmp;
        }

        public static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            if (bmp == null) return null;
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        public static Bitmap Crop(Bitmap src, Rectangle rect)
        {
            rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width < 1 || rect.Height < 1)
                rect = new Rectangle(0, 0, Math.Max(1, src.Width), Math.Max(1, src.Height));
            var dest = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.DrawImage(src, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
            }
            return dest;
        }

        public static void Pixelate(Bitmap bmp, Rectangle rect, int block)
        {
            if (bmp == null || block < 2) return;
            rect.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (rect.Width < 1 || rect.Height < 1) return;
            for (int y = rect.Top; y < rect.Bottom; y += block)
            {
                for (int x = rect.Left; x < rect.Right; x += block)
                {
                    int w = Math.Min(block, rect.Right - x);
                    int h = Math.Min(block, rect.Bottom - y);
                    var c = bmp.GetPixel(x + w / 2, y + h / 2);
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++)
                            bmp.SetPixel(x + xx, y + yy, c);
                }
            }
        }
    }
}
