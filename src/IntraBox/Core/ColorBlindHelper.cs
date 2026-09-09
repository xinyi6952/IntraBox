using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IntraBox.Core
{
    /// <summary>
    /// 色盲视角颜色矩阵（Brettel / Viénot 近似），供界面与测试共用。
    /// mode：0 红色盲 1 绿色盲 2 蓝色盲 3 全色盲。
    /// </summary>
    public static class ColorBlindHelper
    {
        public const int DemoWidth = 640;
        public const int DemoHeight = 300;
        public const int SwatchX0 = 16;
        public const int SwatchY = 48;
        public const int SwatchW = 88;
        public const int SwatchH = 52;
        public const int SwatchGap = 10;

        public static readonly string[] SampleHex =
        {
            "#E74C3C", "#27AE60", "#F1C40F", "#3498DB", "#E67E22", "#9B59B6"
        };

        public static readonly string[] SampleNames =
        {
            "红", "绿", "黄", "蓝", "橙", "紫"
        };

        public static byte[] Map(byte r, byte g, byte b, int mode)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double nR, nG, nB;
            if (mode == 0)
            {
                nR = 0.567 * R + 0.433 * G;
                nG = 0.558 * R + 0.442 * G;
                nB = 0.242 * G + 0.758 * B;
            }
            else if (mode == 1)
            {
                nR = 0.625 * R + 0.375 * G;
                nG = 0.700 * R + 0.300 * G;
                nB = 0.300 * G + 0.700 * B;
            }
            else if (mode == 2)
            {
                nR = 0.950 * R + 0.050 * G;
                nG = 0.433 * G + 0.567 * B;
                nB = 0.475 * G + 0.525 * B;
            }
            else
            {
                double y = 0.299 * R + 0.587 * G + 0.114 * B;
                nR = nG = nB = y;
            }
            return new byte[] { Clamp(nR), Clamp(nG), Clamp(nB) };
        }

        public static string ToHex(byte r, byte g, byte b)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }

        public static bool TryParseHex(string s, out byte r, out byte g, out byte b)
        {
            r = 0;
            g = 0;
            b = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return false;
            try
            {
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string SimulatedCaption(int mode)
        {
            if (mode == 0) return "红色盲大约看成的";
            if (mode == 1) return "绿色盲大约看成的";
            if (mode == 2) return "蓝色盲大约看成的";
            return "全色盲大约看成的（只剩明暗）";
        }

        public static string RelationHint(int mode)
        {
            if (mode == 0 || mode == 1)
                return "看「红」和「绿」右边两块：越像，说明红色盲、绿色盲越难区分红和绿。黄、蓝通常仍分得开。";
            if (mode == 2)
                return "看「黄」和「蓝」右边两块：越像，说明蓝色盲越难区分它们。红、绿通常仍分得开。";
            return "全色盲下右边都会变成灰，只能靠深浅区分，不能靠色相。";
        }

        /// <summary>
        /// 内置示例：红绿色块 + 绿色「通过」/ 红色「删除」，用来对照色盲是否还能分开。
        /// </summary>
        public static Bitmap CreateDemoBitmap()
        {
            var bmp = new Bitmap(DemoWidth, DemoHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var titleFont = NewUiFont(15, FontStyle.Bold))
            using (var labelFont = NewUiFont(13, FontStyle.Regular))
            using (var btnFont = NewUiFont(16, FontStyle.Bold))
            using (var captionFont = NewUiFont(13, FontStyle.Regular))
            using (var titleBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (var white = new SolidBrush(Color.White))
            {
                g.Clear(Color.FromArgb(255, 248, 248, 248));
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawString("示例图（不是你的照片）：红和绿靠颜色区分对错", titleFont, titleBrush, 16, 12);

                for (int i = 0; i < SampleHex.Length; i++)
                {
                    byte r, gv, b;
                    TryParseHex(SampleHex[i], out r, out gv, out b);
                    int x = SwatchX0 + i * (SwatchW + SwatchGap);
                    using (var br = new SolidBrush(Color.FromArgb(r, gv, b)))
                        g.FillRectangle(br, x, SwatchY, SwatchW, SwatchH);
                    g.DrawString(SampleNames[i], labelFont, titleBrush, x + 28, SwatchY + SwatchH + 4);
                }

                int btnY = 140;
                using (var green = new SolidBrush(Color.FromArgb(0x27, 0xAE, 0x60)))
                    g.FillRectangle(green, 16, btnY, 150, 48);
                using (var red = new SolidBrush(Color.FromArgb(0xE7, 0x4C, 0x3C)))
                    g.FillRectangle(red, 182, btnY, 150, 48);
                DrawCentered(g, "通过", btnFont, white, new Rectangle(16, btnY, 150, 48));
                DrawCentered(g, "删除", btnFont, white, new Rectangle(182, btnY, 150, 48));

                g.DrawString("左边「通过」是绿、右边「删除」是红。若模拟后两钮颜色差不多，色盲就分不清谁能点。",
                    captionFont, titleBrush, new RectangleF(16, 200, DemoWidth - 32, 80));
            }
            return bmp;
        }

        /// <summary>按色觉类型变换整图（LockBits，保留 Alpha）。</summary>
        public static Bitmap Transform(Bitmap src, int mode)
        {
            if (src == null) return null;
            int w = src.Width;
            int h = src.Height;
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var src32 = src.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb))
            {
                var rect = new Rectangle(0, 0, w, h);
                BitmapData sdata = src32.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData ddata = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int sStride = sdata.Stride;
                    int dStride = ddata.Stride;
                    int sAbs = Math.Abs(sStride);
                    int dAbs = Math.Abs(dStride);
                    int rowPx = w * 4;
                    byte[] sbuf = new byte[sAbs * h];
                    byte[] dbuf = new byte[dAbs * h];
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(IntPtr.Add(sdata.Scan0, y * sStride), sbuf, y * sAbs, rowPx);
                    for (int y = 0; y < h; y++)
                    {
                        int sRow = y * sAbs;
                        int dRow = y * dAbs;
                        for (int x = 0; x < w; x++)
                        {
                            int si = sRow + x * 4;
                            int di = dRow + x * 4;
                            byte[] t = Map(sbuf[si + 2], sbuf[si + 1], sbuf[si], mode);
                            dbuf[di] = t[2];
                            dbuf[di + 1] = t[1];
                            dbuf[di + 2] = t[0];
                            dbuf[di + 3] = sbuf[si + 3];
                        }
                    }
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(dbuf, y * dAbs, IntPtr.Add(ddata.Scan0, y * dStride), rowPx);
                }
                finally
                {
                    src32.UnlockBits(sdata);
                    dst.UnlockBits(ddata);
                }
            }
            return dst;
        }

        private static void DrawCentered(Graphics g, string text, Font font, Brush brush, Rectangle box)
        {
            var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString(text, font, brush, box, sf);
        }

        private static Font NewUiFont(float pixelSize, FontStyle style)
        {
            try { return new Font("Microsoft YaHei", pixelSize, style, GraphicsUnit.Pixel); }
            catch { return new Font("SimSun", pixelSize, style, GraphicsUnit.Pixel); }
        }

        private static byte Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 255;
            return (byte)Math.Round(v * 255);
        }
    }
}
