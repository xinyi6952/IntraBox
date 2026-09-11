using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using IntraBox.Core;

namespace IntraBox.Modules.ClipboardHistory
{
    /// <summary>
    /// 剪贴板历史数据存储（静态单例）：独立于视图生命周期。
    /// 总条数上限 200；图片另有条数上限（默认 20，最多 50，且不超过总上限）。全固定时拒绝再插入。
    /// 单条文本/图片受设置中的文件大小上限约束。图片写回始终用原图 PNG，不用缩略图冒充。
    /// </summary>
    public static class ClipboardStore
    {
        public static int MaxItems
        {
            get { return AppSettings.ClampClipboardMax(ConfigManager.Instance.Settings.ClipboardMaxItems); }
        }

        /// <summary>生效的图片条数上限：min(设置值, 总条数)。</summary>
        public static int MaxImageItems
        {
            get
            {
                return EffectiveMaxImageItems(MaxItems, ConfigManager.Instance.Settings.ClipboardMaxImageItems);
            }
        }

        public static int EffectiveMaxImageItems(int maxItems, int maxImages)
        {
            int total = AppSettings.ClampClipboardMax(maxItems);
            int images = AppSettings.ClampClipboardMaxImages(maxImages);
            return images < total ? images : total;
        }

        public static readonly ObservableCollection<ClipItem> Items = new ObservableCollection<ClipItem>();

        public static string LastText;
        public static string LastImageSig;
        /// <summary>写回图片期间为 true，避免 SetImage 重入监听时误记一条。</summary>
        public static bool SuppressImageCapture;

        /// <summary>是否把剪贴板图片记入历史。读配置失败时视为关闭。</summary>
        public static bool RecordImagesEnabled
        {
            get
            {
                try { return ConfigManager.Instance.Settings.ClipboardRecordImages; }
                catch { return false; }
            }
        }

        /// <summary>勾选记录图片时才入库。写回抑制在监听路径内部判断。</summary>
        public static bool ShouldCaptureImage(bool recordImages)
        {
            return recordImages;
        }

        /// <summary>托盘停泊且设置为跳过图片时不解码入库。</summary>
        public static bool ShouldCaptureImage(bool recordImages, bool parked, bool skipWhenParked)
        {
            if (!recordImages) return false;
            if (parked && skipWhenParked) return false;
            return true;
        }

        public static bool SkipImagesWhenParked
        {
            get
            {
                try { return ConfigManager.Instance.Settings.ClipboardSkipImagesWhenHidden; }
                catch { return true; }
            }
        }

        /// <summary>空白或纯空白文本不入库。</summary>
        public static bool IsBlankText(string text)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        /// <summary>指纹相同则视为同一张图（写回回声或再次复制）。</summary>
        public static bool ShouldSkipDuplicateImage(string sig, string lastSig)
        {
            return sig != null && sig == lastSig;
        }

        /// <summary>插入成功返回 true；空白文本、全固定且已满时返回 false。重复内容去掉旧条，以本次为准。</summary>
        public static bool Add(ClipItem item)
        {
            if (item == null) return false;
            if (!item.IsImage && IsBlankText(item.Text)) return false;
            RemoveDuplicates(item);
            if (item.IsImage)
            {
                while (CountImages() >= MaxImageItems)
                {
                    int imgIdx = FindOldestUnpinned(true);
                    if (imgIdx < 0) return false;
                    Items.RemoveAt(imgIdx);
                }
            }
            while (Items.Count >= MaxItems)
            {
                int removeIdx = FindOldestUnpinned(false);
                if (removeIdx < 0) return false;
                Items.RemoveAt(removeIdx);
            }
            Items.Insert(0, item);
            return true;
        }

        private static void RemoveDuplicates(ClipItem item)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!IsSameContent(Items[i], item)) continue;
                if (Items[i].IsPinned) item.IsPinned = true;
                Items.RemoveAt(i);
            }
        }

        public static bool IsSameContent(ClipItem a, ClipItem b)
        {
            if (a == null || b == null) return false;
            if (a.IsImage != b.IsImage) return false;
            if (a.IsImage)
                return !string.IsNullOrEmpty(a.ImageSig) && a.ImageSig == b.ImageSig;
            return string.Equals(a.Text, b.Text, StringComparison.Ordinal);
        }

        /// <summary>设置下调上限后裁掉未固定的多余项（先按图片上限，再按总条数）。</summary>
        public static void TrimToLimit()
        {
            int imgMax = MaxImageItems;
            while (CountImages() > imgMax)
            {
                int imgIdx = FindOldestUnpinned(true);
                if (imgIdx < 0) break;
                Items.RemoveAt(imgIdx);
            }
            int max = MaxItems;
            while (Items.Count > max)
            {
                int removeIdx = FindOldestUnpinned(false);
                if (removeIdx < 0) break;
                Items.RemoveAt(removeIdx);
            }
        }

        public static int CountImages()
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].IsImage) n++;
            }
            return n;
        }

        /// <summary>查看/写回只用原图 PNG；解码失败不返回缩略图。</summary>
        public const string OriginalImageMissing = "原图数据缺失或已损坏，无法用缩略图代替。";

        public static bool TryDecodeOriginal(ClipItem item, out BitmapSource src, out string error)
        {
            src = null;
            error = null;
            if (item == null || !item.IsImage)
            {
                error = OriginalImageMissing;
                return false;
            }
            if (item.ImagePng == null || item.ImagePng.Length == 0)
            {
                error = OriginalImageMissing;
                return false;
            }
            int w, h;
            byte[] bgra;
            if (!ClipboardImage.TryDecodePngBytes(item.ImagePng, out w, out h, out bgra))
            {
                error = OriginalImageMissing;
                return false;
            }
            src = ClipboardImage.ToFrozenBgra32(bgra, w, h, 96, 96);
            if (src == null)
            {
                error = OriginalImageMissing;
                return false;
            }
            return true;
        }

        /// <summary>从列表尾部（较旧）找第一条未固定项；imagesOnly 时只找图片。</summary>
        private static int FindOldestUnpinned(bool imagesOnly)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].IsPinned) continue;
                if (imagesOnly && !Items[i].IsImage) continue;
                return i;
            }
            return -1;
        }
    }

    public sealed class ClipItem : INotifyPropertyChanged
    {
        private bool _isPinned;

        public bool IsImage { get; set; }
        public string Text { get; set; }
        /// <summary>图片指纹，用于去重。</summary>
        public string ImageSig { get; set; }
        /// <summary>图片历史列表用缩略图；写回/查看用原图像素的 PNG 压缩副本。</summary>
        public BitmapSource Thumb { get; set; }
        /// <summary>原图 PNG（压缩），避免只写回 128px 缩略图发糊。</summary>
        public byte[] ImagePng { get; set; }
        public string Preview { get; set; }

        /// <summary>列表用的单行精简预览（约 72 字，多行压成一行）。</summary>
        public string ShortPreview
        {
            get { return ClipItem.MakeShortPreview(IsImage, Text, Preview); }
        }

        public DateTime Time { get; set; }

        public static string MakeShortPreview(bool isImage, string text, string fallback)
        {
            if (isImage)
                return string.IsNullOrEmpty(fallback) ? "[图片]" : fallback;
            return TruncateOneLine(text, 72);
        }

        public static string TruncateOneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var single = s.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
            while (single.IndexOf("  ", StringComparison.Ordinal) >= 0)
                single = single.Replace("  ", " ");
            single = single.Trim();
            if (single.Length <= max) return single;
            return single.Substring(0, max) + "…";
        }

        public bool IsPinned
        {
            get { return _isPinned; }
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs("IsPinned"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
