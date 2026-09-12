using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IntraBox.Controls;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;
using IntraBox.Modules.Notes;
using IntraBox.Modules.Todo;
using WF = System.Windows.Forms;

namespace IntraBox.Modules.Settings
{
    /// <summary>应用设置：主题、文件大小上限、截屏热键、工具显隐与导航排序。写入 config.json。</summary>
    public partial class SettingsView : UserControl, IModuleView, ILeaveGuard
    {
        private bool _loading;
        private bool _hkCtrl = true;
        private bool _hkAlt = true;
        private bool _hkShift;
        private int _hkVk = 0x41;
        private bool _hkLCtrl = true;
        private bool _hkLAlt = true;
        private bool _hkLShift;
        private int _hkLVk = 0x4C;
        private List<ToolVisItem> _toolItems;
        private List<ToolVisGroup> _toolGroups;
        private List<NavSortGroup> _sortGroups;
        private bool _sortUiOpen;
        private int _loadedMb;
        private int _loadedClip;
        private int _loadedClipImages;
        private int _loadedHistoryDelay;
        private bool _loadedVaultClip;
        private bool _loadedRestoreLast;
        private bool _loadedTrayUnload;
        private bool _loadedSkipClipImgPark;
        private int _loadedTrayIdle;
        private int _loadedTrimHigh;
        private int _loadedTrimLow;
        private string _loadedVis = "";
        private string _loadedSort = "";

        public SettingsView()
        {
            _loading = true;
            InitializeComponent();
            RangeBaseUtil.SetBounds(TrimHighSlider, MemoryTrimPolicy.MinHighMb, MemoryTrimPolicy.MaxHighMb);
            RangeBaseUtil.SetBounds(TrimLowSlider, MemoryTrimPolicy.MinLowMb, MemoryTrimPolicy.MaxLowMb);
            VersionText.Text = "版本 " + AppVersion.Display;
            _loading = false;
        }

        public void OnActivated()
        {
            _loading = true;
            int mb = SizeLimits.MaxFileMb;
            SizeSlider.Value = mb;
            UpdateSizeLabel(mb);
            HardCapHint.Text = "允许范围 " + SizeLimits.MinFileMb + "–" + SizeLimits.AbsoluteMaxFileMb
                + " MB。当前上限内的文件会整份加载，调高会增加内存峰值。";
            ThemeCombo.SelectedIndex = ThemeIndex(ThemeManager.Current);
            var s = ConfigManager.Instance.Settings;
            _hkCtrl = s.CaptureHotkeyCtrl;
            _hkAlt = s.CaptureHotkeyAlt;
            _hkShift = s.CaptureHotkeyShift;
            _hkVk = s.CaptureHotkeyVk > 0 ? s.CaptureHotkeyVk : 0x41;
            RefreshHotkeyBox();
            _hkLCtrl = s.LauncherHotkeyCtrl;
            _hkLAlt = s.LauncherHotkeyAlt;
            _hkLShift = s.LauncherHotkeyShift;
            _hkLVk = s.LauncherHotkeyVk > 0 ? s.LauncherHotkeyVk : 0x4C;
            RefreshLauncherHotkeyBox();
            LoadToolVisibility();
            LoadNavSort(false);
            ShowSortUi(false);
            int clip = AppSettings.ClampClipboardMax(s.ClipboardMaxItems);
            ClipSlider.Value = clip;
            UpdateClipLabel(clip);
            int clipImg = AppSettings.ClampClipboardMaxImages(s.ClipboardMaxImageItems);
            ClipImageSlider.Value = clipImg;
            UpdateClipImageLabel(clipImg);
            VaultClipCheck.IsChecked = s.ClipboardRecordVaultCopies;
            RestoreLastCheck.IsChecked = s.RestoreLastModuleOnStartup;
            TrayUnloadCheck.IsChecked = s.TrayIdleUnloadModule;
            SkipClipImgParkCheck.IsChecked = s.ClipboardSkipImagesWhenHidden;
            int idle = MemoryTrimPolicy.ClampIdleSec(s.TrayIdleReleaseSec);
            TrayIdleSlider.Value = idle;
            UpdateTrayIdleLabel(idle);
            int high = MemoryTrimPolicy.ClampHighMb(s.MemoryTrimHighMb);
            TrimHighSlider.Value = high;
            UpdateTrimHighLabel(high);
            SyncTrimLowSliderMax(high);
            int low = MemoryTrimPolicy.ClampLowMb(s.MemoryTrimLowMb, high);
            TrimLowSlider.Value = low;
            UpdateTrimLowLabel(low);
            int delay = AppSettings.CurrentHistoryPersistDelayMs();
            HistoryDelaySlider.Value = delay;
            UpdateHistoryDelayLabel(delay);
            RefreshDataDirBox();
            MsgText.Text = "";
            _loading = false;
            RememberClean();
        }

