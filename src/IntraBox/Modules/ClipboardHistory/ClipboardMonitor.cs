using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntraBox.Core;

namespace IntraBox.Modules.ClipboardHistory
{
    /// <summary>
    /// 全局剪贴板监听：用 Windows 剪贴板链监听（AddClipboardFormatListener），事件驱动，
    /// 仅在剪贴板变化时读取一次，避免轮询与用户粘贴竞争剪贴板（OpenClipboard 互斥）。
    /// 必须在 UI 线程启动，与剪贴板 API、ObservableCollection 同线程。
    /// </summary>
    public static class ClipboardMonitor
    {
        private static HwndSource _source;

        public static void Start()
        {
            if (_source != null) return;
            var parameters = new HwndSourceParameters("IntraBoxClipboardListener")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x80 // WS_EX_TOOLWINDOW，不显示在任务栏
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            Win32Native.AddClipboardFormatListener(_source.Handle);
            CheckClipboard();
        }

        public static void Stop()
        {
            if (_source == null) return;
            Win32Native.RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Native.WM_CLIPBOARDUPDATE)
            {
                CheckClipboard();
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 写回剪贴板之后调用：记下剪贴板上的实际指纹，后续相同指纹不再入库。
        /// </summary>
        public static void MarkPasted(ClipItem item)
        {
            if (item == null) return;
            if (item.IsImage)
            {
                if (item.Thumb != null)
                    ClipboardStore.LastImageSig = GetImageSignature(item.Thumb);
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        var img = Clipboard.GetImage();
                        if (img != null)
                            ClipboardStore.LastImageSig = GetImageSignature(img);
                    }
                }
                catch { }
            }
            else
            {
                ClipboardStore.LastText = item.Text;
            }
        }

        /// <summary>检测剪贴板变化（文本与图片分开处理，避免一个失败影响另一个）。</summary>
        private static void CheckClipboard()
        {
            CheckClipboardText();
            CheckClipboardImage();
        }

        private static void CheckClipboardText()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                var text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text)) return;
                if (text == ClipboardStore.LastText) return;
                string err;
                if (!SizeLimits.TryCheckText(text, out err))
                {
                    ClipboardStore.LastText = text.Length + ":" + (text.Length > 64 ? text.Substring(0, 64) : text);
                    return;
                }
                ClipboardStore.LastText = text;
                AddTextItem(text);
            }
            catch
            {
                // 剪贴板被其他进程短暂占用，忽略本次，下个周期重试
            }
        }

        private static void CheckClipboardImage()
        {
            try
            {
                if (!Clipboard.ContainsImage()) return;
                var img = Clipboard.GetImage();
                if (img == null) return;
                var sig = GetImageSignature(img);
                if (ClipboardStore.SuppressImageCapture)
                {
                    ClipboardStore.LastImageSig = sig;
                    return;
                }
                if (ClipboardStore.ShouldSkipDuplicateImage(sig, ClipboardStore.LastImageSig))
                    return;
                long bytes = (long)img.PixelWidth * img.PixelHeight * 4;
                if (bytes > SizeLimits.MaxFileBytes)
                {
                    ClipboardStore.LastImageSig = sig;
                    return;
                }
                ClipboardStore.LastImageSig = sig;
                AddImage(img);
            }
            catch
            {
                // 同上
            }
        }

        private static void AddTextItem(string text)
        {
            ClipboardStore.Add(new ClipItem
            {
                IsImage = false,
                Text = text,
                Preview = ClipItem.TruncateOneLine(text, 72),
                Thumb = null,
                Time = DateTime.Now
            });
        }

        private const int ThumbMaxEdge = 128;

        private static void AddImage(BitmapSource img)
        {
            int origW = img.PixelWidth;
            int origH = img.PixelHeight;
            var thumb = CopyAsThumb(img, ThumbMaxEdge);
            ClipboardStore.Add(new ClipItem
            {
                IsImage = true,
                Text = null,
                Preview = "[图片] " + origW + "×" + origH,
                Thumb = thumb,
                Time = DateTime.Now
            });
        }

        /// <summary>
        /// 生成独立缩略图：把像素拷进新 BitmapSource 并 Freeze，
        /// 避免 TransformedBitmap 继续引用剪贴板原图导致内存收不回。
        /// </summary>
        private static BitmapSource CopyAsThumb(BitmapSource src, int maxEdge)
        {
            int w = src.PixelWidth;
            int h = src.PixelHeight;
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            double scale = Math.Min(1.0, (double)maxEdge / Math.Max(w, h));
            int tw = Math.Max(1, (int)Math.Round(w * scale));
            int th = Math.Max(1, (int)Math.Round(h * scale));

            BitmapSource current = src;
            if (src.Format != PixelFormats.Bgra32)
                current = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            if (tw != w || th != h)
            {
                double sx = (double)tw / w;
                double sy = (double)th / h;
                current = new TransformedBitmap(current, new ScaleTransform(sx, sy));
            }

            int stride = tw * 4;
            var pixels = new byte[stride * th];
            current.CopyPixels(pixels, stride, 0);
            double dpiX = src.DpiX > 0 ? src.DpiX : 96;
            double dpiY = src.DpiY > 0 ? src.DpiY : 96;
            var copy = BitmapSource.Create(tw, th, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
            copy.Freeze();
            return copy;
        }

        /// <summary>图片指纹：用于去重，以及写回剪贴板后避免立刻再记一条。</summary>
        private static string GetImageSignature(BitmapSource img)
        {
            int w = img.PixelWidth, h = img.PixelHeight;
            const int sample = 16;
            var sw = Math.Max(1.0, (double)sample / w);
            var sh = Math.Max(1.0, (double)sample / h);
            var scaled = new TransformedBitmap(img, new ScaleTransform(sw, sh));
            var fmt = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int pw = fmt.PixelWidth, ph = fmt.PixelHeight;
            int stride = pw * 4;
            var pixels = new byte[stride * ph];
            fmt.CopyPixels(pixels, stride, 0);

            long sum = (long)w * 100000 + h;
            foreach (var b in pixels) sum = sum * 31 + b;
            return w + "x" + h + ":" + sum;
        }
    }
}
