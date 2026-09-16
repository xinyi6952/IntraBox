using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Core;

namespace IntraBox.Controls
{
    /// <summary>可输入筛选的下拉（自绘，不依赖全局 ComboBox 模板）。</summary>
    public partial class SearchCombo : UserControl
    {
        public sealed class Item
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public string Extra { get; set; }
        }

        private readonly List<Item> _all = new List<Item>();
        private string _id = "";
        private string _text = "";
        private bool _suppress;
        private bool _filterOnType;

        public SearchCombo()
        {
            InitializeComponent();
            Loaded += SearchCombo_Loaded;
            Unloaded += SearchCombo_Unloaded;
        }

        public string SelectedId
        {
            get { return _id ?? ""; }
        }

        public string SelectedText
        {
            get { return _text ?? ""; }
        }

        public void SetItems(IList<Item> items)
        {
            _all.Clear();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it == null || string.IsNullOrEmpty(it.Id)) continue;
                    _all.Add(it);
                }
            }
            ShowSelected();
        }

        public void SelectId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _id = _all[i].Id;
                    _text = _all[i].Text ?? "";
                    ShowSelected();
                    return;
                }
            }
        }

        private Window _hostWindow;

        private void SearchCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hostWindow != null)
                _hostWindow.PreviewMouseDown -= Window_PreviewMouseDown;
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
                _hostWindow.PreviewMouseDown += Window_PreviewMouseDown;
            ShowSelected();
        }

        private void SearchCombo_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_hostWindow != null)
            {
                _hostWindow.PreviewMouseDown -= Window_PreviewMouseDown;
                _hostWindow = null;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!DropPopup.IsOpen) return;
            var src = e.OriginalSource as DependencyObject;
            if (src != null && (IsDescendant(this, src) || IsDescendant(DropPopup.Child, src)))
                return;
            CloseDrop(true);
        }

        private static bool IsDescendant(DependencyObject root, DependencyObject node)
        {
            if (root == null || node == null) return false;
            var n = node;
            while (n != null)
            {
                if (n == root) return true;
                var popup = n as Popup;
                if (popup != null && popup.Child != null && IsDescendant(root, popup.Child))
                    return true;
                n = VisualTreeHelper.GetParent(n);
            }
            return false;
        }

        private void FillList(string query)
        {
            var scored = new List<KeyValuePair<int, Item>>();
            for (int i = 0; i < _all.Count; i++)
            {
                var it = _all[i];
                int s = SearchTextHelper.Score(query, it.Text, it.Extra);
                if (s <= 0) continue;
                scored.Add(new KeyValuePair<int, Item>(s, it));
            }
            if (!string.IsNullOrWhiteSpace(query))
                scored.Sort(CompareScore);
            var view = new List<Item>(scored.Count);
            for (int i = 0; i < scored.Count; i++)
                view.Add(scored[i].Value);
            List.ItemsSource = view;
        }

        private static int CompareScore(KeyValuePair<int, Item> a, KeyValuePair<int, Item> b)
        {
            int c = b.Key.CompareTo(a.Key);
            if (c != 0) return c;
            return string.Compare(a.Value.Text, b.Value.Text, StringComparison.OrdinalIgnoreCase);
        }

        private void ShowSelected()
        {
            _suppress = true;
            _filterOnType = false;
            Edit.Text = _text ?? "";
            _suppress = false;
        }

        private string FilterQuery()
        {
            string q = Edit.Text ?? "";
            if (string.Equals(q, _text, StringComparison.Ordinal)) return "";
            return q;
        }

        private void OpenDrop(bool forceAll)
        {
            FillList(forceAll ? "" : FilterQuery());
            DropPopup.IsOpen = true;
        }

        private void CloseDrop(bool restore)
        {
            DropPopup.IsOpen = false;
            _filterOnType = false;
            if (restore) ShowSelected();
        }

        private void Pick(Item it)
        {
            if (it == null) return;
            _id = it.Id ?? "";
            _text = it.Text ?? "";
            CloseDrop(true);
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (DropPopup.IsOpen) CloseDrop(true);
            else
            {
                OpenDrop(true);
                Edit.Focus();
            }
        }

        private void Edit_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _filterOnType = true;
            Edit.SelectAll();
        }

        private void Edit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (DropPopup.IsOpen) return;
                if (Edit.IsKeyboardFocusWithin) return;
                ShowSelected();
            }), DispatcherPriority.Input);
        }

        private void Edit_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress) return;
            if (!_filterOnType) return;
            OpenDrop(false);
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ListBoxItem))
                src = VisualTreeHelper.GetParent(src);
            var row = src as ListBoxItem;
            if (row == null) return;
            Pick(row.DataContext as Item);
            e.Handled = true;
        }

        private void Edit_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseDrop(true);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                if (!DropPopup.IsOpen) OpenDrop(true);
                if (List.Items.Count > 0 && List.SelectedIndex < 0)
                    List.SelectedIndex = 0;
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter) return;
            Item pick = List.SelectedItem as Item;
            if (pick == null && List.Items.Count == 1)
                pick = List.Items[0] as Item;
            if (pick != null) Pick(pick);
            else CloseDrop(true);
            e.Handled = true;
        }
    }
}