        public void OnDeactivated()
        {
        }

        public bool CanLeave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("设置有未保存的更改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return TryPersistSettings();
            return true;
        }

        public bool HasUnsavedChanges()
        {
            return IsDirty();
        }

        private string CurrentVisKey()
        {
            if (_toolItems == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _toolItems.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(_toolItems[i].Key);
                sb.Append(_toolItems[i].IsVisible ? ":1" : ":0");
            }
            return sb.ToString();
        }

        private void RememberClean()
        {
            _loadedMb = SizeLimits.ClampMb((int)SizeSlider.Value);
            _loadedClip = AppSettings.ClampClipboardMax((int)ClipSlider.Value);
            _loadedClipImages = AppSettings.ClampClipboardMaxImages((int)ClipImageSlider.Value);
            _loadedVaultClip = VaultClipCheck != null && VaultClipCheck.IsChecked == true;
            _loadedRestoreLast = RestoreLastCheck != null && RestoreLastCheck.IsChecked == true;
            _loadedTrayUnload = TrayUnloadCheck != null && TrayUnloadCheck.IsChecked == true;
            _loadedSkipClipImgPark = SkipClipImgParkCheck != null && SkipClipImgParkCheck.IsChecked == true;
            _loadedTrayIdle = MemoryTrimPolicy.ClampIdleSec((int)TrayIdleSlider.Value);
            _loadedTrimHigh = MemoryTrimPolicy.ClampHighMb((int)TrimHighSlider.Value);
            _loadedTrimLow = MemoryTrimPolicy.ClampLowMb((int)TrimLowSlider.Value, _loadedTrimHigh);
            _loadedHistoryDelay = AppSettings.ClampHistoryPersistDelayMs((int)HistoryDelaySlider.Value);
            _loadedVis = CurrentVisKey();
            _loadedSort = CurrentSortKey();
        }

        private bool IsDirty()
        {
            int mb = SizeLimits.ClampMb((int)SizeSlider.Value);
            int clip = AppSettings.ClampClipboardMax((int)ClipSlider.Value);
            int clipImg = AppSettings.ClampClipboardMaxImages((int)ClipImageSlider.Value);
            int delay = AppSettings.ClampHistoryPersistDelayMs((int)HistoryDelaySlider.Value);
            bool vaultClip = VaultClipCheck != null && VaultClipCheck.IsChecked == true;
            bool restoreLast = RestoreLastCheck != null && RestoreLastCheck.IsChecked == true;
            bool trayUnload = TrayUnloadCheck != null && TrayUnloadCheck.IsChecked == true;
            bool skipImg = SkipClipImgParkCheck != null && SkipClipImgParkCheck.IsChecked == true;
            int idle = MemoryTrimPolicy.ClampIdleSec((int)TrayIdleSlider.Value);
            int high = MemoryTrimPolicy.ClampHighMb((int)TrimHighSlider.Value);
            int low = MemoryTrimPolicy.ClampLowMb((int)TrimLowSlider.Value, high);
            return mb != _loadedMb || clip != _loadedClip || clipImg != _loadedClipImages || delay != _loadedHistoryDelay
                || vaultClip != _loadedVaultClip
                || restoreLast != _loadedRestoreLast || trayUnload != _loadedTrayUnload || skipImg != _loadedSkipClipImgPark
                || idle != _loadedTrayIdle || high != _loadedTrimHigh || low != _loadedTrimLow
                || CurrentVisKey() != _loadedVis || CurrentSortKey() != _loadedSort;
        }

