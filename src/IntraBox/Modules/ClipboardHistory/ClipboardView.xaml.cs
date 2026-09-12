using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntraBox.Core;

namespace IntraBox.Modules.ClipboardHistory
{
    /// <summary>
    /// 剪贴板历史展示：搜索、固定、删除、写回。监听由全局 ClipboardMonitor 负责。
    /// </summary>
    public partial class ClipboardView : UserControl, IModuleView
    {
        private ICollectionView _view;
        private bool _loading = true;

        public ClipboardView()
        {
            InitializeComponent();

            _view = CollectionViewSource.GetDefaultView(ClipboardStore.Items);
            _view.Filter = FilterItem;
            HistoryList.ItemsSource = _view;
            try
            {
                RecordImageCheck.IsChecked = ConfigManager.Instance.Settings.ClipboardRecordImages;
            }
            catch
            {
                RecordImageCheck.IsChecked = false;
            }
            _loading = false;
        }

        public void OnActivated()
        {
            if (_view != null) _view.Filter = FilterItem;
            if (_view != null) _view.Refresh();
        }

        public void OnDeactivated()
        {
            if (_view != null) _view.Filter = null;
            if (ViewerOverlay != null && ViewerOverlay.Visibility == Visibility.Visible)
                ViewerClose_Click(null, null);
        }

        private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HistoryList.SelectedItem is ClipItem item) PasteItem(item);
        }

        private void HistoryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
                dep = VisualTreeHelper.GetParent(dep);
            var lbi = dep as ListBoxItem;
            if (lbi != null)
            {
                lbi.IsSelected = true;
                lbi.Focus();
            }
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var item = HistoryList.SelectedItem as ClipItem;
            bool has = item != null;
            if (MenuViewItem != null) MenuViewItem.IsEnabled = has;
            if (MenuPasteItem != null) MenuPasteItem.IsEnabled = has;
            if (MenuPinItem != null)
            {
                MenuPinItem.IsEnabled = has;
                MenuPinItem.Header = has && item.IsPinned ? "取消固定" : "固定";
            }
            if (MenuDeleteItem != null) MenuDeleteItem.IsEnabled = has;
            if (MenuClearItem != null) MenuClearItem.IsEnabled = true;
        }

        private void ViewMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowViewer(HistoryList.SelectedItem as ClipItem);
        }

        private void PasteMenu_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.SelectedItem is ClipItem item) PasteItem(item);
        }

        private void PinMenu_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.SelectedItem is ClipItem item)
                item.IsPinned = !item.IsPinned;
        }

        private void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void ClearMenu_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmHelper.Action("确定清空未固定的剪贴板历史？此操作不可撤销。", "清空确认"))
                return;
            for (int i = ClipboardStore.Items.Count - 1; i >= 0; i--)
            {
                if (!ClipboardStore.Items[i].IsPinned) ClipboardStore.Items.RemoveAt(i);
            }
            GcHelper.CollectSafely("clipboard-clear");
        }

        private void ShowViewer(ClipItem item)
        {
            if (item == null) return;
            if (item.IsImage)
            {
                BitmapSource src;
                string err;
                if (!ClipboardStore.TryDecodeOriginal(item, out src, out err))
                {
                    MsgText.Text = err;
                    return;
                }
                ViewerTitle.Text = (item.Preview ?? "[图片]")
                    + "  " + item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                ViewerImage.Source = src;
                ApplyViewerImageSize(src);
                ViewerImageBox.Visibility = Visibility.Visible;
                ViewerScroll.Visibility = Visibility.Collapsed;
                ViewerText.Text = "";
            }
            else
            {
                ViewerTitle.Text = "文本  " + item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                ViewerText.Text = item.Text ?? "";
                ViewerScroll.Visibility = Visibility.Visible;
                ViewerImageBox.Visibility = Visibility.Collapsed;
                ViewerImage.Source = null;
                ViewerImage.Width = double.NaN;
                ViewerImage.Height = double.NaN;
            }
            ViewerOverlay.Visibility = Visibility.Visible;
        }

        private void ViewerClose_Click(object sender, RoutedEventArgs e)
        {
            ViewerOverlay.Visibility = Visibility.Collapsed;
            ViewerImageBox.Visibility = Visibility.Collapsed;
            ViewerScroll.Visibility = Visibility.Visible;
            ViewerImage.Source = null;
            ViewerImage.Width = double.NaN;
            ViewerImage.Height = double.NaN;
            ViewerText.Text = "";
        }

        /// <summary>按屏幕像素 1:1 设 DIP 尺寸；Viewbox 只缩小不放大，完整落入窗口。</summary>
        private void ApplyViewerImageSize(BitmapSource src)
        {
            if (src == null || ViewerImage == null) return;
            double dpiX = 96;
            double dpiY = 96;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                dpiX = dpi.PixelsPerInchX;
                dpiY = dpi.PixelsPerInchY;
            }
            catch { }
            Size dip = ClipboardImage.ScreenDipSize(src.PixelWidth, src.PixelHeight, dpiX, dpiY);
            ViewerImage.Width = dip.Width;
            ViewerImage.Height = dip.Height;
        }

        private void DeleteSelected()
        {
            var item = HistoryList.SelectedItem as ClipItem;
            if (item == null) return;
            string detail = item.IsImage ? item.Preview : ClipItem.TruncateOneLine(item.Text, 80);
            if (!ConfirmHelper.Delete(detail)) return;
            ClipboardStore.Items.Remove(item);
            if (ViewerOverlay.Visibility == Visibility.Visible)
                ViewerClose_Click(null, null);
            GcHelper.CollectSafely("clipboard-delete");
        }

        private void PasteItem(ClipItem item)
        {
            try
            {
                if (item.IsImage)
                {
                    BitmapSource toWrite;
                    string decodeErr;
                    if (!ClipboardStore.TryDecodeOriginal(item, out toWrite, out decodeErr))
                    {
                        MsgText.Text = decodeErr;
                        return;
                    }
                    ClipboardStore.SuppressImageCapture = true;
                    try
                    {
                        string err;
                        if (ClipboardHelper.TrySetImage(toWrite, out err))
                            ClipboardMonitor.MarkPasted(item);
                        else
                        {
                            MsgText.Text = "写回失败：" + err;
                            return;
                        }
                    }
                    finally
                    {
                        ClipboardStore.SuppressImageCapture = false;
                    }
                    MsgText.Text = "已写回剪贴板";
                    return;
                }
                else if (!item.IsImage)
                {
                    string err;
                    if (ClipboardHelper.TrySetText(item.Text, out err))
                        ClipboardMonitor.MarkPasted(item);
                    else
                    {
                        MsgText.Text = "写回失败：" + err;
                        return;
                    }
                }
                MsgText.Text = "已写回剪贴板";
            }
            catch (Exception ex)
            {
                MsgText.Text = "写回失败：" + ex.RootMessage();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _view?.Refresh();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
        }

        private void RecordImageCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = RecordImageCheck != null && RecordImageCheck.IsChecked == true;
            try
            {
                ConfigManager.Instance.Settings.ClipboardRecordImages = on;
                ConfigManager.Instance.Save();
            }
            catch { }
        }

        private bool FilterItem(object obj)
        {
            if (!(obj is ClipItem item)) return false;
            if (string.IsNullOrEmpty(SearchBox.Text)) return true;
            var hay = item.IsImage ? "[图片]" : item.Text;
            return hay != null && hay.IndexOf(SearchBox.Text, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
