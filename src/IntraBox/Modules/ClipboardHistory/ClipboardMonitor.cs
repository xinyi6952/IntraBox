using System;
using System.Windows;
using System.Windows.Interop;
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
        private const int ThumbMaxEdge = 128;

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
                int w, h;
                byte[] bgra;
                if (ClipboardHelper.TryGetImageBgra(out w, out h, out bgra))
                    ClipboardStore.LastImageSig = ClipboardImage.Signature(w, h, bgra);
                else if (item.Thumb != null)
                {
                    bgra = ClipboardImage.CopyBgra32(item.Thumb, out w, out h);
                    ClipboardStore.LastImageSig = ClipboardImage.Signature(w, h, bgra);
                }
            }
            else
            {
                ClipboardStore.LastText = item.Text;
            }
        }

        /// <summary>检测剪贴板变化（一次打开同时取文本与图片，避免连开两次）。</summary>
        private static void CheckClipboard()
        {
            string text;
            int w, h;
            byte[] bgra;
            bool recordImages = ClipboardStore.RecordImagesEnabled;
            bool wantImg = ClipboardStore.ShouldCaptureImage(
                recordImages,
                MemoryIdleGuard.IsParked,
                ClipboardStore.SkipImagesWhenParked);
            if (!ClipboardHelper.TryReadClipboard(SizeLimits.MaxFileBytes, wantImg, out text, out w, out h, out bgra)) return;
            CheckClipboardText(text);
            if (ClipboardStore.ShouldCaptureImage(recordImages, MemoryIdleGuard.IsParked, ClipboardStore.SkipImagesWhenParked))
                CheckClipboardImage(w, h, bgra);
        }

        private static void CheckClipboardText(string text)
        {
            if (ClipboardStore.IsBlankText(text)) return;
            string err;
            if (!SizeLimits.TryCheckText(text, out err))
            {
                ClipboardStore.LastText = text.Length + ":" + (text.Length > 64 ? text.Substring(0, 64) : text);
                return;
            }
            ClipboardStore.LastText = text;
            AddTextItem(text);
        }

        private static void CheckClipboardImage(int origW, int origH, byte[] bgra)
        {
            if (origW < 1 || origH < 1) return;
            if (bgra == null)
            {
                ClipboardStore.LastImageSig = origW + "x" + origH + ":oversized";
                return;
            }
            var sig = ClipboardImage.Signature(origW, origH, bgra);
            if (ClipboardStore.SuppressImageCapture)
            {
                ClipboardStore.LastImageSig = sig;
                return;
            }
            ClipboardStore.LastImageSig = sig;
            AddImage(origW, origH, bgra, sig);
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

        private static void AddImage(int origW, int origH, byte[] bgra, string sig)
        {
            var thumb = ClipboardImage.CreateThumb(bgra, origW, origH, ThumbMaxEdge, 96, 96);
            byte[] png = ClipboardImage.EncodePngBytes(bgra, origW, origH);
            ClipboardStore.Add(new ClipItem
            {
                IsImage = true,
                Text = null,
                ImageSig = sig,
                Preview = "[图片] " + origW + "×" + origH,
                Thumb = thumb,
                ImagePng = png,
                Time = DateTime.Now
            });
        }
    }
}
