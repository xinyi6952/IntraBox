using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

        private void PasteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.SelectedItem is ClipItem item) PasteItem(item);
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

        private void ViewMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowViewer(HistoryList.SelectedItem as ClipItem);
        }

        private void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void ShowViewer(ClipItem item)
        {
            if (item == null) return;
            if (item.IsImage && item.Thumb != null)
            {
                ViewerTitle.Text = (item.Preview ?? "[图片]")
                    + "  " + item.Time.ToString("yyyy-MM-dd HH:mm:ss")
                    + "  （历史仅保留缩略图）";
                ViewerImage.Source = item.Thumb;
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
                if (item.IsImage && item.Thumb != null)
                {
                    ClipboardStore.SuppressImageCapture = true;
                    try
                    {
                        Clipboard.SetImage(item.Thumb);
                        ClipboardMonitor.MarkPasted(item);
                    }
                    finally
                    {
                        ClipboardStore.SuppressImageCapture = false;
                    }
                    MsgText.Text = "已写回剪贴板（图片为缩略图）";
                    return;
                }
                else if (!item.IsImage)
                {
                    Clipboard.SetText(item.Text);
                    ClipboardMonitor.MarkPasted(item);
                }
                MsgText.Text = "已写回剪贴板";
            }
            catch (Exception ex)
            {
                MsgText.Text = "写回失败：" + ex.Message;
            }
        }

        private void PinBtn_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.SelectedItem is ClipItem item)
            {
                item.IsPinned = !item.IsPinned;
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmHelper.Action("确定清空未固定的剪贴板历史？此操作不可撤销。", "清空确认"))
                return;
            for (int i = ClipboardStore.Items.Count - 1; i >= 0; i--)
            {
                if (!ClipboardStore.Items[i].IsPinned) ClipboardStore.Items.RemoveAt(i);
            }
            GcHelper.CollectSafely();
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
