using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox
{
    /// <summary>
    /// 主窗口：左侧导航 + 右侧工作区。模块切换交由 ModuleLoader（按需加载、用完销毁）。
    /// 设置固定在导航底部，不随工具列表滚动。
    /// </summary>
    public partial class MainWindow : Window, IMemoryHost
    {
        private readonly ModuleLoader _loader = new ModuleLoader();
        private readonly DispatcherTimer _memoryTimer = new DispatcherTimer();
        private bool _navSyncing;
        private bool _settingsSelected;
        private ICollectionView _navView;
        private string _navFilter = "";
        private int _openGen;
        private bool _openQueued;
        private string _openKey = "";

        public MainWindow()
        {
            InitializeComponent();
            Title = AppVersion.ProductTitle;
            NavTitle.ToolTip = "版本 " + AppVersion.Display;
            LoadNav();
            ApplyNavCollapsed(ConfigManager.Instance.Settings.NavCollapsed);
            _memoryTimer.Interval = TimeSpan.FromSeconds(5);
            _memoryTimer.Tick += (s, e) => UpdateMemoryText();
            _memoryTimer.Start();
            UpdateMemoryText();
            MemoryIdleGuard.Attach(this);
            AppLog.Info("主窗口已打开");
        }

        private void LoadNav()
        {
            string keepFilter = _navFilter;
            var tools = ToolVisibility.NavTools();
            string[] fav = NavFavoriteHelper.Normalize(ConfigManager.Instance.Settings.NavFavoriteKeys);
            var rows = new List<NavRow>();
            for (int i = 0; i < tools.Count; i++)
                rows.Add(new NavRow(tools[i], NavFavoriteHelper.Contains(fav, tools[i].Key)));
            var view = new CollectionViewSource { Source = rows }.View;
            view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            view.Filter = NavFilter;
            _navView = view;
            NavList.ItemsSource = view;
            if (!string.IsNullOrEmpty(keepFilter) && NavSearchBox != null)
            {
                _navFilter = keepFilter;
                if (NavSearchBox.Text != keepFilter)
                    NavSearchBox.Text = keepFilter;
                else
                    view.Refresh();
            }
        }

        private bool NavFilter(object obj)
        {
            var row = obj as NavRow;
            var m = row != null ? row.Info : obj as ModuleInfo;
            if (m == null) return false;
            if (string.IsNullOrWhiteSpace(_navFilter)) return true;
            return SearchTextHelper.Score(_navFilter, m.DisplayName, m.Category) > 0
                || SearchTextHelper.Score(_navFilter, m.Key) > 0;
        }

        private void ApplyNavFilter()
        {
            _navFilter = NavSearchBox != null ? (NavSearchBox.Text ?? "").Trim() : "";
            if (_navView != null)
                _navView.Refresh();
            else if (NavList.ItemsSource is ICollectionView v)
                v.Refresh();
        }

        private void NavSearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (NavSearchBox == null) return;
            NavSearchBox.Text = "";
            NavSearchBox.Focus();
            ApplyNavFilter();
            StatusText.Text = "已清除导航筛选";
        }

        private void NavSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NavSearchClearBtn != null)
                NavSearchClearBtn.Visibility = string.IsNullOrEmpty(NavSearchBox.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            ApplyNavFilter();
        }

        private void NavSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyNavFilter();
                StatusText.Text = string.IsNullOrEmpty(_navFilter)
                    ? "已显示全部工具"
                    : ("导航筛选：" + _navFilter);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (NavSearchBox != null && !string.IsNullOrEmpty(NavSearchBox.Text))
                {
                    NavSearchBox.Text = "";
                    ApplyNavFilter();
                    StatusText.Text = "已清除导航筛选";
                    e.Handled = true;
                }
            }
        }

        /// <summary>设置页保存工具显隐后刷新导航。若当前模块被隐藏则切到第一个可见工具或设置。</summary>
        public void ReloadNav()
        {
            string keep = _loader.CurrentInfo != null ? _loader.CurrentInfo.Key : null;
            LoadNav();
            ApplyNavCollapsed(ConfigManager.Instance.Settings.NavCollapsed);

            if (keep == ToolVisibility.SettingsKey)
            {
                OpenSettings();
                return;
            }

            NavRow found = null;
            if (!string.IsNullOrEmpty(keep))
                found = FindRow(keep);

            if (found != null)
            {
                NavList.SelectedItem = found;
                return;
            }

            if (NavList.Items.Count > 0)
                NavList.SelectedIndex = 0;
            else
                OpenSettings();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_navSyncing) return;
            var info = ModuleOf(NavList.SelectedItem);
            if (info == null) return;
            ScheduleOpen(info);
        }

        /// <summary>
        /// 先点亮菜单并在右侧工作区盖上「正在打开…」，下一拍再创建工具。
        /// 连点时只完成最后一次；更早的排队创建直接放弃。
        /// </summary>
        private void ScheduleOpen(ModuleInfo info)
        {
            if (info == null) return;
            if (_loader.CurrentInfo != null && _loader.CurrentInfo.Key == info.Key && !_openQueued)
            {
                ApplyOpenedChrome(info);
                HideOpeningOverlay();
                return;
            }

            _openGen++;
            int gen = _openGen;
            _openQueued = true;
            _openKey = info.Key;
            if (info.Key == ToolVisibility.SettingsKey)
            {
                _navSyncing = true;
                NavList.SelectedItem = null;
                _navSyncing = false;
                SetSettingsSelected(true);
            }
            else
            {
                SetSettingsSelected(false);
            }
            if (ModuleTitle != null) ModuleTitle.Text = info.DisplayName;
            if (StatusText != null) StatusText.Text = "正在打开…";
            ShowOpeningOverlay();
            string key = info.Key;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => FinishOpen(gen, key)));
        }

        private void FinishOpen(int gen, string key)
        {
            if (gen != _openGen)
            {
                DiscardPending(key);
                return;
            }
            _openQueued = false;
            var info = ToolVisibility.Find(key);
            if (info == null)
            {
                HideOpeningOverlay();
                DiscardPending(key);
                return;
            }
            bool opened = false;
            try
            {
                opened = _loader.Activate(info, WorkspaceHost);
            }
            catch
            {
                if (gen == _openGen) HideOpeningOverlay();
                throw;
            }
            if (gen != _openGen)
            {
                DiscardPending(key);
                return;
            }
            if (!opened)
            {
                HideOpeningOverlay();
                RestoreNavSelectionAfterLeaveCancel();
                ApplyOpenedChrome(_loader.CurrentInfo);
                DiscardPending(key);
                return;
            }
            ApplyOpenedChrome(info);
            PersistLastModule(info.Key);
            AppLog.Info("打开工具：" + info.DisplayName + " (" + info.Key + ")");
            HideOpeningOverlay();
        }

        private void ShowOpeningOverlay()
        {
            if (WorkspaceHost == null) return;
            LoadingOverlay.Show(WorkspaceHost, "正在打开…");
        }

        private void HideOpeningOverlay()
        {
            if (WorkspaceHost == null) return;
            LoadingOverlay.Hide(WorkspaceHost);
        }

        private void ApplyOpenedChrome(ModuleInfo info)
        {
            if (info == null)
            {
                if (ModuleTitle != null) ModuleTitle.Text = "请从左侧选择一个工具";
                if (StatusText != null) StatusText.Text = "就绪";
                return;
            }
            SetSettingsSelected(info.Key == ToolVisibility.SettingsKey);
            if (ModuleTitle != null) ModuleTitle.Text = info.DisplayName;
            if (StatusText != null) StatusText.Text = "当前工具：" + info.DisplayName;
        }

        /// <summary>被放弃或离开确认取消时，清掉尚未消费的启动器跳转。</summary>
        private void DiscardPending(string key)
        {
            if (key == "todo" && !PendingStillWanted("todo"))
            {
                IntraBox.Modules.Todo.TodoView.PendingOpenUid = null;
                IntraBox.Modules.Todo.TodoView.PendingDueDate = null;
            }
            if (key == "notes" && !PendingStillWanted("notes"))
                IntraBox.Modules.Notes.NotesView.PendingOpenUid = null;
            if (key == "vault" && !PendingStillWanted("vault"))
                IntraBox.Modules.Vault.VaultView.PendingOpenUid = null;
        }

        private bool PendingStillWanted(string key)
        {
            if (_loader.CurrentInfo != null && _loader.CurrentInfo.Key == key) return true;
            return _openQueued && _openKey == key;
        }

        /// <summary>离开确认取消后恢复侧栏选中（设置不在列表里，需单独点亮底部设置项）。</summary>
        private void RestoreNavSelectionAfterLeaveCancel()
        {
            _navSyncing = true;
            var cur = _loader.CurrentInfo;
            if (cur != null && cur.Key == ToolVisibility.SettingsKey)
            {
                NavList.SelectedItem = null;
                SetSettingsSelected(true);
            }
            else
            {
                NavList.SelectedItem = FindRow(cur.Key);
                SetSettingsSelected(false);
            }
            _navSyncing = false;
        }

        private void SettingsNav_Click(object sender, MouseButtonEventArgs e)
        {
            OpenSettings();
        }

        private void NavStar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var btn = sender as FrameworkElement;
            var row = btn != null ? btn.DataContext as NavRow : null;
            if (row == null || row.Info == null) return;
            var s = ConfigManager.Instance.Settings;
            s.NavFavoriteKeys = NavFavoriteHelper.Toggle(s.NavFavoriteKeys, row.Info.Key);
            ConfigManager.Instance.Save();
            row.IsFavorite = NavFavoriteHelper.Contains(s.NavFavoriteKeys, row.Info.Key);
        }

        private static ModuleInfo ModuleOf(object item)
        {
            var row = item as NavRow;
            if (row != null) return row.Info;
            return item as ModuleInfo;
        }

        private NavRow FindRow(string key)
        {
            if (string.IsNullOrEmpty(key) || NavList == null) return null;
            foreach (object item in NavList.Items)
            {
                var row = item as NavRow;
                if (row != null && row.Info != null && row.Info.Key == key) return row;
            }
            return null;
        }

        private void OpenSettings()
        {
            var info = ToolVisibility.Find(ToolVisibility.SettingsKey);
            if (info == null) return;
            ScheduleOpen(info);
        }

        /// <summary>提醒弹窗「打开任务」或启动器：切到任务计划并选中指定 uid。</summary>
        public void OpenTodo(string uid)
        {
            IntraBox.Modules.Todo.TodoView.PendingOpenUid = uid;
            OpenModule("todo", delegate
            {
                var view = _loader.CurrentView as IntraBox.Modules.Todo.TodoView;
                if (view != null) view.OpenPending();
            });
        }

        /// <summary>工作日历：按当天计划完成时间新建待办。</summary>
        public void OpenTodoOnDate(DateTime day)
        {
            IntraBox.Modules.Todo.TodoView.PendingDueDate = day.Date;
            OpenModule("todo", delegate
            {
                var view = _loader.CurrentView as IntraBox.Modules.Todo.TodoView;
                if (view != null) view.OpenPending();
            });
        }

        /// <summary>启动器：切到笔记并打开指定篇。</summary>
        public void OpenNote(string uid)
        {
            IntraBox.Modules.Notes.NotesView.PendingOpenUid = uid;
            OpenModule("notes", delegate
            {
                var view = _loader.CurrentView as IntraBox.Modules.Notes.NotesView;
                if (view != null) view.OpenPending();
            });
        }

        /// <summary>启动器：切到账号备忘并打开指定分类。</summary>
        public void OpenVault(string uid)
        {
            IntraBox.Modules.Vault.VaultView.PendingOpenUid = uid;
            OpenModule("vault", delegate
            {
                var view = _loader.CurrentView as IntraBox.Modules.Vault.VaultView;
                if (view != null) view.OpenPending();
            });
        }

        /// <summary>启动器：切到指定工具（含设置）。</summary>
        public void OpenTool(string key)
        {
            if (string.IsNullOrEmpty(key) || key == ToolVisibility.SettingsKey)
            {
                WindowRestore.ShowAndRestore(this);
                OpenSettings();
                return;
            }
            OpenModule(key, null);
        }

        private void OpenModule(string key, Action ifAlreadyCurrent)
        {
            WindowRestore.ShowAndRestore(this);
            var info = ToolVisibility.Find(key);
            if (info == null) return;
            if (_loader.CurrentInfo != null && _loader.CurrentInfo.Key == key)
            {
                if (ifAlreadyCurrent != null) ifAlreadyCurrent();
                return;
            }
            if (!ToolVisibility.IsVisible(key))
            {
                _navSyncing = true;
                NavList.SelectedItem = null;
                _navSyncing = false;
                ScheduleOpen(info);
                return;
            }
            NavRow row = FindRow(key);
            if (row != null) NavList.SelectedItem = row;
        }

        /// <summary>启动时恢复上次打开的工具；设置关闭或无记录则保持欢迎语。重工具推迟到 UI 空闲，先画出空壳。</summary>
        public void RestoreLastModule()
        {
            if (!ConfigManager.Instance.Settings.RestoreLastModuleOnStartup) return;
            string key = ConfigManager.Instance.Settings.LastModuleKey;
            if (string.IsNullOrEmpty(key)) return;
            if (MemoryTrimPolicy.IsHeavyModule(key))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (_loader.CurrentView != null) return;
                    RestoreLastModuleCore(key);
                }));
                return;
            }
            RestoreLastModuleCore(key);
        }

        /// <summary>托盘回来后工作区已空时恢复上次工具（不受启动开关影响）。已有一次打开在排队则让路。</summary>
        public void RestoreLastModuleNow()
        {
            if (_openQueued) return;
            RestoreLastModuleCore(ConfigManager.Instance.Settings.LastModuleKey);
        }

        private void RestoreLastModuleCore(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (key == ToolVisibility.SettingsKey)
            {
                OpenSettings();
                return;
            }
            NavRow row = FindRow(key);
            if (row != null) NavList.SelectedItem = row;
        }

        public bool HasEmptyWorkspace()
        {
            return _loader.CurrentView == null;
        }

        public void ParkCurrentResources()
        {
            var p = _loader.CurrentView as IParkableResources;
            if (p != null) p.ParkHeavyResources();
        }

        public void UnparkCurrentResources()
        {
            if (_openQueued) return;
            var p = _loader.CurrentView as IParkableResources;
            if (p == null) return;
            int seen = _openGen;
            ShowOpeningOverlay();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (seen != _openGen || _openQueued) return;
                try { p.UnparkHeavyResources(); }
                finally
                {
                    if (seen == _openGen && !_openQueued) HideOpeningOverlay();
                }
            }));
        }

        public bool TryUnloadCurrentSilent()
        {
            if (_loader.CurrentView == null) return true;
            if (!_loader.TryDestroySilent()) return false;
            _navSyncing = true;
            NavList.SelectedItem = null;
            _navSyncing = false;
            SetSettingsSelected(false);
            ModuleTitle.Text = "请从左侧选择一个工具";
            StatusText.Text = "已释放当前工具";
            return true;
        }

        public bool IsBusyForTrim()
        {
            if (IntraBox.Controls.LoadingOverlay.HasAny()) return true;
            if (IntraBox.Modules.Screenshot.CaptureOverlayWindow.IsOpen) return true;
            if (IntraBox.Modules.ScreenRuler.ScreenRulerOverlayWindow.IsOpen) return true;
            if (IntraBox.Modules.Launcher.LauncherOverlayWindow.IsOpen) return true;
            try { return IntraBox.Modules.Todo.TodoReminderService.IsPromptOpen; }
            catch { return false; }
        }

        private static void PersistLastModule(string key)
        {
            ConfigManager.Instance.Settings.LastModuleKey = key ?? "";
            ConfigManager.Instance.Save();
        }

        private void SetSettingsSelected(bool on)
        {
            _settingsSelected = on;
            UpdateSettingsBtnVisual();
        }

        private void UpdateSettingsBtnVisual()
        {
            if (SettingsNavBtn == null) return;
            SettingsNavBtn.Background = _settingsSelected
                ? (Brush)FindResource("NavSelectedBrush")
                : Brushes.Transparent;
        }

        private void SettingsNav_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_settingsSelected || SettingsNavBtn == null) return;
            SettingsNavBtn.Background = (Brush)FindResource("NavHoverBrush");
        }

        private void SettingsNav_MouseLeave(object sender, MouseEventArgs e)
        {
            UpdateSettingsBtnVisual();
        }

        /// <summary>托盘退出前调用：比对未保存时可取消退出。</summary>
        public bool PrepareExit()
        {
            return _loader.TryDeactivate();
        }

        private void NavToggle_Click(object sender, RoutedEventArgs e)
        {
            bool collapse = NavCol.Width.Value > 80;
            ApplyNavCollapsed(collapse);
            ConfigManager.Instance.Settings.NavCollapsed = collapse;
            ConfigManager.Instance.Save();
        }

        private void ApplyNavCollapsed(bool collapsed)
        {
            if (collapsed)
            {
                NavCol.Width = new GridLength(44);
                NavTitle.Visibility = Visibility.Collapsed;
                NavSearchPanel.Visibility = Visibility.Collapsed;
                NavList.Visibility = Visibility.Collapsed;
                SettingsNavWrap.Visibility = Visibility.Collapsed;
                NavHeader.Margin = new Thickness(4, 12, 4, 8);
                NavToggleBtn.HorizontalAlignment = HorizontalAlignment.Center;
                NavToggleBtn.Content = "»";
                NavToggleBtn.ToolTip = "展开导航";
            }
            else
            {
                NavCol.Width = new GridLength(224);
                NavTitle.Visibility = Visibility.Visible;
                NavSearchPanel.Visibility = Visibility.Visible;
                NavList.Visibility = Visibility.Visible;
                SettingsNavWrap.Visibility = Visibility.Visible;
                NavHeader.Margin = new Thickness(8, 14, 8, 8);
                NavToggleBtn.HorizontalAlignment = HorizontalAlignment.Right;
                NavToggleBtn.Content = "«";
                NavToggleBtn.ToolTip = "折叠导航";
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            // 托盘右键「退出」已设 IsExiting，直接结束；标题栏关闭 / Alt+F4 走询问
            if (ConfirmHelper.IsExiting)
                return;

            e.Cancel = true;
            bool exit;
            var settings = ConfigManager.Instance.Settings;
            if (settings.ClosePromptSkip)
            {
                exit = settings.ClosePreferExit;
            }
            else
            {
                var dlg = new ClosePromptWindow { Owner = this };
                dlg.PrefillPreferExit(settings.ClosePreferExit);
                if (dlg.ShowDialog() != true)
                    return;
                exit = dlg.ChoseExit;
                if (dlg.DontAskAgain)
                {
                    settings.ClosePromptSkip = true;
                    settings.ClosePreferExit = exit;
                    ConfigManager.Instance.Save();
                }
                else
                {
                    // 未勾选「不再提醒」也记住本次选项，下次弹窗预选
                    settings.ClosePreferExit = exit;
                    ConfigManager.Instance.Save();
                }
            }

            if (exit)
            {
                if (!_loader.TryDeactivate()) return;
                ConfirmHelper.IsExiting = true;
                Application.Current.Shutdown();
                return;
            }

            WindowRestore.PersistFrom(this);
            Hide();
            MemoryIdleGuard.NotifyParked();
            // 刚进托盘只 GC，不 EmptyWorkingSet，避免马上再打开时整页换回卡住。
            // 空闲超时仍由 MemoryIdleGuard 卸载并压工作集。
            GcHelper.CollectSafely("tray-hide");
        }

        /// <summary>抽屉最大化时铺满导航右侧：隐藏模块标题并去掉工作区边距。</summary>
        public void SetWorkspaceFill(bool fill)
        {
            if (ModuleHeader != null)
                ModuleHeader.Visibility = fill ? Visibility.Collapsed : Visibility.Visible;
            if (WorkspaceHost != null)
                WorkspaceHost.Margin = fill ? new Thickness(0) : new Thickness(16);
        }

        private void UpdateMemoryText()
        {
            var u = MemoryUsage.Capture();
            MemoryText.Text = "内存 " + u.WorkingSetText;
        }

        public void RefreshMemoryText()
        {
            UpdateMemoryText();
        }

        private void MemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            string tool = null;
            if (_loader.CurrentInfo != null)
                tool = _loader.CurrentInfo.DisplayName;
            var w = new MemoryInspectWindow();
            w.Owner = this;
            w.LoadFrom(tool);
            w.ShowDialog();
            UpdateMemoryText();
        }

        private sealed class NavRow : INotifyPropertyChanged
        {
            private bool _favorite;

            public NavRow(ModuleInfo info, bool favorite)
            {
                Info = info;
                _favorite = favorite;
            }

            public ModuleInfo Info { get; private set; }
            public string Key { get { return Info != null ? Info.Key : ""; } }
            public string DisplayName { get { return Info != null ? Info.DisplayName : ""; } }
            public string Category { get { return Info != null ? Info.Category : ""; } }

            public bool IsFavorite
            {
                get { return _favorite; }
                set
                {
                    if (_favorite == value) return;
                    _favorite = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("IsFavorite"));
                        PropertyChanged(this, new PropertyChangedEventArgs("StarTip"));
                    }
                }
            }

            public string StarTip
            {
                get { return _favorite ? "取消收藏" : "收藏"; }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
