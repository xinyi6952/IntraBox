using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace IntraBox.Modules.Launcher
{
    public partial class LauncherView : UserControl, IModuleView, ILeaveGuard
    {
        private readonly List<FavRow> _rows = new List<FavRow>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private bool _loading;
        private bool _drawerMax;
        private bool _skipListClick;
        private bool _isNew;
        private bool _editCategory;
        private string _editUid;
        private string _parentUid = "";
        private string _loadedName = "";
        private string _loadedKind = LauncherTarget.KindApp;
        private string _loadedTarget = "";
        private string _loadedOpenWith = "";
        private bool _loadedPinned;
        private bool _loadedConfirmOpen = true;
        private const double DrawerRatio = 0.70;

        public LauncherView()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ApplyDrawerSize();
        }

        public void OnActivated()
        {
            string hk = HotkeyService.Instance != null
                ? HotkeyService.Instance.DisplayTextLauncher
                : "Ctrl+Alt+L";
            if (SearchBox != null)
                SearchBox.ToolTip = "搜索名称或目标。热键 " + hk + " 呼出浮层（托盘隐藏仍有效，可在设置中更改）。";
            RefreshList();
            MsgText.Text = "";
        }

        public void OnDeactivated()
        {
            LauncherStore.Flush();
        }

        public bool CanLeave()
        {
            return CloseDrawer();
        }

        private void NewFav_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            OpenDrawerNew(false, SelectedParentUid());
        }

        private void NewCat_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            OpenDrawerNew(true, SelectedParentUid());
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _loading) return;
            RefreshList();
        }

        private string SelectedParentUid()
        {
            var row = FavList.SelectedItem as FavRow;
            if (row == null) return "";
            return row.IsCategory ? row.Uid : (row.ParentUid ?? "");
        }

        private string SelectedUid()
        {
            var row = FavList.SelectedItem as FavRow;
            return row == null ? null : row.Uid;
        }

        private void RefreshList()
        {
            string keep = SelectedUid();
            if (string.IsNullOrEmpty(keep) && !_isNew) keep = _editUid;
            _rows.Clear();
            var nodes = LauncherStore.SnapshotNodes();
            string q = SearchBox != null ? (SearchBox.Text ?? "").Trim() : "";
            var flat = LauncherTree.Flatten(nodes, _collapsed, q);
            for (int i = 0; i < flat.Count; i++)
                _rows.Add(FavRow.From(flat[i]));
            _loading = true;
            FavList.ItemsSource = null;
            FavList.ItemsSource = _rows;
            _loading = false;
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
                    FavList.SelectedItem = _rows[i];
                    _loading = false;
                    return;
                }
            }
        }

        private void ExpandParent(string parentUid)
        {
            if (string.IsNullOrEmpty(parentUid)) return;
            _collapsed.Remove(parentUid);
        }

        private void FavList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitListColumns();
        }

        private void FitListColumns()
        {
            if (FavList == null) return;
            var gv = FavList.View as GridView;
            if (gv == null || gv.Columns.Count < 6) return;
            double w = FavList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
            if (w < 200) return;
            double[] min = { 160, 72, 120, 88, 48, 160 };
            double need = 0;
            for (int i = 0; i < min.Length; i++)
                need += min[i];
            if (w <= need)
            {
                for (int i = 0; i < 6; i++)
                    gv.Columns[i].Width = min[i];
                return;
            }
            double extra = w - need;
            gv.Columns[0].Width = min[0] + extra * 0.40;
            gv.Columns[1].Width = min[1];
            gv.Columns[2].Width = min[2] + extra * 0.40;
            gv.Columns[3].Width = min[3] + extra * 0.20;
            gv.Columns[4].Width = min[4];
            gv.Columns[5].Width = min[5];
        }

        private void FavList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_loading || _skipListClick) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ListViewItem))
            {
                try { src = VisualTreeHelper.GetParent(src); }
                catch { break; }
            }
            if (src == null) return;
            var row = FavList.SelectedItem as FavRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void FavList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = FavList.SelectedItem as FavRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            SkipClick();
            var row = RowFromSender(sender);
            if (row == null || !row.IsCategory) return;
            if (_collapsed.Contains(row.Uid))
                _collapsed.Remove(row.Uid);
            else
                _collapsed.Add(row.Uid);
            RefreshList();
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = FavList.SelectedItem as FavRow;
            var menu = sender as ContextMenu;
            if (menu == null) return;
            bool has = row != null;
            bool cat = has && row.IsCategory;
            bool pin = has && row.Pinned;
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var mi = menu.Items[i] as MenuItem;
                if (mi == null) continue;
                string h = mi.Header as string;
                mi.IsEnabled = has;
                if (h == "打开" || h == "移动")
                    mi.Visibility = has && !cat ? Visibility.Visible : Visibility.Collapsed;
                if (h == "新增收藏" || h == "新增分类")
                    mi.Visibility = cat ? Visibility.Visible : Visibility.Collapsed;
                if (h == "置顶") mi.Visibility = has && !pin ? Visibility.Visible : Visibility.Collapsed;
                if (h == "取消置顶") mi.Visibility = pin ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ListMenu_Closed(object sender, RoutedEventArgs e)
        {
            SkipClick();
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenRow(FavList.SelectedItem as FavRow);
        }

        private void MenuAddFav_Click(object sender, RoutedEventArgs e)
        {
            AddChild(FavList.SelectedItem as FavRow, false);
        }

        private void MenuAddCat_Click(object sender, RoutedEventArgs e)
        {
            AddChild(FavList.SelectedItem as FavRow, true);
        }

        private void MenuMove_Click(object sender, RoutedEventArgs e)
        {
            MoveRow(FavList.SelectedItem as FavRow);
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = FavList.SelectedItem as FavRow;
            if (row == null) return;
            SkipClick();
            OpenDrawer(row.Uid);
        }

        private void MenuPin_Click(object sender, RoutedEventArgs e)
        {
            TogglePin(FavList.SelectedItem as FavRow, true);
        }

        private void MenuUnpin_Click(object sender, RoutedEventArgs e)
        {
            TogglePin(FavList.SelectedItem as FavRow, false);
        }

        private void MenuDup_Click(object sender, RoutedEventArgs e)
        {
            DuplicateRow(FavList.SelectedItem as FavRow);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteRow(FavList.SelectedItem as FavRow);
        }

        private void OpOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenRow(RowFromSender(sender));
        }

        private void OpMove_Click(object sender, RoutedEventArgs e)
        {
            MoveRow(RowFromSender(sender));
        }

        private void OpDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteRow(RowFromSender(sender));
        }

        private void OpenRow(FavRow row)
        {
            if (row == null || row.IsCategory) return;
            SkipClick();
            var node = LauncherStore.GetFavorite(row.Uid);
            if (node == null) return;
            var owner = Window.GetWindow(this);
            if (!LauncherLaunch.ConfirmIfNeeded(node, owner)) return;
            if (LauncherLaunch.TryOpen(node))
                LauncherStore.TouchRecent(LauncherHit.FavoriteId(node.Uid));
        }

        private void AddChild(FavRow row, bool category)
        {
            if (row == null || !row.IsCategory) return;
            SkipClick();
            if (!CloseDrawer()) return;
            ExpandParent(row.Uid);
            OpenDrawerNew(category, row.Uid);
        }

        private void TogglePin(FavRow row, bool pinned)
        {
            if (row == null) return;
            SkipClick();
            LauncherStore.SetPinned(row.Uid, pinned);
            RefreshList();
        }

        private void DuplicateRow(FavRow row)
        {
            if (row == null) return;
            SkipClick();
            if (!CloseDrawer()) return;
            string err;
            string uid = LauncherStore.TryDuplicate(row.Uid, out err);
            if (uid == null)
            {
                MsgText.Text = err;
                return;
            }
            ExpandParent(row.ParentUid);
            RefreshList();
            SelectUid(uid);
            MsgText.Text = "已复制";
        }

        private void MoveRow(FavRow row)
        {
            if (row == null || row.IsCategory) return;
            SkipClick();
            if (!CloseDrawer()) return;
            string parent;
            if (!LauncherMoveWindow.TryPick(Window.GetWindow(this), LauncherStore.SnapshotNodes(), row.ParentUid, out parent))
                return;
            if (parent == (row.ParentUid ?? ""))
            {
                MsgText.Text = "未移动";
                return;
            }
            string err;
            if (!LauncherStore.TryMove(row.Uid, parent, out err))
            {
                MsgText.Text = err;
                return;
            }
            ExpandParent(parent);
            RefreshList();
            SelectUid(row.Uid);
            MsgText.Text = "已移动";
        }

        private void DeleteRow(FavRow row)
        {
            if (row == null) return;
            SkipClick();
            string detail = row.Name;
            if (row.IsCategory)
            {
                int n = LauncherStore.CountDescendants(row.Uid);
                if (n > 0)
                    detail = row.Name + "\n\n将同时删除其下 " + n + " 项。";
            }
            if (!ConfirmHelper.Delete(detail)) return;
            if (DrawerHost.Visibility == Visibility.Visible && _editUid == row.Uid && !_isNew)
                HideDrawer();
            else if (!CloseDrawer())
                return;
            LauncherStore.Remove(row.Uid);
            RefreshList();
            MsgText.Text = "已删除";
        }

        private static FavRow RowFromSender(object sender)
        {
            var fe = sender as FrameworkElement;
            return fe == null ? null : fe.DataContext as FavRow;
        }

        private void SkipClick()
        {
            _skipListClick = true;
            Dispatcher.BeginInvoke(new Action(delegate { _skipListClick = false; }), DispatcherPriority.Input);
        }

        private void OpenDrawerNew(bool category, string parentUid)
        {
            _isNew = true;
            _editCategory = category;
            _editUid = null;
            _parentUid = parentUid ?? "";
            ExpandParent(_parentUid);
            _loading = true;
            NameBox.Text = "";
            TargetBox.Text = "";
            OpenWithBox.Text = "";
            PinCheck.IsChecked = false;
            ConfirmOpenCheck.IsChecked = true;
            SelectKind(LauncherTarget.KindApp);
            ApplyEditMode();
            _loading = false;
            RememberLoaded();
            DrawerTitle.Text = category ? "新建分类" : "新建收藏";
            SetDrawerMsg("", false);
            DrawerHost.Visibility = Visibility.Visible;
            SetDrawerMaximized(false);
            NameBox.Focus();
        }

        private void OpenDrawer(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (DrawerHost.Visibility == Visibility.Visible && !_isNew && _editUid == uid)
                return;
            if (!CloseDrawer()) return;
            var item = LauncherStore.GetNode(uid);
            if (item == null) return;
            _isNew = false;
            _editCategory = item.IsCategory;
            _editUid = uid;
            _parentUid = item.ParentUid ?? "";
            _loading = true;
            NameBox.Text = item.Name ?? "";
            TargetBox.Text = item.Target ?? "";
            OpenWithBox.Text = item.OpenWith ?? "";
            PinCheck.IsChecked = item.Pinned;
            ConfirmOpenCheck.IsChecked = item.ConfirmOpen != false;
            SelectKind(string.IsNullOrEmpty(item.Kind) ? LauncherTarget.KindApp : item.Kind);
            ApplyEditMode();
            _loading = false;
            RememberLoaded();
            DrawerTitle.Text = item.IsCategory ? "编辑分类" : "编辑收藏";
            SetDrawerMsg("", false);
            DrawerHost.Visibility = Visibility.Visible;
            SetDrawerMaximized(false);
        }

        private void ApplyEditMode()
        {
            FavFields.Visibility = _editCategory ? Visibility.Collapsed : Visibility.Visible;
            DrawerHint.Text = _editCategory
                ? "分类相当于目录，可在其下新增收藏或子分类。"
                : "程序选 .exe / .lnk / .bat；可勾选打开前二次确认（默认开）防止误触。已在运行的相同程序再开一份会再确认。内网地址须 http:// 或 https://。不支持 .cmd / .vbs / .ps1。文件可指定打开方式，留空则用系统默认。";
            UpdateKindFields();
        }

        private void SelectKind(string kind)
        {
            int idx = 0;
            if (kind == LauncherTarget.KindFolder) idx = 1;
            else if (kind == LauncherTarget.KindFile) idx = 2;
            else if (kind == LauncherTarget.KindUrl) idx = 3;
            KindCombo.SelectedIndex = idx;
        }

        private string SelectedKind()
        {
            var item = KindCombo.SelectedItem as ComboBoxItem;
            if (item == null || item.Tag == null) return LauncherTarget.KindApp;
            return item.Tag.ToString();
        }

        private void RememberLoaded()
        {
            _loadedName = NameBox.Text ?? "";
            _loadedKind = SelectedKind();
            _loadedTarget = TargetBox.Text ?? "";
            _loadedOpenWith = OpenWithBox.Text ?? "";
            _loadedPinned = PinCheck.IsChecked == true;
            _loadedConfirmOpen = ConfirmOpenCheck.IsChecked == true;
        }

        private bool IsDirty()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return false;
            if ((NameBox.Text ?? "") != _loadedName) return true;
            if ((PinCheck.IsChecked == true) != _loadedPinned) return true;
            if (_editCategory) return false;
            if (SelectedKind() != _loadedKind) return true;
            if ((TargetBox.Text ?? "") != _loadedTarget) return true;
            if ((OpenWithBox.Text ?? "") != _loadedOpenWith) return true;
            if ((ConfirmOpenCheck.IsChecked == true) != _loadedConfirmOpen) return true;
            return false;
        }

        private void Field_Changed(object sender, TextChangedEventArgs e)
        {
            Field_Changed(sender, (RoutedEventArgs)e);
        }

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            SetDrawerMsg(IsDirty() ? "未保存" : "", false);
        }

        private void Target_Changed(object sender, TextChangedEventArgs e)
        {
            Field_Changed(sender, e);
            if (!_loading) UpdateKindFields();
        }

        private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            UpdateKindFields();
            Field_Changed(sender, e);
        }

        private void UpdateKindFields()
        {
            if (OpenWithRow == null) return;
            bool file = !_editCategory && SelectedKind() == LauncherTarget.KindFile;
            bool app = !_editCategory && SelectedKind() == LauncherTarget.KindApp;
            OpenWithRow.Visibility = file ? Visibility.Visible : Visibility.Collapsed;
            if (ConfirmOpenCheck != null)
                ConfirmOpenCheck.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
            if (file && DefaultHandlerHint != null)
                DefaultHandlerHint.Text = LauncherTarget.DefaultHandlerHint(TargetBox.Text);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            string kind = SelectedKind();
            if (kind == LauncherTarget.KindUrl)
            {
                SetDrawerMsg("内网地址请直接粘贴 http:// 或 https:// 链接。", false);
                return;
            }
            if (kind == LauncherTarget.KindFolder)
            {
                using (var dlg = new WF.FolderBrowserDialog())
                {
                    dlg.Description = "选择文件夹";
                    if (dlg.ShowDialog() != WF.DialogResult.OK) return;
                    TargetBox.Text = dlg.SelectedPath;
                    if (string.IsNullOrWhiteSpace(NameBox.Text))
                        NameBox.Text = System.IO.Path.GetFileName(dlg.SelectedPath);
                }
                Field_Changed(sender, e);
                UpdateKindFields();
                return;
            }
            var ofd = new OpenFileDialog();
            ofd.CheckFileExists = true;
            if (kind == LauncherTarget.KindApp)
            {
                ofd.Filter = "程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|所有文件 (*.*)|*.*";
                ofd.Title = "选择程序";
            }
            else
            {
                ofd.Filter = "所有文件 (*.*)|*.*";
                ofd.Title = "选择文件";
            }
            if (ofd.ShowDialog() != true) return;
            TargetBox.Text = ofd.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
            Field_Changed(sender, e);
            UpdateKindFields();
        }

        private void BrowseOpenWith_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.CheckFileExists = true;
            ofd.Filter = "程序 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*";
            ofd.Title = "选择打开方式";
            if (ofd.ShowDialog() != true) return;
            OpenWithBox.Text = ofd.FileName;
            Field_Changed(sender, e);
        }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (DrawerHost.Visibility == Visibility.Visible)
                Save_Click(sender, e);
        }

        private bool SaveCurrent(out string error)
        {
            error = "";
            if (_editCategory)
            {
                if (_isNew)
                {
                    string uid = LauncherStore.TryAddCategory(_parentUid, NameBox.Text, out error);
                    if (uid == null) return false;
                    _isNew = false;
                    _editUid = uid;
                    RememberLoaded();
                    return true;
                }
                if (!LauncherStore.TryUpdateCategory(_editUid, NameBox.Text, PinCheck.IsChecked == true, out error))
                    return false;
                RememberLoaded();
                return true;
            }
            string openWith = SelectedKind() == LauncherTarget.KindFile ? OpenWithBox.Text : "";
            bool confirmOpen = ConfirmOpenCheck.IsChecked == true;
            if (_isNew)
            {
                string uid = LauncherStore.TryAddFavorite(_parentUid, NameBox.Text, SelectedKind(), TargetBox.Text, openWith, confirmOpen, out error);
                if (uid == null) return false;
                _isNew = false;
                _editUid = uid;
                RememberLoaded();
                return true;
            }
            if (!LauncherStore.TryUpdateFavorite(_editUid, NameBox.Text, SelectedKind(), TargetBox.Text, openWith, PinCheck.IsChecked == true, confirmOpen, out error))
                return false;
            RememberLoaded();
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!SaveCurrent(out err))
            {
                SetDrawerMsg(err, true);
                return;
            }
            SetDrawerMsg("已保存", false);
            DrawerTitle.Text = _editCategory ? "编辑分类" : "编辑收藏";
            RefreshList();
            MsgText.Text = "已保存";
        }

        private void SaveClose_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!SaveCurrent(out err))
            {
                SetDrawerMsg(err, true);
                return;
            }
            HideDrawer();
            RefreshList();
            MsgText.Text = "已保存";
        }

        private bool CloseDrawer()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return true;
            if (!IsDirty())
            {
                HideDrawer();
                return true;
            }
            string what = _editCategory ? "当前分类" : "当前收藏";
            var r = ConfirmHelper.Unsaved(what + "有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
            {
                string err;
                if (!SaveCurrent(out err))
                {
                    SetDrawerMsg(err, true);
                    return false;
                }
            }
            HideDrawer();
            RefreshList();
            return true;
        }

        private void HideDrawer()
        {
            DrawerHost.Visibility = Visibility.Collapsed;
            _isNew = false;
            _editUid = null;
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

        private void SetDrawerMsg(string text, bool error)
        {
            if (DrawerMsg == null) return;
            DrawerMsg.Text = text ?? "";
            try
            {
                DrawerMsg.Foreground = FindResource(error ? "DangerBrush" : "TextSecondaryBrush") as Brush;
            }
            catch
            {
            }
        }

        private sealed class FavRow
        {
            public string Uid { get; set; }
            public string ParentUid { get; set; }
            public string Name { get; set; }
            public string KindText { get; set; }
            public string Target { get; set; }
            public string OpenWithText { get; set; }
            public bool Pinned { get; set; }
            public string PinText { get; set; }
            public bool IsCategory { get; set; }
            public Thickness Indent { get; set; }
            public Visibility ExpanderVisibility { get; set; }
            public string ExpanderGlyph { get; set; }
            public Visibility OpenOpsVisibility { get; set; }
            public Visibility MoveOpsVisibility { get; set; }

            public static FavRow From(LauncherFlatRow flat)
            {
                var n = flat.Node;
                bool cat = n.IsCategory;
                return new FavRow
                {
                    Uid = n.Uid,
                    ParentUid = n.ParentUid ?? "",
                    Name = n.Name ?? "",
                    KindText = cat ? "分类" : LauncherTarget.KindLabel(n.Kind),
                    Target = cat ? "" : (n.Target ?? ""),
                    OpenWithText = cat ? "" : LauncherTarget.OpenWithLabel(n.Kind, n.OpenWith),
                    Pinned = n.Pinned,
                    PinText = n.Pinned ? "置顶" : "",
                    IsCategory = cat,
                    Indent = new Thickness(flat.Depth * 16, 0, 0, 0),
                    ExpanderVisibility = cat && flat.HasChildren ? Visibility.Visible : Visibility.Collapsed,
                    ExpanderGlyph = flat.Expanded ? "−" : "+",
                    OpenOpsVisibility = cat ? Visibility.Collapsed : Visibility.Visible,
                    MoveOpsVisibility = cat ? Visibility.Collapsed : Visibility.Visible
                };
            }
        }
    }
}
