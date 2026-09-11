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
    public partial class NotesView : UserControl, IModuleView, ILeaveGuard, IParkableResources
    {
        public static string PendingOpenUid;

        private readonly List<NoteRow> _rows = new List<NoteRow>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private bool _loading;
        private bool _drawerMax = true;
        private bool _skipListClick;
        private string _sortKey = "updated";
        private bool _sortAsc;
        private const double DrawerRatio = 0.90;

        private const string LockedMsg = "已锁定，只能查看。取消勾选「锁定」后才能编辑或删除。";

        public NotesView()
        {
            InitializeComponent();
            UpdateSortHeaders();
            SizeChanged += (s, e) => ApplyDrawerSize();
            DrawerEditor.Saved += (s, e) => RefreshListKeep();
            DrawerEditor.CloseRequested += (s, e) =>
            {
                HideDrawer();
                RefreshList();
            };
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
            if (DrawerHost != null && DrawerHost.Visibility == Visibility.Visible
                && DrawerEditor != null && DrawerEditor.AutoSaveEnabled)
                DrawerEditor.FlushNow();
            if (DrawerEditor != null) DrawerEditor.ReleaseMdPreview();
            NoteStore.Flush();
        }

        public bool CanLeave()
        {
            return CloseDrawer();
        }

        public bool HasUnsavedChanges()
        {
            if (DrawerHost == null || DrawerHost.Visibility != Visibility.Visible) return false;
            if (DrawerEditor != null && DrawerEditor.AutoSaveEnabled) return false;
            return DrawerEditor != null && DrawerEditor.IsDirty();
        }

        public void ParkHeavyResources()
        {
            if (DrawerEditor != null) DrawerEditor.ReleaseMdPreview();
        }

        public void UnparkHeavyResources()
        {
            if (DrawerHost != null && DrawerHost.Visibility == Visibility.Visible && DrawerEditor != null)
                DrawerEditor.RestoreMdPreview();
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
            NewNote(NoteKind.Rich, SelectedParentUid());
        }

        private void NewMd_Click(object sender, RoutedEventArgs e)
        {
            NewNote(NoteKind.Markdown, SelectedParentUid());
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            NewFolder(SelectedParentUid());
        }

        private string SelectedParentUid()
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return "";
            return row.IsFolder ? row.Uid : (row.ParentUid ?? "");
        }

        private void NewNote(int kind, string parentUid)
        {
            if (!CloseDrawer()) return;
            parentUid = parentUid ?? "";
            if (NoteStore.IsLocked(parentUid))
            {
                MsgText.Text = LockedMsg;
                return;
            }
            if (!NoteStore.CanAddUnder(parentUid))
            {
                MsgText.Text = "无法再新增：数量或层级已达上限。";
                return;
            }
            string title = NoteStore.UniqueTitle(parentUid, NoteKind.DefaultTitle(kind), null);
            var item = NoteItem.CreateNew(kind, title);
            item.ParentUid = parentUid;
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
            ExpandParent(parentUid);
            RefreshList();
            OpenDrawer(item.Uid);
        }

        private void NewFolder(string parentUid)
        {
            if (!CloseDrawer()) return;
            parentUid = parentUid ?? "";
            if (NoteStore.IsLocked(parentUid))
            {
                MsgText.Text = LockedMsg;
                return;
            }
            if (!NoteStore.CanAddUnder(parentUid))
            {
                MsgText.Text = "无法再新增：数量或层级已达上限。";
                return;
            }
            string title = NoteStore.UniqueTitle(parentUid, NoteKind.DefaultFolderTitle, null);
            var item = NoteItem.CreateFolder(title, parentUid);
            NoteStore.Add(item);
            ExpandParent(parentUid);
            RefreshList();
            OpenDrawer(item.Uid);
        }

        private void ExpandParent(string parentUid)
        {
            if (string.IsNullOrEmpty(parentUid)) return;
            _collapsed.Remove(parentUid);
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
            var flat = NoteTree.Flatten(items, _collapsed, q, filter, _sortKey, _sortAsc);
            for (int i = 0; i < flat.Count; i++)
                _rows.Add(NoteRow.From(flat[i]));
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
            RefreshList();
        }

        private void UpdateSortHeaders()
        {
            var view = NoteList != null ? NoteList.View as GridView : null;
            if (view == null || view.Columns.Count < 5) return;
            view.Columns[0].Header = SortTitle("置顶", "pin");
            view.Columns[1].Header = SortTitle("标题", "title");
            view.Columns[2].Header = SortTitle("创建时间", "created");
            view.Columns[3].Header = SortTitle("修改时间", "updated");
            view.Columns[4].Header = SortTitle("类型", "kind");
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
            double[] min = { 52, 200, 136, 136, 88 };
            double need = 0;
            for (int i = 0; i < 5; i++)
                need += min[i];
            if (w < need)
            {
                double scale = w / need;
                double used = 0;
                for (int i = 0; i < 4; i++)
                {
                    double wi = Math.Floor(min[i] * scale);
                    if (wi < 1) wi = 1;
                    gv.Columns[i].Width = wi;
                    used += wi;
                }
                double last = w - used;
                gv.Columns[4].Width = last < 1 ? 1 : last;
                return;
            }
            double extra = w - need;
            gv.Columns[0].Width = min[0];
            gv.Columns[1].Width = min[1] + extra;
            gv.Columns[2].Width = min[2];
            gv.Columns[3].Width = min[3];
            gv.Columns[4].Width = min[4];
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

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            SkipClick();
            var fe = sender as FrameworkElement;
            var row = fe == null ? null : fe.DataContext as NoteRow;
            if (row == null || !row.IsFolder) return;
            if (_collapsed.Contains(row.Uid))
                _collapsed.Remove(row.Uid);
            else
                _collapsed.Add(row.Uid);
            RefreshList();
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            var menu = sender as ContextMenu;
            if (menu == null) return;
            bool has = row != null;
            bool folder = has && row.IsFolder;
            bool pin = has && row.Pinned;
            bool locked = has && row.Locked;
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var mi = menu.Items[i] as MenuItem;
                if (mi == null) continue;
                string h = mi.Header as string;
                mi.IsEnabled = has;
                if (h == "移动" || h == "导出")
                    mi.Visibility = has && !folder ? Visibility.Visible : Visibility.Collapsed;
                if (h == "新增普通笔记" || h == "新增 Markdown" || h == "新增目录")
                {
                    mi.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
                    mi.IsEnabled = folder && !locked;
                }
                if (h == "置顶") mi.Visibility = has && !pin ? Visibility.Visible : Visibility.Collapsed;
                if (h == "取消置顶") mi.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
                if (h == "删除" || h == "移动" || h == "置顶" || h == "取消置顶")
                    mi.IsEnabled = has && !locked;
            }
        }

        private void ListMenu_Closed(object sender, RoutedEventArgs e)
        {
            _skipListClick = true;
            Dispatcher.BeginInvoke(new Action(delegate { _skipListClick = false; }), DispatcherPriority.Input);
        }

        private void MenuMove_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null || row.IsFolder) return;
            if (row.Locked)
            {
                MsgText.Text = LockedMsg;
                return;
            }
            SkipClick();
            if (!CloseDrawer()) return;
            string parent;
            if (!NoteMoveWindow.TryPick(Window.GetWindow(this), NoteStore.Snapshot(), row.ParentUid, out parent))
                return;
            if (parent == (row.ParentUid ?? ""))
            {
                MsgText.Text = "未移动";
                return;
            }
            string err;
            if (!NoteStore.TryMove(row.Uid, parent, out err))
            {
                MsgText.Text = err;
                return;
            }
            ExpandParent(parent);
            RefreshList();
            SelectUid(row.Uid);
            MsgText.Text = "已移动";
        }

        private void MenuAddRich_Click(object sender, RoutedEventArgs e)
        {
            AddChildNote(NoteKind.Rich);
        }

        private void MenuAddMd_Click(object sender, RoutedEventArgs e)
        {
            AddChildNote(NoteKind.Markdown);
        }

        private void MenuAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null || !row.IsFolder) return;
            SkipClick();
            ExpandParent(row.Uid);
            NewFolder(row.Uid);
        }

        private void AddChildNote(int kind)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null || !row.IsFolder) return;
            SkipClick();
            ExpandParent(row.Uid);
            NewNote(kind, row.Uid);
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
            if (row.Locked)
            {
                MsgText.Text = LockedMsg;
                return;
            }
            SkipClick();
            NoteStore.SetPinned(row.Uid, true);
            RefreshList();
        }

        private void MenuUnpin_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            if (row.Locked)
            {
                MsgText.Text = LockedMsg;
                return;
            }
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
            string err;
            string uid = NoteStore.TryDuplicate(row.Uid, out err);
            if (uid == null)
            {
                MsgText.Text = err;
                return;
            }
            ExpandParent(src.ParentUid);
            RefreshList();
            SelectUid(uid);
            MsgText.Text = "已复制";
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            var row = NoteList.SelectedItem as NoteRow;
            if (row == null) return;
            SkipClick();
            var it = NoteStore.GetByUid(row.Uid);
            if (it == null || it.IsFolder) return;
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
            if (NoteStore.SubtreeHasLock(row.Uid))
            {
                MsgText.Text = row.IsFolder
                    ? "目录或其下有锁定项，不能删除。取消锁定后再删。"
                    : LockedMsg;
                return;
            }
            string detail = row.Title;
            if (row.IsFolder)
            {
                int n = NoteStore.CountDescendants(row.Uid);
                if (n > 0)
                    detail = row.Title + "\n\n将同时删除其下 " + n + " 项。";
            }
            if (!ConfirmHelper.Delete(detail)) return;
            string current = DrawerEditor.CurrentUid;
            bool inDrawer = DrawerHost.Visibility == Visibility.Visible && !string.IsNullOrEmpty(current)
                && (current == row.Uid || NoteTree.SubtreeUids(NoteStore.Snapshot(), row.Uid).Contains(current));
            if (inDrawer)
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
            DrawerTitle.Text = item.IsFolder ? "目录" : (NoteKind.Label(item.Kind) + "笔记");
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
            if (DrawerEditor != null) DrawerEditor.ReleaseMdPreview();
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
            public string ParentUid { get; set; }
            public string Title { get; set; }
            public int Kind { get; set; }
            public string KindText { get; set; }
            public bool Pinned { get; set; }
            public string PinText { get; set; }
            public bool IsFolder { get; set; }
            public bool Locked { get; set; }
            public Thickness Indent { get; set; }
            public Visibility ExpanderVisibility { get; set; }
            public string ExpanderGlyph { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedText { get; set; }
            public DateTime UpdatedAt { get; set; }
            public string UpdatedText { get; set; }

            public static NoteRow From(NoteFlatRow flat)
            {
                var it = flat.Item;
                bool folder = it.IsFolder;
                return new NoteRow
                {
                    Uid = it.Uid,
                    ParentUid = it.ParentUid ?? "",
                    Title = it.Title ?? "",
                    Kind = it.Kind,
                    KindText = folder ? "目录" : NoteKind.Label(it.Kind),
                    Pinned = it.Pinned,
                    PinText = it.Pinned ? "置顶" : "",
                    IsFolder = folder,
                    Locked = it.ReadOnly,
                    Indent = new Thickness(flat.Depth * 16, 0, 0, 0),
                    ExpanderVisibility = folder && flat.HasChildren ? Visibility.Visible : Visibility.Collapsed,
                    ExpanderGlyph = flat.Expanded ? "−" : "+",
                    CreatedAt = it.CreatedAt,
                    CreatedText = it.CreatedAt == default(DateTime) ? "" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    UpdatedAt = it.UpdatedAt,
                    UpdatedText = it.UpdatedAt == default(DateTime) ? "" : it.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                };
            }
        }
    }
}
