using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Core;

namespace IntraBox
{
    /// <summary>
    /// 主窗口：左侧导航 + 右侧工作区。模块切换交由 ModuleLoader（按需加载、用完销毁）。
    /// 设置固定在导航底部，不随工具列表滚动。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ModuleLoader _loader = new ModuleLoader();
        private readonly DispatcherTimer _memoryTimer = new DispatcherTimer();
        private bool _navSyncing;
        private bool _settingsSelected;

        public MainWindow()
        {
            InitializeComponent();
            LoadNav();
            ApplyNavCollapsed(ConfigManager.Instance.Settings.NavCollapsed);
            _memoryTimer.Interval = TimeSpan.FromSeconds(5);
            _memoryTimer.Tick += (s, e) => UpdateMemoryText();
            _memoryTimer.Start();
            UpdateMemoryText();
        }

        private void LoadNav()
        {
            var view = new CollectionViewSource { Source = ToolVisibility.NavTools() }.View;
            view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            NavList.ItemsSource = view;
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

            ModuleInfo found = null;
            if (!string.IsNullOrEmpty(keep))
            {
                foreach (ModuleInfo m in NavList.Items)
                {
                    if (m.Key == keep)
                    {
                        found = m;
                        break;
                    }
                }
            }

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
            if (NavList.SelectedItem is ModuleInfo info)
            {
                SetSettingsSelected(false);
                if (!_loader.Activate(info, WorkspaceHost))
                {
                    _navSyncing = true;
                    NavList.SelectedItem = _loader.CurrentInfo;
                    _navSyncing = false;
                    return;
                }
                ModuleTitle.Text = info.DisplayName;
                StatusText.Text = "当前工具：" + info.DisplayName;
                PersistLastModule(info.Key);
            }
        }

        private void SettingsNav_Click(object sender, MouseButtonEventArgs e)
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            var info = ToolVisibility.Find(ToolVisibility.SettingsKey);
            if (info == null) return;
            if (!_loader.Activate(info, WorkspaceHost)) return;

            _navSyncing = true;
            NavList.SelectedItem = null;
            _navSyncing = false;
            SetSettingsSelected(true);
            ModuleTitle.Text = info.DisplayName;
            StatusText.Text = "当前工具：" + info.DisplayName;
            PersistLastModule(info.Key);
        }

        /// <summary>提醒弹窗「打开任务」：切到任务计划并选中指定 uid。</summary>
        public void OpenTodo(string uid)
        {
            IntraBox.Modules.Todo.TodoView.PendingOpenUid = uid;
            var info = ToolVisibility.Find("todo");
            if (info == null) return;
            if (_loader.CurrentInfo != null && _loader.CurrentInfo.Key == "todo")
            {
                var view = _loader.CurrentView as IntraBox.Modules.Todo.TodoView;
                if (view != null) view.OpenPending();
                WindowRestore.ShowAndRestore(this);
                return;
            }
            if (!ToolVisibility.IsVisible("todo"))
            {
                if (!_loader.Activate(info, WorkspaceHost)) return;
                SetSettingsSelected(false);
                _navSyncing = true;
                NavList.SelectedItem = null;
                _navSyncing = false;
                ModuleTitle.Text = info.DisplayName;
                StatusText.Text = "当前工具：" + info.DisplayName;
                PersistLastModule(info.Key);
                return;
            }
            foreach (ModuleInfo m in NavList.Items)
            {
                if (m.Key == "todo")
                {
                    NavList.SelectedItem = m;
                    break;
                }
            }
        }

        /// <summary>启动时恢复上次打开的工具；无记录则保持欢迎语。</summary>
        public void RestoreLastModule()
        {
            string key = ConfigManager.Instance.Settings.LastModuleKey;
            if (string.IsNullOrEmpty(key)) return;
            if (key == ToolVisibility.SettingsKey)
            {
                OpenSettings();
                return;
            }
            foreach (ModuleInfo m in NavList.Items)
            {
                if (m.Key == key)
                {
                    NavList.SelectedItem = m;
                    return;
                }
            }
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
    }
}
