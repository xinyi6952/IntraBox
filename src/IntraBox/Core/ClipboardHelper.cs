using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IntraBox.Core
{
    /// <summary>剪贴板写入：占用时 COMException 不弹全局框。</summary>
    public static class ClipboardHelper
    {
        // 超过该字符数改用延迟渲染：DataObject 只在目标程序粘贴时才提供数据，
        // 避免 SetText 同步拷贝几 MB 文本阻塞 UI。内容逐字符原样保留，绝不压缩/重排。
        private const int DeferredThresholdChars = 1 * 1024 * 1024; // 约 1MB（按 UTF-16 字符估算）
        private const long DeferredImagePixels = 4000L * 3000L; // 约 1200 万像素以上才延迟渲染

        // OpenClipboard 被其它进程短暂占用时会抛 CLIPBRD_E_CANT_OPEN (0x800401D0)。
        // 重试 10 次、间隔递增 10/12/15/18/22/25/...ms（末次失败不再等待），累计约 200ms，足够覆盖绝大多数瞬时占用。
        private const int RetryCount = 10;
        private static readonly int[] RetryDelayMs = { 10, 12, 15, 18, 22, 25, 28, 32, 36, 40 };

        // CLIPBRD_E_CANT_OPEN：剪贴板被其它进程锁住，可重试恢复。
        private const int CLIPBRD_E_CANT_OPEN = unchecked((int)0x800401D0);

        /// <summary>
        /// 包裹一次剪贴板访问：遇 OpenClipboard 失败按退避重试，仍失败则吞掉返回 false。
        /// 绝不让 COMException/ExternalException 冒泡成全局未处理异常。
        /// </summary>
        private static bool TryWithRetry(Action action)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (COMException ex) when (ex.ErrorCode == CLIPBRD_E_CANT_OPEN)
                {
                    // 仅重试真·占用；其它异常（坏数据/权限等）fail-fast，交给调用方报真实原因。
                    if (i < RetryCount - 1) Sleep(i);
                }
            }
            return false;
        }

        private static T TryWithRetry<T>(Func<T> func, T fallback)
        {
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    return func();
                }
                catch (COMException ex) when (ex.ErrorCode == CLIPBRD_E_CANT_OPEN)
                {
                    // 同上：仅重试真·占用。
                    if (i < RetryCount - 1) Sleep(i);
                }
            }
            return fallback;
        }

        private static void Sleep(int attempt)
        {
            int ms = attempt < RetryDelayMs.Length ? RetryDelayMs[attempt] : RetryDelayMs[RetryDelayMs.Length - 1];
            Thread.Sleep(ms);
        }

        public static bool TrySetText(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "没有可复制的内容";
                return false;
            }
            try
            {
                if (text.Length >= DeferredThresholdChars)
                {
                    var data = new DataObject();
                    string captured = text;
                    // Func<object> 触发 OLE 延迟渲染：目标程序真正粘贴时才回调取数据。
                    // 同时注册 UnicodeText 与 Text，任一格式被请求都返回同一份原文。
                    data.SetData(DataFormats.UnicodeText, (Func<object>)(() => captured), false);
                    data.SetData(DataFormats.Text, (Func<object>)(() => captured), false);
                    if (!TryWithRetry(() => Clipboard.SetDataObject(data, true)))
                    {
                        error = "复制失败：剪贴板被占用，请稍后重试";
                        return false;
                    }
                }
                else
                {
                    if (!TryWithRetry(() => Clipboard.SetText(text)))
                    {
                        error = "复制失败：剪贴板被占用，请稍后重试";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "复制失败：" + ex.Message;
                return false;
            }
        }

        public static bool TrySetText(string text)
        {
            string error;
            return TrySetText(text, out error);
        }

        public static bool TrySetImage(BitmapSource image, out string error)
        {
            error = null;
            if (image == null)
            {
                error = "没有可复制的图像";
                return false;
            }
            try
            {
                int w, h;
                byte[] bgra = ClipboardImage.CopyBgra32(image, out w, out h);
                if (bgra == null)
                {
                    error = "没有可复制的图像";
                    return false;
                }

                // 大图（约 4K 级）延迟编码 PNG/DIB：仅在目标程序粘贴时再压，避免复制瞬间卡住 UI。
                long pixels = (long)w * h;
                DataObject data = pixels >= DeferredImagePixels
                    ? ClipboardImage.CreateDeferredDataObject(bgra, w, h, image.DpiX, image.DpiY)
                    : ClipboardImage.CreateDataObject(bgra, w, h, image.DpiX, image.DpiY);
                if (data == null)
                {
                    error = "没有可复制的图像";
                    return false;
                }
                if (!TryWithRetry(() => Clipboard.SetDataObject(data, true)))
                {
                    error = "复制失败：剪贴板被占用，请稍后重试";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "复制失败：" + ex.Message;
                return false;
            }
        }

        // ---------- 读取：监听/粘贴路径同样需要重试 ----------

        /// <summary>安全读取剪贴板文本。占用时重试，仍失败返回 false（绝不抛）。</summary>
        public static bool TryGetText(out string text)
        {
            text = null;
            try
            {
                // 无文本时 GetText 返回 ""（不抛），占用时才抛，故无需前置 ContainsText；空文本由调用方兜住。
                text = TryWithRetry(() => Clipboard.GetText(), null);
                return text != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>安全判断剪贴板是否含文本。占用时返回 false（保守视为无）。</summary>
        public static bool SafeContainsText()
        {
            try { return TryWithRetry(() => Clipboard.ContainsText(), false); }
            catch { return false; }
        }

        /// <summary>安全判断剪贴板是否含图像（含仅有 PNG 而无 CF_BITMAP 的情况）。占用时返回 false。</summary>
        public static bool SafeContainsImage()
        {
            try
            {
                return TryWithRetry(() =>
                {
                    var data = Clipboard.GetDataObject();
                    return ClipboardImage.HasImage(data);
                }, false);
            }
            catch { return false; }
        }

        /// <summary>安全读取剪贴板图像。走 PNG/DIB 解码，避免 WPF GetImage 黑块。占用时返回 null。</summary>
        public static BitmapSource SafeGetImage()
        {
            int w, h;
            byte[] bgra;
            if (!TryGetImageBgra(out w, out h, out bgra) || bgra == null) return null;
            return ClipboardImage.ToFrozenBgra32(bgra, w, h, 96, 96);
        }

        /// <summary>一次 OpenClipboard：文本 + 图片像素。占用时返回 false。图片像素超限时仍返回宽高、bgra 为 null。</summary>
        public static bool TryReadClipboard(long maxPixelBytes, out string text, out int imageWidth, out int imageHeight, out byte[] imageBgra)
        {
            text = null;
            imageWidth = 0;
            imageHeight = 0;
            imageBgra = null;
            try
            {
                var result = TryWithRetry(() =>
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null) return null;
                    int w, h;
                    byte[] bgra;
                    ClipboardImage.TryDecode(data, maxPixelBytes, out w, out h, out bgra);
                    return Tuple.Create(ReadText(data), w, h, bgra);
                }, null);
                if (result == null) return false;
                text = result.Item1;
                imageWidth = result.Item2;
                imageHeight = result.Item3;
                imageBgra = result.Item4;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>只取图片像素。占用或无图返回 false。</summary>
        public static bool TryGetImageBgra(out int width, out int height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = null;
            try
            {
                var result = TryWithRetry(() =>
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null) return null;
                    int w, h;
                    byte[] px;
                    ClipboardImage.TryDecode(data, out w, out h, out px);
                    return Tuple.Create(w, h, px);
                }, null);
                if (result == null || result.Item3 == null) return false;
                width = result.Item1;
                height = result.Item2;
                bgra = result.Item3;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadText(IDataObject data)
        {
            if (data == null) return null;
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    var t = data.GetData(DataFormats.UnicodeText, true) as string;
                    if (t != null) return t;
                }
                if (data.GetDataPresent(DataFormats.Text, true))
                    return data.GetData(DataFormats.Text, true) as string;
            }
            catch
            {
            }
            return null;
        }
    }
}
