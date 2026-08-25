using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using IntraBox.Core;

namespace IntraBox.Modules.ClipboardHistory
{
    /// <summary>
    /// 剪贴板历史数据存储（静态单例）：独立于视图生命周期。
    /// 条数上限 200；全固定时拒绝再插入。单条文本/图片受设置中的文件大小上限约束。
    /// </summary>
    public static class ClipboardStore
    {
        public static int MaxItems
        {
            get { return AppSettings.ClampClipboardMax(ConfigManager.Instance.Settings.ClipboardMaxItems); }
        }

        public static readonly ObservableCollection<ClipItem> Items = new ObservableCollection<ClipItem>();

        public static string LastText;
        public static string LastImageSig;

        /// <summary>插入成功返回 true；全固定且已满时返回 false。</summary>
        public static bool Add(ClipItem item)
        {
            if (item == null) return false;
            while (Items.Count >= MaxItems)
            {
                int removeIdx = -1;
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (!Items[i].IsPinned) { removeIdx = i; break; }
                }
                if (removeIdx < 0) return false;
                Items.RemoveAt(removeIdx);
            }
            Items.Insert(0, item);
            return true;
        }

        /// <summary>设置下调上限后裁掉未固定的多余项。</summary>
        public static void TrimToLimit()
        {
            int max = MaxItems;
            while (Items.Count > max)
            {
                int removeIdx = -1;
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (!Items[i].IsPinned) { removeIdx = i; break; }
                }
                if (removeIdx < 0) break;
                Items.RemoveAt(removeIdx);
            }
        }
    }

    public sealed class ClipItem : INotifyPropertyChanged
    {
        private bool _isPinned;

        public bool IsImage { get; set; }
        public string Text { get; set; }
        /// <summary>图片历史只保留独立缩略图（最长边约 256px），不持有原图。</summary>
        public BitmapSource Thumb { get; set; }
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
