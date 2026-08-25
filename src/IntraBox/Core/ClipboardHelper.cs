using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IntraBox.Core
{
    /// <summary>剪贴板写入：占用时 COMException 不弹全局框。</summary>
    public static class ClipboardHelper
    {
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
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                error = "复制失败：" + (ex is COMException ? "剪贴板被占用，请稍后重试" : ex.Message);
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
                Clipboard.SetImage(image);
                return true;
            }
            catch (Exception ex)
            {
                error = "复制失败：" + (ex is COMException ? "剪贴板被占用，请稍后重试" : ex.Message);
                return false;
            }
        }
    }
}