        private void LoadToolVisibility()
        {
            _toolItems = new List<ToolVisItem>();
            _toolGroups = new List<ToolVisGroup>();
            var tools = ToolVisibility.ToggleableTools();
            ToolVisGroup cur = null;
            for (int i = 0; i < tools.Count; i++)
            {
                var item = new ToolVisItem
                {
                    Key = tools[i].Key,
                    DisplayName = tools[i].DisplayName,
                    IsVisible = ToolVisibility.IsVisible(tools[i].Key)
                };
                _toolItems.Add(item);
                if (cur == null || cur.Category != tools[i].Category)
                {
                    cur = new ToolVisGroup { Category = tools[i].Category };
                    _toolGroups.Add(cur);
                }
                cur.Items.Add(item);
            }
            ToolVisGroups.ItemsSource = _toolGroups;
        }

        private string CurrentSortKey()
        {
            if (_sortGroups == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < _sortGroups.Count; g++)
            {
                if (g > 0) sb.Append('|');
                sb.Append(_sortGroups[g].Category);
                sb.Append('>');
                var items = _sortGroups[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(items[i].Key);
                }
            }
            return sb.ToString();
        }

        private void LoadNavSort(bool useDefault)
        {
            var tools = useDefault ? ToolVisibility.ToggleableToolsDefault() : ToolVisibility.ToggleableTools();
            _sortGroups = BuildSortGroups(tools);
            BindNavSort();
            RebuildVisGroups();
        }

        private static List<NavSortGroup> BuildSortGroups(List<ModuleInfo> tools)
        {
            var groups = new List<NavSortGroup>();
            NavSortGroup cur = null;
            for (int i = 0; i < tools.Count; i++)
            {
                if (cur == null || cur.Category != tools[i].Category)
                {
                    cur = new NavSortGroup { Category = tools[i].Category };
                    groups.Add(cur);
                }
                cur.Items.Add(new NavSortItem
                {
                    Key = tools[i].Key,
                    DisplayName = tools[i].DisplayName
                });
            }
            return groups;
        }

        private void BindNavSort()
        {
            if (NavSortGrid == null) return;
            var rows = new List<NavTreeNode>();
            if (_sortGroups != null)
            {
                for (int g = 0; g < _sortGroups.Count; g++)
                {
                    var sg = _sortGroups[g];
                    rows.Add(new NavTreeNode
                    {
                        Title = sg.Category,
                        Category = sg.Category,
                        IsCategory = true
                    });
                    for (int i = 0; i < sg.Items.Count; i++)
                    {
                        rows.Add(new NavTreeNode
                        {
                            Title = sg.Items[i].DisplayName,
                            Key = sg.Items[i].Key,
                            Category = sg.Category,
                            IsCategory = false
                        });
                    }
                }
            }
            NavSortGrid.ItemsSource = null;
            NavSortGrid.ItemsSource = rows;
        }

        private void NavSortToggle_Click(object sender, RoutedEventArgs e)
        {
            ShowSortUi(!_sortUiOpen);
        }

        private void ShowSortUi(bool on)
        {
            _sortUiOpen = on;
            if (ToolVisGroups != null)
                ToolVisGroups.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (NavSortPanel != null)
                NavSortPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (NavSortToggleBtn != null)
                NavSortToggleBtn.Content = on ? "返回显隐" : "调整顺序";
            if (on) BindNavSort();
        }

        private void RebuildVisGroups()
        {
            if (_toolItems == null || _sortGroups == null) return;
            _toolGroups = new List<ToolVisGroup>();
            for (int g = 0; g < _sortGroups.Count; g++)
            {
                var sg = _sortGroups[g];
                var vg = new ToolVisGroup { Category = sg.Category };
                for (int i = 0; i < sg.Items.Count; i++)
                {
                    var vis = FindToolVis(sg.Items[i].Key);
                    if (vis != null) vg.Items.Add(vis);
                }
                if (vg.Items.Count > 0)
                    _toolGroups.Add(vg);
            }
            if (ToolVisGroups != null)
            {
                ToolVisGroups.ItemsSource = null;
                ToolVisGroups.ItemsSource = _toolGroups;
            }
        }

        private ToolVisItem FindToolVis(string key)
        {
            if (_toolItems == null) return null;
            for (int i = 0; i < _toolItems.Count; i++)
            {
                if (_toolItems[i].Key == key) return _toolItems[i];
            }
            return null;
        }

