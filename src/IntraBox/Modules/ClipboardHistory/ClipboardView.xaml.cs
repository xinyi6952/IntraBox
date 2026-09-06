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

        public ClipboardView()
        {
            InitializeComponent();

            _view = CollectionViewSource.GetDefaultView(ClipboardStore.Items);
            _view.Filter = FilterItem;
            HistoryList.ItemsSource = _view;
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
            GcHelper.CollectSafely();
        }

        private void ShowViewer(ClipItem item)
        {
            if (item == null) return;
            if (item.IsImage)
            {
                BitmapSource src = DecodeOriginal(item) ?? item.Thumb;
                ViewerTitle.Text = (item.Preview ?? "[图片]")
                    + "  " + item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                ViewerImage.Source = src;
                ViewerImage.Visibility = Visibility.Visible;
                ViewerText.Visibility = Visibility.Collapsed;
                ViewerText.Text = "";
            }
            else
            {
                ViewerTitle.Text = "文本  " + item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                ViewerText.Text = item.Text ?? "";
                ViewerText.Visibility = Visibility.Visible;
                ViewerImage.Visibility = Visibility.Collapsed;
                ViewerImage.Source = null;
            }
            ViewerOverlay.Visibility = Visibility.Visible;
        }

        private static BitmapSource DecodeOriginal(ClipItem item)
        {
            if (item == null || item.ImagePng == null) return null;
            int w, h;
            byte[] bgra;
            if (!ClipboardImage.TryDecodePngBytes(item.ImagePng, out w, out h, out bgra))
                return null;
            return ClipboardImage.ToFrozenBgra32(bgra, w, h, 96, 96);
        }

        private void ViewerClose_Click(object sender, RoutedEventArgs e)
        {
            ViewerOverlay.Visibility = Visibility.Collapsed;
            ViewerImage.Source = null;
            ViewerText.Text = "";
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
            GcHelper.CollectSafely();
        }

        private void PasteItem(ClipItem item)
        {
            try
            {
                if (item.IsImage)
                {
                    BitmapSource toWrite = DecodeOriginal(item) ?? item.Thumb;
                    if (toWrite == null) return;
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

        private bool FilterItem(object obj)
        {
            if (!(obj is ClipItem item)) return false;
            if (string.IsNullOrEmpty(SearchBox.Text)) return true;
            var hay = item.IsImage ? "[图片]" : item.Text;
            return hay != null && hay.IndexOf(SearchBox.Text, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
