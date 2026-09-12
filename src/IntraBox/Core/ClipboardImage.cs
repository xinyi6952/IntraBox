using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IntraBox.Core
{
    /// <summary>
    /// 剪贴板位图编解码。WPF 的 GetImage/SetImage 会把 32 位 DIB 的全 0 Alpha
    /// 当成预乘透明，RGB 被乘成 0，预览和写回都变成黑块。这里走 PNG / 自解析 DIB，
    /// 全 0 Alpha 时补成不透明，不引入第三方库。
    /// </summary>
    public static class ClipboardImage
    {
        private const string PngFormat = "PNG";
        private const string PngMime = "image/png";

        public static bool HasImage(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                return data.GetDataPresent(PngFormat, false)
                    || data.GetDataPresent(PngMime, false)
                    || data.GetDataPresent(DataFormats.Dib, false)
                    || data.GetDataPresent("Format17", false)
                    || data.GetDataPresent(DataFormats.Bitmap, true);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>从 IDataObject 解出独立 BGRA 像素（stride = width×4）。失败则 bgra 为 null。像素数超限时 width/height 仍有值。</summary>
        public static bool TryDecode(IDataObject data, out int width, out int height, out byte[] bgra)
        {
            return TryDecode(data, long.MaxValue, out width, out height, out bgra);
        }

        public static bool TryDecode(IDataObject data, long maxPixelBytes, out int width, out int height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            if (data == null) return false;
            if (maxPixelBytes < 1) maxPixelBytes = long.MaxValue;

            byte[] png = ReadBytes(data, PngFormat, false) ?? ReadBytes(data, PngMime, false);
            if (png != null)
            {
                int pw, ph;
                if (TryPngSize(png, out pw, out ph) && PixelBytes(pw, ph) > maxPixelBytes)
                {
                    width = pw;
                    height = ph;
                    return false;
                }
                if (TryDecodePng(png, out width, out height, out bgra))
                    return true;
            }

            byte[] dib = ReadBytes(data, DataFormats.Dib, false) ?? ReadBytes(data, "Format17", false);
            if (dib != null)
            {
                int dw, dh;
                if (TryDibSize(dib, out dw, out dh) && PixelBytes(dw, dh) > maxPixelBytes)
                {
                    width = dw;
                    height = dh;
                    return false;
                }
                if (TryDecodeDib(dib, out width, out height, out bgra))
                    return true;
            }

            try
            {
                if (!data.GetDataPresent(DataFormats.Bitmap, true)) return false;
                var src = data.GetData(DataFormats.Bitmap, true) as BitmapSource;
                if (src == null) return false;
                if (PixelBytes(src.PixelWidth, src.PixelHeight) > maxPixelBytes)
                {
                    width = src.PixelWidth;
                    height = src.PixelHeight;
                    return false;
                }
                bgra = CopyBgra32(src, out width, out height);
                return bgra != null;
            }
            catch
            {
                return false;
            }
        }

        private static long PixelBytes(int width, int height)
        {
            return (long)width * height * 4;
        }

        public static byte[] CopyBgra32(BitmapSource src, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (src == null) return null;
            width = src.PixelWidth;
            height = src.PixelHeight;
            if (width < 1 || height < 1) return null;

            BitmapSource current = src;
            if (src.Format != PixelFormats.Bgra32)
                current = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            int stride = width * 4;
            var pixels = new byte[stride * height];
            current.CopyPixels(pixels, stride, 0);
            FixZeroAlpha(pixels);
            return pixels;
        }

        public static BitmapSource ToFrozenBgra32(byte[] bgra, int width, int height, double dpiX, double dpiY)
        {
            if (bgra == null || width < 1 || height < 1) return null;
            if (dpiX <= 0) dpiX = 96;
            if (dpiY <= 0) dpiY = 96;
            var bmp = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, bgra, width * 4);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// 按屏幕 DPI 把像素换成 DIP，使 1 图像素对应 1 屏幕像素。
        /// 位图常标 96DPI，WPF 按 DIP 排布时在 125%/150% 缩放下会显得被放大。
        /// dpi 无效时按 96；宽高至少为 1。
        /// </summary>
        public static Size ScreenDipSize(int pixelWidth, int pixelHeight, double dpiX, double dpiY)
        {
            if (dpiX < 1) dpiX = 96;
            if (dpiY < 1) dpiY = 96;
            if (pixelWidth < 1) pixelWidth = 1;
            if (pixelHeight < 1) pixelHeight = 1;
            return new Size(pixelWidth * 96.0 / dpiX, pixelHeight * 96.0 / dpiY);
        }

        public static BitmapSource CreateThumb(byte[] bgra, int width, int height, int maxEdge, double dpiX, double dpiY)
        {
            if (bgra == null || width < 1 || height < 1 || maxEdge < 1) return null;
            double scale = Math.Min(1.0, (double)maxEdge / Math.Max(width, height));
            int tw = Math.Max(1, (int)Math.Round(width * scale));
            int th = Math.Max(1, (int)Math.Round(height * scale));
            byte[] thumb = (tw == width && th == height) ? bgra : DownsampleBgra(bgra, width, height, tw, th);
            return ToFrozenBgra32(thumb, tw, th, dpiX, dpiY);
        }

        public static byte[] DownsampleBgra(byte[] src, int width, int height, int tw, int th)
        {
            var dst = new byte[tw * th * 4];
            for (int y = 0; y < th; y++)
            {
                int sy = Math.Min(height - 1, (int)((y + 0.5) * height / th));
                int srcRow = sy * width * 4;
                int dstRow = y * tw * 4;
                for (int x = 0; x < tw; x++)
                {
                    int sx = Math.Min(width - 1, (int)((x + 0.5) * width / tw));
                    int s = srcRow + sx * 4;
                    int d = dstRow + x * 4;
                    dst[d] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    dst[d + 3] = src[s + 3];
                }
            }
            return dst;
        }

        /// <summary>用原图像素抽样，避免 TransformedBitmap 再拷一整张图。</summary>
        public static string Signature(int width, int height, byte[] bgra)
        {
            if (bgra == null || width < 1 || height < 1) return null;
            const int sample = 16;
            int tw = Math.Min(sample, width);
            int th = Math.Min(sample, height);
            long sum = (long)width * 100000 + height;
            for (int y = 0; y < th; y++)
            {
                int sy = y * height / th;
                int row = sy * width * 4;
                for (int x = 0; x < tw; x++)
                {
                    int i = row + (x * width / tw) * 4;
                    sum = ((sum * 31 + bgra[i]) * 31 + bgra[i + 1]) * 31 + bgra[i + 2];
                    sum = sum * 31 + bgra[i + 3];
                }
            }
            return width + "x" + height + ":" + sum;
        }

        /// <summary>32 位 DIB 常把未用 Alpha 全填 0，WPF 当预乘后整图变黑。全 0 时改为 255。</summary>
        public static void FixZeroAlpha(byte[] bgra)
        {
            if (bgra == null || bgra.Length < 4) return;
            for (int i = 3; i < bgra.Length; i += 4)
            {
                if (bgra[i] != 0) return;
            }
            for (int i = 3; i < bgra.Length; i += 4)
                bgra[i] = 255;
        }

        public static DataObject CreateDataObject(byte[] bgra, int width, int height, double dpiX, double dpiY)
        {
            var frozen = ToFrozenBgra32(bgra, width, height, dpiX, dpiY);
            if (frozen == null) return null;
            var data = new DataObject();
            data.SetData(PngFormat, EncodePng(frozen), true);
            data.SetData(DataFormats.Dib, new MemoryStream(EncodeDib24(bgra, width, height)), true);
            data.SetData(DataFormats.Bitmap, frozen, true);
            return data;
        }

        public static DataObject CreateDeferredDataObject(byte[] bgra, int width, int height, double dpiX, double dpiY)
        {
            var frozen = ToFrozenBgra32(bgra, width, height, dpiX, dpiY);
            if (frozen == null) return null;
            var data = new DataObject();
            data.SetData(PngFormat, (Func<object>)(() => EncodePng(bgra, width, height)), false);
            data.SetData(DataFormats.Dib, (Func<object>)(() => new MemoryStream(EncodeDib24(bgra, width, height))), false);
            data.SetData(DataFormats.Bitmap, (Func<object>)(() => frozen), false);
            return data;
        }

        /// <summary>24 位 BI_RGB DIB（无 Alpha），其它程序用 GetImage 也不会把图乘成黑块。</summary>
        public static byte[] EncodeDib24(byte[] bgra, int width, int height)
        {
            int stride = ((width * 3 + 3) / 4) * 4;
            var dib = new byte[40 + stride * height];
            WriteInt32(dib, 0, 40);
            WriteInt32(dib, 4, width);
            WriteInt32(dib, 8, height);
            WriteInt16(dib, 12, 1);
            WriteInt16(dib, 14, 24);
            WriteInt32(dib, 20, stride * height);
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * width * 4;
                int dstRow = 40 + y * stride;
                for (int x = 0; x < width; x++)
                {
                    int s = srcRow + x * 4;
                    int d = dstRow + x * 3;
                    dib[d] = bgra[s];
                    dib[d + 1] = bgra[s + 1];
                    dib[d + 2] = bgra[s + 2];
                }
            }
            return dib;
        }

        public static bool TryDibSize(byte[] dib, out int width, out int height)
        {
            return TryReadDibHeader(dib, out width, out height, out _, out _, out _, out _, out _, out _);
        }

        public static bool TryDecodeDib(byte[] dib, out int width, out int height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            int bitCount, offset;
            bool topDown;
            if (!TryReadDibHeader(dib, out width, out height, out _, out _, out _, out bitCount, out offset, out topDown))
                return false;

            int srcStride = ((width * bitCount + 31) / 32) * 4;
            if (offset + (long)srcStride * height > dib.Length) return false;

            bgra = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int srcY = topDown ? y : (height - 1 - y);
                int srcRow = offset + srcY * srcStride;
                int dstRow = y * width * 4;
                if (bitCount == 32)
                {
                    Buffer.BlockCopy(dib, srcRow, bgra, dstRow, width * 4);
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        int s = srcRow + x * 3;
                        int d = dstRow + x * 4;
                        bgra[d] = dib[s];
                        bgra[d + 1] = dib[s + 1];
                        bgra[d + 2] = dib[s + 2];
                        bgra[d + 3] = 255;
                    }
                }
            }
            if (bitCount == 32) FixZeroAlpha(bgra);
            return true;
        }

        private static bool TryReadDibHeader(byte[] dib, out int width, out int height, out int start, out int biSize, out int compression, out int bitCount, out int pixelOffset, out bool topDown)
        {
            width = 0;
            height = 0;
            start = 0;
            biSize = 0;
            compression = 0;
            bitCount = 0;
            pixelOffset = 0;
            topDown = false;
            if (dib == null || dib.Length < 40) return false;
            if (dib.Length >= 54 && dib[0] == (byte)'B' && dib[1] == (byte)'M')
                start = 14;
            if (dib.Length - start < 40) return false;

            biSize = BitConverter.ToInt32(dib, start);
            if (biSize < 40 || start + biSize > dib.Length) return false;

            width = BitConverter.ToInt32(dib, start + 4);
            int heightRaw = BitConverter.ToInt32(dib, start + 8);
            topDown = heightRaw < 0;
            height = heightRaw < 0 ? -heightRaw : heightRaw;
            short planes = BitConverter.ToInt16(dib, start + 12);
            bitCount = BitConverter.ToInt16(dib, start + 14);
            compression = BitConverter.ToInt32(dib, start + 16);
            if (width < 1 || height < 1 || planes != 1) return false;
            if (compression != 0 && compression != 3) return false;
            if (bitCount != 24 && bitCount != 32) return false;

            pixelOffset = start + biSize;
            if (compression == 3 && biSize == 40)
                pixelOffset += 12;
            return pixelOffset >= 0;
        }

        public static bool TryPngSize(byte[] png, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (png == null || png.Length < 24) return false;
            if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return false;
            width = ReadInt32Be(png, 16);
            height = ReadInt32Be(png, 20);
            return width > 0 && height > 0;
        }

        private static bool TryDecodePng(byte[] png, out int width, out int height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            try
            {
                BitmapImage img;
                using (var ms = new MemoryStream(png, false))
                {
                    img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                }
                bgra = CopyBgra32(img, out width, out height);
                return bgra != null;
            }
            catch
            {
                return false;
            }
        }

        public static MemoryStream EncodePng(byte[] bgra, int width, int height)
        {
            return EncodePng(ToFrozenBgra32(bgra, width, height, 96, 96));
        }

        public static byte[] EncodePngBytes(byte[] bgra, int width, int height)
        {
            using (var ms = EncodePng(bgra, width, height))
                return ms.ToArray();
        }

        public static bool TryDecodePngBytes(byte[] png, out int width, out int height, out byte[] bgra)
        {
            return TryDecodePng(png, out width, out height, out bgra);
        }

        public static MemoryStream EncodePng(BitmapSource src)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;
            return ms;
        }

        private static byte[] ReadBytes(IDataObject data, string format, bool autoConvert)
        {
            try
            {
                if (!data.GetDataPresent(format, autoConvert)) return null;
                return AsBytes(data.GetData(format, autoConvert));
            }
            catch
            {
                return null;
            }
        }

        private static byte[] AsBytes(object raw)
        {
            var bytes = raw as byte[];
            if (bytes != null && bytes.Length > 0) return bytes;
            var stream = raw as Stream;
            if (stream == null) return null;
            var ms = stream as MemoryStream;
            if (ms != null)
            {
                try
                {
                    if (ms.Length <= 0) return null;
                    return ms.ToArray();
                }
                catch
                {
                    return null;
                }
            }
            if (stream.CanSeek) stream.Position = 0;
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.Length > 0 ? copy.ToArray() : null;
            }
        }

        private static void WriteInt16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteInt32(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32Be(byte[] buf, int offset)
        {
            return (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
        }
    }
}