        private void NavSortReset_Click(object sender, RoutedEventArgs e)
        {
            LoadNavSort(true);
            ShowSortUi(true);
            MsgText.Text = "已恢复默认排序，保存设置后生效。";
        }

        private void NavTreeTop_Click(object sender, RoutedEventArgs e)
        {
            MoveNavNode(sender, -2);
        }

        private void NavTreeUp_Click(object sender, RoutedEventArgs e)
        {
            MoveNavNode(sender, -1);
        }

        private void NavTreeDown_Click(object sender, RoutedEventArgs e)
        {
            MoveNavNode(sender, 1);
        }

        private void NavTreeBottom_Click(object sender, RoutedEventArgs e)
        {
            MoveNavNode(sender, 2);
        }

        private void MoveNavNode(object sender, int dir)
        {
            var btn = sender as Button;
            var node = btn != null ? btn.Tag as NavTreeNode : null;
            if (node == null) return;
            if (node.IsCategory)
                MoveCategory(node.Category, dir);
            else
                MoveTool(node.Key, dir);
        }

        private void MoveCategory(string category, int dir)
        {
            int i = IndexOfGroup(category);
            if (i < 0) return;
            MoveInList(_sortGroups, i, dir);
            BindNavSort();
            RebuildVisGroups();
        }

        private void MoveTool(string key, int dir)
        {
            NavSortGroup g;
            int i = IndexOfTool(key, out g);
            if (g == null || i < 0) return;
            MoveInList(g.Items, i, dir);
            BindNavSort();
            RebuildVisGroups();
        }

        private int IndexOfGroup(string category)
        {
            if (_sortGroups == null || string.IsNullOrEmpty(category)) return -1;
            for (int i = 0; i < _sortGroups.Count; i++)
            {
                if (_sortGroups[i].Category == category) return i;
            }
            return -1;
        }

        private int IndexOfTool(string key, out NavSortGroup group)
        {
            group = null;
            if (_sortGroups == null || string.IsNullOrEmpty(key)) return -1;
            for (int g = 0; g < _sortGroups.Count; g++)
            {
                var items = _sortGroups[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Key == key)
                    {
                        group = _sortGroups[g];
                        return i;
                    }
                }
            }
            return -1;
        }

        private static void MoveInList<T>(List<T> list, int index, int dir)
        {
            if (list == null || index < 0 || index >= list.Count) return;
            int dest = index;
            if (dir == -2) dest = 0;
            else if (dir == -1) dest = index - 1;
            else if (dir == 1) dest = index + 1;
            else dest = list.Count - 1;
            if (dest < 0 || dest >= list.Count || dest == index) return;
            T item = list[index];
            list.RemoveAt(index);
            list.Insert(dest, item);
        }

        private void ToolVisAllOn_Click(object sender, RoutedEventArgs e)
        {
            SetAllToolVis(true);
        }

        private void ToolVisAllOff_Click(object sender, RoutedEventArgs e)
        {
            SetAllToolVis(false);
        }

        private void SetAllToolVis(bool on)
        {
            if (_toolItems == null) return;
            for (int i = 0; i < _toolItems.Count; i++)
                _toolItems[i].IsVisible = on;
        }

        private void RefreshDataDirBox()
        {
            if (DataDirBox == null) return;
            DataDirBox.Text = DataPaths.Root;
        }

