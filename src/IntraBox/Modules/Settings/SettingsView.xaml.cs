using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox.Modules.Settings
{
    /// <summary>应用设置：主题、文件大小上限、截屏热键、工具显隐。写入 config.json。</summary>
    public partial class SettingsView : UserControl, IModuleView, ILeaveGuard
    {
        private bool _loading;
        private bool _hkCtrl = true;
        private bool _hkAlt = true;
        private bool _hkShift;
        private int _hkVk = 0x41;
        private List<ToolVisItem> _toolItems;
        private int _loadedMb;
        private int _loadedClip;
        private string _loadedVis = "";

        public SettingsView()
        {
            InitializeComponent();
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
            LoadToolVisibility();
            int clip = AppSettings.ClampClipboardMax(s.ClipboardMaxItems);
            ClipSlider.Value = clip;
            UpdateClipLabel(clip);
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
            var r = ConfirmHelper.Unsaved("设置有未保存的更改（文件大小上限或工具显隐），是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return TryPersistSettings();
            return true;
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
            _loadedVis = CurrentVisKey();
        }

        private bool IsDirty()
        {
            int mb = SizeLimits.ClampMb((int)SizeSlider.Value);
            int clip = AppSettings.ClampClipboardMax((int)ClipSlider.Value);
            return mb != _loadedMb || clip != _loadedClip || CurrentVisKey() != _loadedVis;
        }

        private void LoadToolVisibility()
        {
            _toolItems = new List<ToolVisItem>();
            var tools = ToolVisibility.ToggleableTools();
            for (int i = 0; i < tools.Count; i++)
            {
                _toolItems.Add(new ToolVisItem
                {
                    Key = tools[i].Key,
                    DisplayName = tools[i].DisplayName,
                    IsVisible = ToolVisibility.IsVisible(tools[i].Key)
                });
            }
            ToolVisList.ItemsSource = _toolItems;
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
        }

        private void UpdateClipLabel(int n)
        {
            ClipLabel.Text = n + " 条";
        }

        private void Welcome_Click(object sender, RoutedEventArgs e)
        {
            var w = new IntraBox.WelcomeWindow();
            w.Owner = Window.GetWindow(this);
            w.ShowDialog();
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
            SizeSlider.Value = mb;
            ClipSlider.Value = clip;
            UpdateSizeLabel(mb);
            UpdateClipLabel(clip);
            ConfigManager.Instance.Settings.MaxFileSizeMb = mb;
            ConfigManager.Instance.Settings.ClipboardMaxItems = clip;
            ConfigManager.Instance.Settings.VisibleToolKeys = visible.ToArray();
            ThemeManager.Apply(ThemeName(ThemeCombo.SelectedIndex));
            ConfigManager.Instance.Save();
            ClipboardStore.TrimToLimit();
            RememberClean();

            var main = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (main != null) main.ReloadNav();

            MsgText.Text = "已保存。当前上限 " + mb + " MB，主题与工具显隐已记住。";
            return true;
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
    }
}
