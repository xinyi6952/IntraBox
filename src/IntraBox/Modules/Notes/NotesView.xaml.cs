using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Notes
{
    public partial class NotesView : UserControl, IModuleView, ILeaveGuard
    {
        public static string PendingOpenUid;

        private readonly List<NoteRow> _rows = new List<NoteRow>();
        private bool _loading;
        private bool _drawerMax = true;
        private bool _skipListClick;
        private string _sortKey = "updated";
        private bool _sortAsc;
        private const double DrawerRatio = 0.90;

        public NotesView()
        {
            InitializeComponent();
            UpdateSortHeaders();
            SizeChanged += (s, e) => ApplyDrawerSize();
            DrawerEditor.Saved += (s, e) => RefreshListKeep();
        }

        public void OnActivated()
        {
            RefreshList();
            OpenPending();
        }

        public void OpenPending()
        {
            string uid = PendingOpenUid;
            PendingOpenUid = null;
            if (string.IsNullOrEmpty(uid)) return;
            OpenDrawer(uid);
        }

        public void OnDeactivated()
        {
            NoteStore.Flush();
        }

        public bool CanLeave()
        {
            return CloseDrawer();
        }

        private void NewBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = NewBtn.ContextMenu;
            if (menu == null) return;
            menu.PlacementTarget = NewBtn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void NewRich_Click(object sender, RoutedEventArgs e)
        {
            NewNote(NoteKind.Rich);
        }

        private void NewMd_Click(object sender, RoutedEventArgs e)
        {
            NewNote(NoteKind.Markdown);
        }

        private void NewNote(int kind)
        {
            if (!CloseDrawer()) return;
            var item = NoteItem.CreateNew(kind, NoteStore.UniqueTitle(kind, null));
            NoteStore.Add(item);
            if (kind == NoteKind.Markdown)
            {
                Directory.CreateDirectory(DataPaths.NoteItemDir(item.Uid));
                File.WriteAllText(DataPaths.NoteBodyMdPath(item.Uid), "", Encoding.UTF8);
            }
            else
            {
                Directory.CreateDirectory(DataPaths.NoteItemDir(item.Uid));
                NoteRichText.SaveDocument(NoteRichText.CreateEmptyDocument(), item.Uid);
            }
            RefreshList();
            OpenDrawer(item.Uid);
        }

        private int _kindFilter;

        private void Kind_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _loading) return;
            int next = KindCombo != null ? KindCombo.SelectedIndex : 0;
            if (next < 0) next = 0;
            if (next == _kindFilter) return;
            if (!CloseDrawer())
            {
                _loading = true;
                KindCombo.SelectedIndex = _kindFilter;
                _loading = false;
                return;
            }
            _kindFilter = next;
            RefreshList();
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private int CurrentFilter()
        {
            return _kindFilter;
        }

        private void RefreshListKeep()
        {
            string keep = null;
            var selected = NoteList.SelectedItem as NoteRow;
            if (selected != null) keep = selected.Uid;
            if (string.IsNullOrEmpty(keep)) keep = DrawerEditor.CurrentUid;
            RefreshList();
            if (!string.IsNullOrEmpty(keep)) SelectUid(keep);
        }

        private void RefreshList()
        {
            string keep = null;
            var selected = NoteList.SelectedItem as NoteRow;
            if (selected != null) keep = selected.Uid;
            _rows.Clear();
            var items = NoteStore.Snapshot();
            int filter = CurrentFilter();
            string q = SearchBox != null ? (SearchBox.Text ?? "").Trim() : "";
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (filter == 1 && it.Kind != NoteKind.Rich) continue;
                if (filter == 2 && it.Kind != NoteKind.Markdown) continue;
                if (q.Length > 0 && (it.Title == null || it.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                _rows.Add(NoteRow.From(it));
            }
            SortRows();
            _loading = true;
            NoteList.ItemsSource = null;
            NoteList.ItemsSource = _rows;
            _loading = false;
            UpdateSortHeaders();
            FitListColumns();
            if (!string.IsNullOrEmpty(keep)) SelectUid(keep);
        }

        private void SelectUid(string uid)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Uid == uid)
                {
                    _loading = true;
                    NoteList.SelectedItem = _rows[i];
                    _loading = false;
                    return;
                }
            }
        }

        private void Header_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null)
            {
                var d = e.OriginalSource as DependencyObject;
                while (d != null && !(d is GridViewColumnHeader))
                    d = VisualTreeHelper.GetParent(d);
                header = d as GridViewColumnHeader;
            }
            if (header == null || header.Role == GridViewColumnHeaderRole.Padding) return;
            string key = SortKeyFromHeader(header.Content as string);
            if (string.IsNullOrEmpty(key)) return;
            if (_sortKey == key) _sortAsc = !_sortAsc;
            else { _sortKey = key; _sortAsc = false; }
            SortRows();
            _loading = true;
            NoteList.ItemsSource = null;
            NoteList.ItemsSource = _rows;
            _loading = false;
            UpdateSortHeaders();
            FitListColumns();
        }

        private void SortRows()
        {
            _rows.Sort(CompareRows);
        }

        private int CompareRows(NoteRow a, NoteRow b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int pin = b.Pinned.CompareTo(a.Pinned);
            if (pin != 0) return pin;
            int c;
            if (_sortKey == "kind")
                c = a.Kind.CompareTo(b.Kind);
            else if (_sortKey == "title")
                c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            else if (_sortKey == "created")
                c = a.CreatedAt.CompareTo(b.CreatedAt);
            else if (_sortKey == "pin")
                c = a.Pinned.CompareTo(b.Pinned);
            else
                c = a.UpdatedAt.CompareTo(b.UpdatedAt);
            if (!_sortAsc) c = -c;
            if (c == 0) c = b.UpdatedAt.CompareTo(a.UpdatedAt);
            return c;
        }

        private void UpdateSortHeaders()
        {
            var view = NoteList != null ? NoteList.View as GridView : null;
            if (view == null || view.Columns.Count < 5) return;
            view.Columns[0].Header = SortTitle("置顶", "pin");
            view.Columns[1].Header = SortTitle("类型", "kind");
            view.Columns[2].Header = SortTitle("标题", "title");
            view.Columns[3].Header = SortTitle("创建时间", "created");
            view.Columns[4].Header = SortTitle("修改时间", "updated");
        }

        private static string SortKeyFromHeader(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            if (content.StartsWith("置顶", StringComparison.Ordinal)) return "pin";
            if (content.StartsWith("类型", StringComparison.Ordinal)) return "kind";
            if (content.StartsWith("标题", StringComparison.Ordinal)) return "title";
            if (content.StartsWith("创建时间", StringComparison.Ordinal)) return "created";
            if (content.StartsWith("修改时间", StringComparison.Ordinal)) return "updated";
            return null;
        }

        private string SortTitle(string title, string key)
        {
            if (_sortKey != key) return title;
            return title + (_sortAsc ? " ↑" : " ↓");
        }

        private void NoteList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitListColumns();
        }

        private void FitListColumns()
        {
            if (NoteList == null) return;
            var gv = NoteList.View as GridView;
            if (gv == null || gv.Columns.Count < 5) return;
            double w = NoteList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
            if (w < 200) return;
            double[] min = { 52, 80, 140, 128, 128 };
            double need = 52 + 80 + 140 + 128 + 128;
            if (w <= need)
            {
                for (int i = 0; i < 5; i++)
                    gv.Columns[i].Width = min[i];
                return;
            }
            double extra = w - need;
            gv.Columns[0].Width = min[0];
            gv.Columns[1].Width = min[1];
            gv.Columns[2].Width = min[2] + extra * 0.55;
            gv.Columns[3].Width = min[3] + extra * 0.22;
            gv.Columns[4].Width = min[4] + extra * 0.23;
        }

        private void NoteList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_loading || _skipListClick) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ListViewItem))
            {
                try { src = VisualTreeHelper.GetParent(src); }
                catch { break; }
            }
            if (src == null) return;
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void NoteList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MenuEdit_Click(sender, e);
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            var menu = sender as ContextMenu;
            if (menu == null) return;
            bool has = row != null;
            bool pin = has && row.Pinned;
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var mi = menu.Items[i] as MenuItem;
                if (mi == null) continue;
                string h = mi.Header as string;
                mi.IsEnabled = has;
                if (h == "置顶") mi.Visibility = pin ? Visibility.Collapsed : Visibility.Visible;
                if (h == "取消置顶") mi.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ListMenu_Closed(object sender, RoutedEventArgs e)
        {
            _skipListClick = true;
            Dispatcher.BeginInvoke(new Action(delegate { _skipListClick = false; }), DispatcherPriority.Input);
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void MenuPin_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            SkipClick();
            NoteStore.SetPinned(row.Uid, true);
            RefreshList();
        }

        private void MenuUnpin_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            SkipClick();
            NoteStore.SetPinned(row.Uid, false);
            RefreshList();
        }

        private void MenuDup_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            SkipClick();
            if (!CloseDrawer()) return;
            var src = NoteStore.GetByUid(row.Uid);
            if (src == null) return;
            var copy = NoteItem.CreateNew(src.Kind, src.Title + " 副本");
            NoteStore.Add(copy);
            try
            {
                CopyDir(DataPaths.NoteItemDir(src.Uid), DataPaths.NoteItemDir(copy.Uid));
            }
            catch { }
            RefreshList();
            MsgText.Text = "已复制";
        }

        private static void CopyDir(string src, string dest)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dest);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
                File.Copy(files[i], Path.Combine(dest, Path.GetFileName(files[i])), true);
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
                CopyDir(dirs[i], Path.Combine(dest, Path.GetFileName(dirs[i])));
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            SkipClick();
            var it = NoteStore.GetByUid(row.Uid);
            if (it == null) return;
            var dlg = new SaveFileDialog();
            if (it.Kind == NoteKind.Markdown)
            {
                dlg.Filter = "Markdown|*.md";
                dlg.FileName = SafeName(it.Title) + ".md";
            }
            else
            {
                dlg.Filter = "文本|*.txt";
                dlg.FileName = SafeName(it.Title) + ".txt";
            }
            if (dlg.ShowDialog() != true) return;
            try
            {
                if (it.Kind == NoteKind.Markdown)
                {
                    string src = DataPaths.NoteBodyMdPath(it.Uid);
                    string text = File.Exists(src) ? File.ReadAllText(src, Encoding.UTF8) : "";
                    File.WriteAllText(dlg.FileName, text, Encoding.UTF8);
                }
                else
                {
                    var tmp = new RichTextBox();
                    NoteRichText.LoadInto(tmp, it.Uid);
                    File.WriteAllText(dlg.FileName, NoteRichText.ToPlain(tmp.Document), Encoding.UTF8);
                }
                MsgText.Text = "已导出";
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SafeName(string title)
        {
            if (string.IsNullOrEmpty(title)) return "笔记";
            foreach (char c in Path.GetInvalidFileNameChars())
                title = title.Replace(c, '_');
            return title;
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            if (!ConfirmHelper.Delete(row.Title)) return;
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == row.Uid)
                HideDrawer();
            else if (!CloseDrawer())
                return;
            NoteStore.Delete(row.Uid);
            RefreshList();
            MsgText.Text = "已删除";
        }

        private void SkipClick()
        {
            _skipListClick = true;
            Dispatcher.BeginInvoke(new Action(delegate { _skipListClick = false; }), DispatcherPriority.Input);
        }

        private void OpenDrawer(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == uid)
                return;
            if (!CloseDrawer()) return;
            var item = NoteStore.GetByUid(uid);
            if (item == null) return;
            DrawerTitle.Text = NoteKind.Label(item.Kind) + "笔记";
            DrawerEditor.LoadItem(item);
            DrawerHost.Visibility = Visibility.Visible;
            _drawerMax = true;
            SetDrawerMaximized(true);
        }

        private bool CloseDrawer()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return true;
            if (DrawerEditor.AutoSaveEnabled)
            {
                DrawerEditor.FlushNow();
                HideDrawer();
                RefreshList();
                return true;
            }
            if (!DrawerEditor.IsDirty())
            {
                HideDrawer();
                return true;
            }
            var r = ConfirmHelper.Unsaved("当前笔记有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
                DrawerEditor.FlushNow();
            HideDrawer();
            RefreshList();
            return true;
        }

        private void HideDrawer()
        {
            DrawerHost.Visibility = Visibility.Collapsed;
            ApplyDrawerSize();
        }

        private void DrawerMax_Click(object sender, RoutedEventArgs e)
        {
            SetDrawerMaximized(!_drawerMax);
        }

        private void SetDrawerMaximized(bool on)
        {
            _drawerMax = on;
            ApplyDrawerSize();
            if (IconExpand != null)
                IconExpand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null)
                IconRestore.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (DrawerMaxBtn != null)
                DrawerMaxBtn.ToolTip = on ? "还原" : "最大化";
        }

        private void ApplyDrawerSize()
        {
            DrawerLayout.Apply(DrawerPanel, DrawerMask, DrawerHost, _drawerMax, DrawerRatio);
        }

        private void DrawerMask_Click(object sender, MouseButtonEventArgs e)
        {
            CloseDrawer();
        }

        private void DrawerClose_Click(object sender, RoutedEventArgs e)
        {
            CloseDrawer();
        }

        private sealed class NoteRow
        {
            public string Uid { get; set; }
            public string Title { get; set; }
            public int Kind { get; set; }
            public string KindText { get; set; }
            public bool Pinned { get; set; }
            public string PinText { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedText { get; set; }
            public DateTime UpdatedAt { get; set; }
            public string UpdatedText { get; set; }

            public static NoteRow From(NoteItem it)
            {
                return new NoteRow
                {
                    Uid = it.Uid,
                    Title = it.Title ?? "",
                    Kind = it.Kind,
                    KindText = NoteKind.Label(it.Kind),
                    Pinned = it.Pinned,
                    PinText = it.Pinned ? "置顶" : "",
                    CreatedAt = it.CreatedAt,
                    CreatedText = it.CreatedAt == default(DateTime) ? "" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    UpdatedAt = it.UpdatedAt,
                    UpdatedText = it.UpdatedAt == default(DateTime) ? "" : it.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                };
            }
        }
    }
}