        private void DataDirOpen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DataPaths.Root,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MsgText.Text = "无法打开：" + ex.Message;
            }
        }

        private void DataDirChange_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WF.FolderBrowserDialog())
            {
                dlg.Description = "选择新的数据目录（将复制现有文件，不删除原目录）";
                dlg.ShowNewFolderButton = true;
                try { dlg.SelectedPath = DataPaths.Root; } catch { }
                if (dlg.ShowDialog() != WF.DialogResult.OK) return;
                BeginCopyDataRoot(dlg.SelectedPath, false);
            }
        }

        private void DataDirReset_Click(object sender, RoutedEventArgs e)
        {
            if (DataPaths.IsDefaultRoot)
            {
                MsgText.Text = "已经是默认数据目录。";
                return;
            }
            BeginCopyDataRoot(DataPaths.DefaultRoot, true);
        }

        private void BeginCopyDataRoot(string dest, bool restoreDefault)
        {
            string err;
            if (!DataPaths.TryValidateNewRoot(dest, out err))
            {
                MsgText.Text = err;
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (DataPaths.SamePath(dest, DataPaths.Root))
            {
                MsgText.Text = "已经是当前数据目录。";
                return;
            }
            LoadingOverlay.Show(this, "正在复制数据文件…");
            string destCopy = dest;
            Task.Run(() =>
            {
                try
                {
                    Dispatcher.Invoke(new Action(FlushAllStores));
                    DataPaths.CopyRootTo(destCopy);
                    Dispatcher.Invoke(new Action(() => FinishDataRootCopy(destCopy, restoreDefault, null)));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(new Action(() => FinishDataRootCopy(null, false, ex.Message)));
                }
            });
        }

        private static void FlushAllStores()
        {
            HistoryManager.Flush();
            IntraBox.Modules.FileOrganize.FileOrganizeStore.Flush();
            TodoStore.Flush();
            NoteStore.Flush();
            IntraBox.Modules.Vault.VaultStore.Flush();
            IntraBox.Modules.Launcher.LauncherStore.Flush();
            ConfigManager.Instance.Save();
        }

        private void FinishDataRootCopy(string dest, bool restoreDefault, string error)
        {
            LoadingOverlay.Hide(this);
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(dest))
            {
                MsgText.Text = "复制失败，仍使用原目录。" + (error ?? "");
                MessageBox.Show("复制失败：" + (error ?? "未知错误"), "IntraBox",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DataPaths.SetRoot(dest, restoreDefault);
            ConfigManager.Instance.Load();
            HistoryManager.LoadFromDisk();
            TodoStore.Reload();
            NoteStore.Reload();
            IntraBox.Modules.Vault.VaultStore.Reload();
            ThemeManager.Apply(ConfigManager.Instance.Settings.Theme);
            RefreshDataDirBox();
            OnActivated();
            MsgText.Text = restoreDefault
                ? "已恢复默认数据目录（文件已复制，原目录未删除）。"
                : "数据目录已更换（文件已复制，原目录未删除）。";
        }

        private void RefreshHotkeyBox()
        {
            if (HotkeyBox == null) return;
            uint mod = 0;
            if (_hkCtrl) mod |= 0x0002;
            if (_hkAlt) mod |= 0x0001;
            if (_hkShift) mod |= 0x0004;
            HotkeyBox.Text = HotkeyService.Format(mod, (uint)_hkVk);
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.System || key == Key.Tab)
                return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!ctrl && !alt)
            {
                MsgText.Text = "请至少加上 Ctrl 或 Alt，以免占用普通按键。";
                return;
            }
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0)
            {
                MsgText.Text = "无法识别该按键。";
                return;
            }
            if (HotkeyService.Instance != null && !HotkeyService.Instance.Rebind(ctrl, alt, shift, vk))
            {
                MsgText.Text = "热键注册失败，可能已被其他程序占用。";
                return;
            }
            _hkCtrl = ctrl;
            _hkAlt = alt;
            _hkShift = shift;
            _hkVk = vk;
            PersistHotkey();
            RefreshHotkeyBox();
            MsgText.Text = "截屏热键已改为 " + HotkeyBox.Text + "。";
        }

        private void HotkeyReset_Click(object sender, RoutedEventArgs e)
        {
            if (HotkeyService.Instance != null && !HotkeyService.Instance.Rebind(true, true, false, 0x41))
            {
                MsgText.Text = "无法恢复默认热键（可能被占用）。";
                return;
            }
            _hkCtrl = true;
            _hkAlt = true;
            _hkShift = false;
            _hkVk = 0x41;
            PersistHotkey();
            RefreshHotkeyBox();
            MsgText.Text = "已恢复默认 Ctrl+Alt+A。";
        }

        private void PersistHotkey()
        {
            var s = ConfigManager.Instance.Settings;
            s.CaptureHotkeyCtrl = _hkCtrl;
            s.CaptureHotkeyAlt = _hkAlt;
            s.CaptureHotkeyShift = _hkShift;
            s.CaptureHotkeyVk = _hkVk;
            ConfigManager.Instance.Save();
        }

        private void RefreshLauncherHotkeyBox()
        {
            if (LauncherHotkeyBox == null) return;
            uint mod = 0;
            if (_hkLCtrl) mod |= 0x0002;
            if (_hkLAlt) mod |= 0x0001;
            if (_hkLShift) mod |= 0x0004;
            LauncherHotkeyBox.Text = HotkeyService.Format(mod, (uint)_hkLVk);
        }

        private void LauncherHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.System || key == Key.Tab)
                return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!ctrl && !alt)
            {
                MsgText.Text = "请至少加上 Ctrl 或 Alt，以免占用普通按键。";
                return;
            }
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0)
            {
                MsgText.Text = "无法识别该按键。";
                return;
            }
            if (HotkeyService.Instance != null && !HotkeyService.Instance.RebindLauncher(ctrl, alt, shift, vk))
            {
                MsgText.Text = "启动器热键注册失败，可能与截屏热键冲突或已被占用。";
                return;
            }
            _hkLCtrl = ctrl;
            _hkLAlt = alt;
            _hkLShift = shift;
            _hkLVk = vk;
            PersistLauncherHotkey();
            RefreshLauncherHotkeyBox();
            MsgText.Text = "启动器热键已改为 " + LauncherHotkeyBox.Text + "。";
        }

        private void LauncherHotkeyReset_Click(object sender, RoutedEventArgs e)
        {
            if (HotkeyService.Instance != null && !HotkeyService.Instance.RebindLauncher(true, true, false, 0x4C))
            {
                MsgText.Text = "无法恢复启动器默认热键（可能被占用）。";
                return;
            }
            _hkLCtrl = true;
            _hkLAlt = true;
            _hkLShift = false;
            _hkLVk = 0x4C;
            PersistLauncherHotkey();
            RefreshLauncherHotkeyBox();
            MsgText.Text = "已恢复启动器默认 Ctrl+Alt+L。";
        }

        private void PersistLauncherHotkey()
        {
            var s = ConfigManager.Instance.Settings;
            s.LauncherHotkeyCtrl = _hkLCtrl;
            s.LauncherHotkeyAlt = _hkLAlt;
            s.LauncherHotkeyShift = _hkLShift;
            s.LauncherHotkeyVk = _hkLVk;
            ConfigManager.Instance.Save();
        }

        private static int ThemeIndex(string name)
        {
            if (name == ThemeManager.Light) return 0;
            if (name == ThemeManager.System) return 2;
            return 1;
        }

        private static string ThemeName(int index)
        {
            if (index == 0) return ThemeManager.Light;
            if (index == 2) return ThemeManager.System;
            return ThemeManager.Dark;
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ThemeCombo == null) return;
            ThemeManager.Apply(ThemeName(ThemeCombo.SelectedIndex));
            MsgText.Text = "主题已切换并保存。";
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || SizeLabel == null) return;
            UpdateSizeLabel((int)SizeSlider.Value);
        }

        private void UpdateSizeLabel(int mb)
        {
            SizeLabel.Text = mb + " MB";
        }

        private void ClipSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || ClipLabel == null) return;
            UpdateClipLabel((int)ClipSlider.Value);
            if (ClipImageSlider != null) UpdateClipImageLabel((int)ClipImageSlider.Value);
        }

        private void ClipImageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || ClipImageLabel == null) return;
            UpdateClipImageLabel((int)ClipImageSlider.Value);
        }

        private void VaultClip_Changed(object sender, RoutedEventArgs e)
        {
        }

        private void MemOpt_Changed(object sender, RoutedEventArgs e)
        {
        }

        private void TrayIdleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TrayIdleLabel == null) return;
            UpdateTrayIdleLabel((int)TrayIdleSlider.Value);
        }

        private void TrimHighSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TrimHighLabel == null) return;
            int high = MemoryTrimPolicy.ClampHighMb((int)TrimHighSlider.Value);
            UpdateTrimHighLabel(high);
            SyncTrimLowSliderMax(high);
            if (TrimLowSlider != null)
            {
                int low = MemoryTrimPolicy.ClampLowMb((int)TrimLowSlider.Value, high);
                if ((int)TrimLowSlider.Value != low) TrimLowSlider.Value = low;
            }
        }

        private void TrimLowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || TrimLowLabel == null) return;
            int high = TrimHighSlider != null ? MemoryTrimPolicy.ClampHighMb((int)TrimHighSlider.Value) : MemoryTrimPolicy.DefaultHighMb;
            UpdateTrimLowLabel(MemoryTrimPolicy.ClampLowMb((int)TrimLowSlider.Value, high));
        }

        private void UpdateTrayIdleLabel(int n)
        {
            TrayIdleLabel.Text = n + " 秒";
        }

        private void UpdateTrimHighLabel(int n)
        {
            TrimHighLabel.Text = n + " MB";
        }

        private void SyncTrimLowSliderMax(int high)
        {
            int maxLow = high - 20;
            if (maxLow < MemoryTrimPolicy.MinLowMb) maxLow = MemoryTrimPolicy.MinLowMb;
            RangeBaseUtil.SetBounds(TrimLowSlider, MemoryTrimPolicy.MinLowMb, maxLow);
        }

        private void UpdateTrimLowLabel(int n)
        {
            TrimLowLabel.Text = n + " MB";
        }

        private void UpdateClipLabel(int n)
        {
            ClipLabel.Text = n + " 条";
        }

        private void UpdateClipImageLabel(int n)
        {
            if (ClipImageLabel == null) return;
            int total = ClipSlider != null ? AppSettings.ClampClipboardMax((int)ClipSlider.Value) : 20;
            int effective = ClipboardStore.EffectiveMaxImageItems(total, n);
            if (effective < n)
                ClipImageLabel.Text = n + " 张（受总条数限制，实际 " + effective + "）";
            else
                ClipImageLabel.Text = n + " 张";
        }

        private void HistoryDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || HistoryDelayLabel == null) return;
            UpdateHistoryDelayLabel((int)HistoryDelaySlider.Value);
        }

        private void UpdateHistoryDelayLabel(int ms)
        {
            HistoryDelayLabel.Text = ms + " 毫秒";
        }

        private void VersionText_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Clipboard.SetText(AppVersion.Full);
                MsgText.Text = "已复制 " + AppVersion.Full;
            }
            catch
            {
                MsgText.Text = "复制版本号失败";
            }
        }

        private void Welcome_Click(object sender, RoutedEventArgs e)
        {
            var w = new IntraBox.WelcomeWindow();
            w.Owner = Window.GetWindow(this);
            w.ShowDialog();
        }

        private void ClosePromptReset_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Instance.Settings;
            s.ClosePromptSkip = false;
            ConfigManager.Instance.Save();
            RememberClean();
            MsgText.Text = "已恢复关闭提示，下次点关闭会再询问";
            MsgText.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TryPersistSettings();
        }

        private bool TryPersistSettings()
        {
            var visible = new List<string>();
            if (_toolItems != null)
            {
                for (int i = 0; i < _toolItems.Count; i++)
                {
                    if (_toolItems[i].IsVisible)
                        visible.Add(_toolItems[i].Key);
                }
            }
            if (visible.Count == 0)
            {
                MsgText.Text = "至少选择一个工具";
                MessageBox.Show("至少选择一个工具", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            int mb = SizeLimits.ClampMb((int)SizeSlider.Value);
            int clip = AppSettings.ClampClipboardMax((int)ClipSlider.Value);
            int clipImg = AppSettings.ClampClipboardMaxImages((int)ClipImageSlider.Value);
            int delay = AppSettings.ClampHistoryPersistDelayMs((int)HistoryDelaySlider.Value);
            SizeSlider.Value = mb;
            ClipSlider.Value = clip;
            ClipImageSlider.Value = clipImg;
            HistoryDelaySlider.Value = delay;
            UpdateSizeLabel(mb);
            UpdateClipLabel(clip);
            UpdateClipImageLabel(clipImg);
            UpdateHistoryDelayLabel(delay);
            ConfigManager.Instance.Settings.MaxFileSizeMb = mb;
            ConfigManager.Instance.Settings.ClipboardMaxItems = clip;
            ConfigManager.Instance.Settings.ClipboardMaxImageItems = clipImg;
            ConfigManager.Instance.Settings.ClipboardRecordVaultCopies = VaultClipCheck != null && VaultClipCheck.IsChecked == true;
            ConfigManager.Instance.Settings.RestoreLastModuleOnStartup = RestoreLastCheck != null && RestoreLastCheck.IsChecked == true;
            ConfigManager.Instance.Settings.TrayIdleUnloadModule = TrayUnloadCheck != null && TrayUnloadCheck.IsChecked == true;
            ConfigManager.Instance.Settings.ClipboardSkipImagesWhenHidden = SkipClipImgParkCheck != null && SkipClipImgParkCheck.IsChecked == true;
            int idle = MemoryTrimPolicy.ClampIdleSec((int)TrayIdleSlider.Value);
            int high = MemoryTrimPolicy.ClampHighMb((int)TrimHighSlider.Value);
            SyncTrimLowSliderMax(high);
            int low = MemoryTrimPolicy.ClampLowMb((int)TrimLowSlider.Value, high);
            TrayIdleSlider.Value = idle;
            TrimHighSlider.Value = high;
            TrimLowSlider.Value = low;
            UpdateTrayIdleLabel(idle);
            UpdateTrimHighLabel(high);
            UpdateTrimLowLabel(low);
            ConfigManager.Instance.Settings.TrayIdleReleaseSec = idle;
            ConfigManager.Instance.Settings.MemoryTrimHighMb = high;
            ConfigManager.Instance.Settings.MemoryTrimLowMb = low;
            ConfigManager.Instance.Settings.HistoryPersistDelayMs = delay;
            ConfigManager.Instance.Settings.VisibleToolKeys = visible.ToArray();
            ApplyNavOrderFromUi();
            ThemeManager.Apply(ThemeName(ThemeCombo.SelectedIndex));
            ConfigManager.Instance.Save();
            ClipboardStore.TrimToLimit();
            RememberClean();

            var main = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (main != null) main.ReloadNav();

            MsgText.Text = "已保存。状态记忆写入延迟 " + delay + " 毫秒，文件上限 " + mb + " MB，剪贴板 " + clip + " 条（图片最多 "
                + ClipboardStore.EffectiveMaxImageItems(clip, clipImg) + " 张），托盘空闲 " + idle + " 秒，工作集水位 "
                + low + "–" + high + " MB，主题、显隐与导航排序已记住。";
            return true;
        }

        private void ApplyNavOrderFromUi()
        {
            var cats = new List<string>();
            var keys = new List<string>();
            if (_sortGroups != null)
            {
                for (int g = 0; g < _sortGroups.Count; g++)
                {
                    cats.Add(_sortGroups[g].Category);
                    var items = _sortGroups[g].Items;
                    for (int i = 0; i < items.Count; i++)
                        keys.Add(items[i].Key);
                }
            }
            var catArr = cats.ToArray();
            var keyArr = keys.ToArray();
            if (NavOrder.IsDefaultOrder(catArr, keyArr))
            {
                ConfigManager.Instance.Settings.NavCategoryOrder = null;
                ConfigManager.Instance.Settings.NavToolOrder = null;
            }
            else
            {
                ConfigManager.Instance.Settings.NavCategoryOrder = catArr;
                ConfigManager.Instance.Settings.NavToolOrder = keyArr;
            }
        }

        private sealed class ToolVisGroup
        {
            public string Category { get; set; }
            public List<ToolVisItem> Items { get; set; }

            public ToolVisGroup()
            {
                Items = new List<ToolVisItem>();
            }
        }

        private sealed class ToolVisItem : INotifyPropertyChanged
        {
            private bool _visible = true;
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public bool IsVisible
            {
                get { return _visible; }
                set
                {
                    if (_visible == value) return;
                    _visible = value;
                    if (PropertyChanged != null)
                        PropertyChanged(this, new PropertyChangedEventArgs("IsVisible"));
                }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private sealed class NavTreeNode
        {
            public string Title { get; set; }
            public string Category { get; set; }
            public string Key { get; set; }
            public bool IsCategory { get; set; }
            public string KindText { get { return IsCategory ? "分类" : "工具"; } }
            public Thickness IndentMargin
            {
                get { return new Thickness(IsCategory ? 6 : 28, 0, 8, 0); }
            }
        }

        private sealed class NavSortGroup
        {
            public string Category { get; set; }
            public List<NavSortItem> Items { get; set; }

            public NavSortGroup()
            {
                Items = new List<NavSortItem>();
            }
        }

        private sealed class NavSortItem
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
        }
    }
}
