using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Vault
{
    public partial class VaultView : UserControl, IModuleView, ILeaveGuard
    {
        public static string PendingOpenUid;

        private readonly List<VaultRow> _rows = new List<VaultRow>();
        private bool _loading;
        private bool _drawerMax = true;
        private bool _skipListClick;
        private string _sortKey = "updated";
        private bool _sortAsc;
        private const double DrawerRatio = 0.90;

        public VaultView()
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
                DrawerEditor.FlushSilent();
            VaultStore.Flush();
            DrawerEditor.ForgetSession();
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

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            var item = VaultItem.CreateNew(VaultStore.UniqueTitle(null));
            VaultStore.Add(item);
            RefreshList();
            OpenDrawer(item.Uid);
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshListKeep()
        {
            string keep = null;
            var selected = VaultList.SelectedItem as VaultRow;
            if (selected != null) keep = selected.Uid;
            if (string.IsNullOrEmpty(keep)) keep = DrawerEditor.CurrentUid;
            RefreshList();
            if (!string.IsNullOrEmpty(keep)) SelectUid(keep);
        }

        private void RefreshList()
        {
            string keep = null;
            var selected = VaultList.SelectedItem as VaultRow;
            if (selected != null) keep = selected.Uid;
            _rows.Clear();
            var items = VaultStore.Snapshot();
            string q = SearchBox != null ? (SearchBox.Text ?? "").Trim() : "";
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (q.Length > 0)
                {
                    var body = VaultStore.LoadBody(it.Uid);
                    if (!VaultText.MatchesSearch(it.Title, body != null ? body.Remark : null,
                            body != null ? body.Entries : null, q))
                        continue;
                }
                _rows.Add(VaultRow.From(it));
            }
            SortRows();
            _loading = true;
            VaultList.ItemsSource = null;
            VaultList.ItemsSource = _rows;
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
                    VaultList.SelectedItem = _rows[i];
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
            VaultList.ItemsSource = null;
            VaultList.ItemsSource = _rows;
            _loading = false;
            UpdateSortHeaders();
            FitListColumns();
        }

        private void SortRows()
        {
            _rows.Sort(CompareRows);
        }

        private int CompareRows(VaultRow a, VaultRow b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int pin = b.Pinned.CompareTo(a.Pinned);
            if (pin != 0) return pin;
            int c;
            if (_sortKey == "title")
                c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            else if (_sortKey == "count")
                c = a.EntryCount.CompareTo(b.EntryCount);
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
            var view = VaultList != null ? VaultList.View as GridView : null;
            if (view == null || view.Columns.Count < 5) return;
            view.Columns[0].Header = SortTitle("置顶", "pin");
            view.Columns[1].Header = SortTitle("分类", "title");
            view.Columns[2].Header = SortTitle("条数", "count");
            view.Columns[3].Header = SortTitle("创建时间", "created");
            view.Columns[4].Header = SortTitle("修改时间", "updated");
        }

        private static string SortKeyFromHeader(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            if (content.StartsWith("置顶", StringComparison.Ordinal)) return "pin";
            if (content.StartsWith("分类", StringComparison.Ordinal)) return "title";
            if (content.StartsWith("条数", StringComparison.Ordinal)) return "count";
            if (content.StartsWith("创建时间", StringComparison.Ordinal)) return "created";
            if (content.StartsWith("修改时间", StringComparison.Ordinal)) return "updated";
            return null;
        }

        private string SortTitle(string title, string key)
        {
            if (_sortKey != key) return title;
            return title + (_sortAsc ? " ↑" : " ↓");
        }

        private void VaultList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitListColumns();
        }

        private void FitListColumns()
        {
            if (VaultList == null) return;
            var gv = VaultList.View as GridView;
            if (gv == null || gv.Columns.Count < 5) return;
            double w = VaultList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
            if (w < 200) return;
            double[] min = { 52, 140, 52, 128, 128 };
            double need = 52 + 140 + 52 + 128 + 128;
            if (w <= need)
            {
                for (int i = 0; i < 5; i++)
                    gv.Columns[i].Width = min[i];
                return;
            }
            double extra = w - need;
            gv.Columns[0].Width = 52;
            gv.Columns[1].Width = 140 + extra;
            gv.Columns[2].Width = 52;
            gv.Columns[3].Width = 128;
            gv.Columns[4].Width = 128;
        }

        private void VaultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MenuEdit_Click(sender, e);
        }

        private void VaultList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_skipListClick || _loading) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ListViewItem))
            {
                try { src = VisualTreeHelper.GetParent(src); }
                catch { break; }
            }
            if (src == null) return;
            var row = VaultList.SelectedItem as VaultRow;
            if (row != null) OpenDrawer(row.Uid);
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = VaultList.SelectedItem as VaultRow;
            bool on = row != null;
            var menu = sender as ContextMenu;
            if (menu == null) return;
            if (MenuEditItem != null)
            {
                MenuEditItem.Header = (row != null && row.Locked) ? "查看" : "编辑";
                MenuEditItem.IsEnabled = on;
            }
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var mi = menu.Items[i] as MenuItem;
                if (mi == null || mi == MenuEditItem) continue;
                string h = mi.Header as string;
                if (h == "删除" || h == "置顶" || h == "取消置顶" || h == "导入")
                    mi.IsEnabled = on && row != null && !row.Locked;
                else
                    mi.IsEnabled = on;
            }
            if (MenuPinItem != null)
                MenuPinItem.Visibility = (row != null && !row.Pinned) ? Visibility.Visible : Visibility.Collapsed;
            if (MenuUnpinItem != null)
                MenuUnpinItem.Visibility = (row != null && row.Pinned) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ListMenu_Closed(object sender, RoutedEventArgs e)
        {
            SkipClick();
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = VaultList.SelectedItem as VaultRow;
            if (row != null) OpenDrawer(row.Uid);
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            CopyAllSelected();
        }

        private void MenuCopyAll_Click(object sender, RoutedEventArgs e)
        {
            CopyAllSelected();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected();
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            ImportSelected();
        }

        private void MenuImport_Click(object sender, RoutedEventArgs e)
        {
            ImportSelected();
        }

        private string SelectedUid()
        {
            var row = VaultList.SelectedItem as VaultRow;
            if (row != null) return row.Uid;
            if (DrawerHost.Visibility == Visibility.Visible) return DrawerEditor.CurrentUid;
            return null;
        }

        private void CopyAllSelected()
        {
            string uid = SelectedUid();
            if (string.IsNullOrEmpty(uid))
            {
                MsgText.Text = "请先选择一个分类";
                return;
            }
            string text, err;
            if (!TryPlaintext(uid, out text, out err))
            {
                if (!string.IsNullOrEmpty(err)) MsgText.Text = err;
                return;
            }
            if (!VaultClipboard.TryCopy(text, out err))
            {
                MsgText.Text = err ?? "复制失败";
                return;
            }
            MsgText.Text = "已复制全部账号密码";
        }

        private void ExportSelected()
        {
            string uid = SelectedUid();
            if (string.IsNullOrEmpty(uid))
            {
                MsgText.Text = "请先选择一个分类";
                return;
            }
            string text, err;
            if (!TryPlaintext(uid, out text, out err))
            {
                if (!string.IsNullOrEmpty(err)) MsgText.Text = err;
                return;
            }
            string title = DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == uid
                ? DrawerEditor.CurrentTitle
                : (VaultList.SelectedItem as VaultRow) != null ? ((VaultRow)VaultList.SelectedItem).Title : null;
            var dlg = new SaveFileDialog
            {
                Filter = "文本|*.txt|所有文件|*.*",
                FileName = VaultText.SafeFileName(title) + ".txt"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, text, new UTF8Encoding(true));
                MsgText.Text = "已导出";
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ImportSelected()
        {
            string uid = SelectedUid();
            if (string.IsNullOrEmpty(uid))
            {
                MsgText.Text = "请先选择或打开一个分类";
                return;
            }
            var meta = VaultStore.GetByUid(uid);
            if (meta != null && meta.ReadOnly)
            {
                MsgText.Text = "已锁定，只能查看。取消勾选「锁定」后才能导入。";
                return;
            }
            var dlg = new OpenFileDialog { Filter = "文本|*.txt|所有文件|*.*" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string check;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out check))
            {
                MessageBox.Show(check, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string raw;
            try
            {
                raw = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var parsed = VaultText.ParseExport(raw);
            if (parsed.Count == 0)
            {
                MsgText.Text = "没有可导入的账号密码";
                return;
            }
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == uid)
            {
                int n = DrawerEditor.ImportEntries(parsed);
                MsgText.Text = n == 0 ? "没有可导入的账号密码" : "已导入 " + n + " 条";
                return;
            }
            if (meta == null) return;
            var body = VaultStore.LoadBody(uid);
            if (body.Entries == null) body.Entries = new List<VaultEntry>();
            for (int i = 0; i < parsed.Count; i++)
            {
                var e = parsed[i];
                if (e == null) continue;
                e.Masked = false;
                e.AccountEnc = null;
                e.PasswordEnc = null;
                body.Entries.Add(e);
            }
            string saveErr = VaultStore.SaveBody(meta, body, null);
            if (saveErr != null)
            {
                MessageBox.Show(saveErr, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RefreshListKeep();
            MsgText.Text = "已导入 " + parsed.Count + " 条";
        }

        private bool TryPlaintext(string uid, out string text, out string error)
        {
            text = null;
            error = null;
            if (string.IsNullOrEmpty(uid))
            {
                error = "请先选择一个分类";
                return false;
            }
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == uid)
                return DrawerEditor.TryBuildPlaintext(out text, out error);
            var body = VaultStore.LoadBody(uid);
            if (body == null) body = VaultStore.NewEmptyBody();
            if (VaultText.HasMasked(body.Entries))
            {
                if (!body.HasPassword)
                {
                    error = "加密行需要先设置查看密码";
                    return false;
                }
                byte[] key;
                if (!VaultPasswordWindow.TryUnlock(Window.GetWindow(this), body.SaltBytes(), body.Verifier, out key))
                    return false;
                if (body.Entries != null)
                {
                    for (int i = 0; i < body.Entries.Count; i++)
                    {
                        string err;
                        if (!VaultText.TryReveal(body.Entries[i], key, out err))
                        {
                            error = err ?? "解密失败";
                            return false;
                        }
                    }
                }
            }
            text = VaultText.CopyAll(body.Entries);
            if (string.IsNullOrEmpty(text))
            {
                error = "没有可复制的账号密码";
                return false;
            }
            return true;
        }

        private void MenuPin_Click(object sender, RoutedEventArgs e)
        {
            var row = VaultList.SelectedItem as VaultRow;
            if (row == null) return;
            if (row.Locked)
            {
                MsgText.Text = "已锁定，只能查看。取消勾选「锁定」后才能编辑或删除。";
                return;
            }
            VaultStore.SetPinned(row.Uid, true);
            RefreshListKeep();
        }

        private void MenuUnpin_Click(object sender, RoutedEventArgs e)
        {
            var row = VaultList.SelectedItem as VaultRow;
            if (row == null) return;
            if (row.Locked)
            {
                MsgText.Text = "已锁定，只能查看。取消勾选「锁定」后才能编辑或删除。";
                return;
            }
            VaultStore.SetPinned(row.Uid, false);
            RefreshListKeep();
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = VaultList.SelectedItem as VaultRow;
            if (row == null) return;
            if (row.Locked)
            {
                MsgText.Text = "已锁定，只能查看。取消勾选「锁定」后才能删除。";
                return;
            }
            if (!ConfirmHelper.Delete(row.Title)) return;
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == row.Uid)
                HideDrawer();
            else if (!CloseDrawer())
                return;
            VaultStore.Delete(row.Uid);
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
            var item = VaultStore.GetByUid(uid);
            if (item == null) return;
            DrawerTitle.Text = "账号备忘";
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
            var r = ConfirmHelper.Unsaved("当前分类有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
                DrawerEditor.FlushNow();
            HideDrawer();
            RefreshList();
            return true;
        }

        private void HideDrawer()
        {
            DrawerEditor.ForgetSession();
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

        private sealed class VaultRow
        {
            public string Uid { get; set; }
            public string Title { get; set; }
            public bool Pinned { get; set; }
            public string PinText { get; set; }
            public int EntryCount { get; set; }
            public string CountText { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedText { get; set; }
            public DateTime UpdatedAt { get; set; }
            public string UpdatedText { get; set; }
            public bool Locked { get; set; }

            public static VaultRow From(VaultItem it)
            {
                return new VaultRow
                {
                    Uid = it.Uid,
                    Title = it.Title ?? "",
                    Pinned = it.Pinned,
                    PinText = it.Pinned ? "置顶" : "",
                    EntryCount = it.EntryCount,
                    CountText = it.EntryCount.ToString(),
                    CreatedAt = it.CreatedAt,
                    CreatedText = it.CreatedAt == default(DateTime) ? "" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    UpdatedAt = it.UpdatedAt,
                    UpdatedText = it.UpdatedAt == default(DateTime) ? "" : it.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Locked = it.ReadOnly
                };
            }
        }
    }
}
