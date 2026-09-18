using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.EyeCare
{
    public partial class EyeCareView : UserControl, IModuleView
    {
        private bool _syncing = true;
        private readonly List<SegRow> _rows = new List<SegRow>();
        private readonly DispatcherTimerShim _saveDelay;

        public EyeCareView()
        {
            InitializeComponent();
            _saveDelay = new DispatcherTimerShim();
        }

        public void OnActivated()
        {
            EyeCareService.Changed += OnChanged;
            _syncing = true;
            LoadFromSettings();
            _syncing = false;
            SyncStatus();
        }

        public void OnDeactivated()
        {
            EyeCareService.Changed -= OnChanged;
            _saveDelay.Stop();
            TryCommitRows();
        }

        private void OnChanged(object sender, EventArgs e)
        {
            SyncStatus();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            TryCommitRows();
            if (!EyeCareService.Enabled)
            {
                string prompt = EyeCareHelper.BuildConflictPrompt(EyeCareConflict.DetectRunning());
                if (prompt != null && !ConfirmHelper.Warn(prompt, "护眼模式"))
                    return;
            }
            EyeCareService.SetEnabled(!EyeCareService.Enabled);
            SyncStatus();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            EyeCareService.TogglePause();
            SyncStatus();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmHelper.Action(
                "将时段与用眼提醒恢复为默认自动三段（含白天），并取消暂停。护眼开关不变。继续？",
                "恢复默认设置"))
                return;
            _saveDelay.Stop();
            _syncing = true;
            EyeCareService.RestoreDefaults();
            LoadFromSettings();
            _syncing = false;
            SyncStatus();
        }

        private void Rest_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            EyeCareService.SetRestRemind(RestCheck.IsChecked == true);
            SyncStatus();
        }

        private void RestPreview_Click(object sender, RoutedEventArgs e)
        {
            EyeCareRestWindow.ShowOnce(true);
        }

        private void AddSeg_Click(object sender, RoutedEventArgs e)
        {
            var draft = ReadRows();
            EyeCareSegment neu;
            if (!EyeCareHelper.TrySuggestNew(draft, out neu))
            {
                FormHint.Apply(SegmentHint, "无法添加时段，请先缩短或删除现有段。");
                return;
            }
            if (draft.Count >= EyeCareHelper.MaxSegments)
            {
                FormHint.Apply(SegmentHint, "不能保存：最多 " + EyeCareHelper.MaxSegments + " 段。");
                return;
            }
            draft.Add(neu);
            RebuildRows(draft);
            TryCommitRows();
        }

        private void LoadFromSettings()
        {
            var s = ConfigManager.Instance.Settings;
            if (EyeCareHelper.EnsureSegments(s, false))
                ConfigManager.Instance.Save();
            RestCheck.IsChecked = s.EyeCareRestRemind;
            RebuildRows(EyeCareHelper.CloneSegments(s.EyeCareSegments));
            FormHint.Apply(SegmentHint, "", false);
        }

        private void RebuildRows(IList<EyeCareSegment> segs)
        {
            _rows.Clear();
            SegmentHost.Children.Clear();
            if (segs == null) return;
            bool prev = _syncing;
            _syncing = true;
            for (int i = 0; i < segs.Count; i++)
            {
                EyeCareSegment seg = segs[i];
                if (seg == null) continue;
                var row = new SegRow();
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                var top = new WrapPanel();
                row.On = new CheckBox
                {
                    Content = "启用",
                    Margin = new Thickness(0, 4, 12, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = !seg.Disabled
                };
                row.On.Checked += Enable_Changed;
                row.On.Unchecked += Enable_Changed;
                top.Children.Add(row.On);
                top.Children.Add(Label("开始"));
                row.Start = TimeBox(seg.StartHm);
                top.Children.Add(row.Start);
                top.Children.Add(Label("结束"));
                row.End = TimeBox(seg.EndHm);
                top.Children.Add(row.End);
                row.Kind = KindCombo(EyeCareHelper.MatchPresetName(seg.StartHm, seg.EndHm));
                top.Children.Add(row.Kind);
                var del = new Button { Content = "删除", MinWidth = 64, Margin = new Thickness(8, 4, 8, 4), Tag = i };
                del.Click += Delete_Click;
                top.Children.Add(del);
                block.Children.Add(top);

                row.KelvinLabel = new TextBlock
                {
                    Width = 72,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = EyeCareHelper.NormalizeKelvin(seg.Kelvin) + " K"
                };
                row.Kelvin = MakeSlider(EyeCareHelper.MinKelvin, EyeCareHelper.MaxKelvin, 100, 500, 100,
                    EyeCareHelper.NormalizeKelvin(seg.Kelvin));
                block.Children.Add(SliderLine("色温", row.Kelvin, row.KelvinLabel));

                row.BrightLabel = new TextBlock
                {
                    Width = 72,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = EyeCareHelper.NormalizeBrightness(seg.Brightness) + "%"
                };
                row.Bright = MakeSlider(EyeCareHelper.MinBrightness, EyeCareHelper.MaxBrightness, 5, 10, 5,
                    EyeCareHelper.NormalizeBrightness(seg.Brightness));
                block.Children.Add(SliderLine("亮度", row.Bright, row.BrightLabel));

                _rows.Add(row);
                ApplyRowEnabled(row);
                SegmentHost.Children.Add(block);
            }
            _syncing = prev;
        }

        private ComboBox KindCombo(string selected)
        {
            var box = new ComboBox { Width = 88, Margin = new Thickness(8, 4, 8, 4) };
            box.Items.Add(new ComboBoxItem { Content = EyeCareHelper.PresetDayName });
            box.Items.Add(new ComboBoxItem { Content = EyeCareHelper.PresetEveningName });
            box.Items.Add(new ComboBoxItem { Content = EyeCareHelper.PresetNightName });
            box.Items.Add(new ComboBoxItem { Content = EyeCareHelper.PresetCustomName });
            SelectCombo(box, selected);
            box.SelectionChanged += Kind_Changed;
            return box;
        }

        private static void SelectCombo(ComboBox box, string name)
        {
            if (box == null) return;
            for (int i = 0; i < box.Items.Count; i++)
            {
                var item = box.Items[i] as ComboBoxItem;
                if (item != null && string.Equals(item.Content as string, name, StringComparison.Ordinal))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            box.SelectedIndex = box.Items.Count - 1;
        }

        private static string ComboText(ComboBox box)
        {
            var item = box != null ? box.SelectedItem as ComboBoxItem : null;
            return item != null ? item.Content as string : EyeCareHelper.PresetCustomName;
        }

        private Slider MakeSlider(double min, double max, double small, double large, double tick, double value)
        {
            var sl = new Slider
            {
                Minimum = min,
                Maximum = max,
                SmallChange = small,
                LargeChange = large,
                TickFrequency = tick,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Value = value,
                Margin = new Thickness(0, 4, 8, 4)
            };
            sl.ValueChanged += Slider_Changed;
            return sl;
        }

        private static DockPanel SliderLine(string title, Slider slider, TextBlock value)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(value, Dock.Right);
            dock.Children.Add(value);
            var caption = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Width = 40
            };
            dock.Children.Add(caption);
            dock.Children.Add(slider);
            return dock;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
        }

        private TextBox TimeBox(string text)
        {
            var box = new TextBox { Width = 72, Margin = new Thickness(0, 4, 8, 4), Text = text ?? "" };
            box.LostFocus += Row_LostFocus;
            return box;
        }

        private void Row_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            SyncRowKindFromTimes();
            _saveDelay.Run(delegate { TryCommitRows(); });
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            UpdateSliderLabels();
            _saveDelay.Run(delegate { TryCommitRows(); });
        }

        private void Kind_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            var box = sender as ComboBox;
            int idx = IndexOfKind(box);
            if (idx < 0) return;
            string name = ComboText(box);
            EyeCareSegment preset = EyeCareHelper.SegmentFromPreset(name);
            if (preset == null) return;
            _syncing = true;
            _rows[idx].Start.Text = preset.StartHm;
            _rows[idx].End.Text = preset.EndHm;
            _rows[idx].Kelvin.Value = preset.Kelvin;
            _rows[idx].Bright.Value = preset.Brightness;
            UpdateSliderLabels();
            _syncing = false;
            TryCommitRows();
        }

        private int IndexOfKind(ComboBox box)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Kind == box) return i;
            }
            return -1;
        }

        private void SyncRowKindFromTimes()
        {
            _syncing = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                SegRow row = _rows[i];
                if (row.Kind == null || row.Start == null || row.End == null) continue;
                SelectCombo(row.Kind, EyeCareHelper.MatchPresetName(row.Start.Text, row.End.Text));
            }
            _syncing = false;
        }

        private void UpdateSliderLabels()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                SegRow row = _rows[i];
                if (row.KelvinLabel != null && row.Kelvin != null)
                    row.KelvinLabel.Text = ((int)Math.Round(row.Kelvin.Value)).ToString(CultureInfo.InvariantCulture) + " K";
                if (row.BrightLabel != null && row.Bright != null)
                    row.BrightLabel.Text = ((int)Math.Round(row.Bright.Value)).ToString(CultureInfo.InvariantCulture) + "%";
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            int idx;
            if (btn == null || btn.Tag == null
                || !int.TryParse(btn.Tag.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                return;
            var draft = ReadRows();
            if (idx < 0 || idx >= draft.Count) return;
            if (draft.Count <= 1)
            {
                FormHint.Apply(SegmentHint, "请至少保留一段。", true);
                return;
            }
            string detail = "时段 " + EyeCareHelper.FormatRange(draft[idx].StartHm, draft[idx].EndHm);
            if (!ConfirmHelper.Delete(detail)) return;
            draft.RemoveAt(idx);
            RebuildRows(draft);
            TryCommitRows();
        }

        private List<EyeCareSegment> ReadRows()
        {
            var list = new List<EyeCareSegment>();
            for (int i = 0; i < _rows.Count; i++)
                list.Add(_rows[i].ToSegment());
            return list;
        }

        private void Enable_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            var box = sender as CheckBox;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].On == box)
                {
                    ApplyRowEnabled(_rows[i]);
                    break;
                }
            }
            TryCommitRows();
        }

        private static void ApplyRowEnabled(SegRow row)
        {
            if (row == null) return;
            bool on = row.On == null || row.On.IsChecked == true;
            if (row.Start != null) row.Start.IsEnabled = on;
            if (row.End != null) row.End.IsEnabled = on;
            if (row.Kind != null) row.Kind.IsEnabled = on;
            if (row.Kelvin != null) row.Kelvin.IsEnabled = on;
            if (row.Bright != null) row.Bright.IsEnabled = on;
        }

        private bool TryCommitRows()
        {
            if (_syncing) return true;
            var draft = ReadRows();
            string err;
            if (!EyeCareService.TrySaveSegments(draft, out err))
            {
                FormHint.Apply(SegmentHint, err ?? "时段无效。", true);
                SyncStatus();
                return false;
            }
            string hint = EyeCareHelper.HasCoverageGap(draft)
                ? "未覆盖的时段会恢复原色。"
                : (EyeCareHelper.IsDefaultSegments(draft) ? "当前为默认自动三段。" : "");
            FormHint.Apply(SegmentHint, hint, false);
            SyncStatus();
            return true;
        }

        private void SyncStatus()
        {
            bool on = EyeCareService.Enabled;
            ToggleBtn.Content = on ? "关闭护眼" : "开启护眼";
            TimeSpan pause = EyeCareService.PauseRemaining;
            bool pausing = on && pause.TotalSeconds >= 1;
            PauseBtn.IsEnabled = on;
            PauseBtn.Content = pausing ? "取消暂停" : "暂停 1 小时";
            SyncRestText();

            if (!on)
            {
                FormHint.Apply(StateText, "当前：未开启。");
                return;
            }
            if (pausing)
            {
                FormHint.Apply(StateText, "已暂停 剩余 " + EyeCareHelper.FormatPauseClock(pause) + "，点「取消暂停」可恢复。");
                return;
            }
            if (!EyeCareService.LastApplyOk && !string.IsNullOrEmpty(EyeCareService.LastError))
            {
                FormHint.Apply(StateText, EyeCareService.LastError, true);
                return;
            }

            var s = ConfigManager.Instance.Settings;
            var segs = s.EyeCareSegments;
            string mode = EyeCareHelper.IsDefaultSegments(segs) ? "自动调节" : ("自定义 " + (segs == null ? 0 : segs.Count) + " 段");
            EyeCareSegment hit;
            if (EyeCareHelper.TryPickSegment(DateTime.Now, segs, out hit))
            {
                string line = mode + " · 当前 " + EyeCareHelper.FormatRange(hit.StartHm, hit.EndHm)
                    + " · " + EyeCareHelper.NormalizeKelvin(hit.Kelvin) + " K · 亮度 "
                    + EyeCareHelper.NormalizeBrightness(hit.Brightness) + "%";
                EyeCareSegment next;
                if (EyeCareHelper.TryNextSegment(DateTime.Now, segs, out next))
                    line += " · 下一段 " + EyeCareHelper.FormatRange(next.StartHm, next.EndHm);
                FormHint.Apply(StateText, line, false);
            }
            else
                FormHint.Apply(StateText, mode + " · 当前不在时段内，屏幕未改色。", false);
        }

        private void SyncRestText()
        {
            if (RestText == null) return;
            var s = ConfigManager.Instance.Settings;
            bool remind = s != null && s.EyeCareRestRemind;
            RestText.Text = EyeCareHelper.FormatRestStatus(
                remind, EyeCareService.RestCounting, EyeCareService.RestRemaining);
        }

        private sealed class SegRow
        {
            public CheckBox On;
            public TextBox Start;
            public TextBox End;
            public ComboBox Kind;
            public Slider Kelvin;
            public Slider Bright;
            public TextBlock KelvinLabel;
            public TextBlock BrightLabel;

            public EyeCareSegment ToSegment()
            {
                return new EyeCareSegment
                {
                    StartHm = Start != null ? Start.Text : "",
                    EndHm = End != null ? End.Text : "",
                    Kelvin = Kelvin != null ? (int)Math.Round(Kelvin.Value) : 0,
                    Brightness = Bright != null ? (int)Math.Round(Bright.Value) : 0,
                    Disabled = On != null && On.IsChecked != true
                };
            }
        }

        private sealed class DispatcherTimerShim
        {
            private System.Windows.Threading.DispatcherTimer _t;
            private Action _act;

            public void Run(Action act)
            {
                _act = act;
                if (_t == null)
                {
                    _t = new System.Windows.Threading.DispatcherTimer();
                    _t.Interval = TimeSpan.FromMilliseconds(280);
                    _t.Tick += delegate
                    {
                        _t.Stop();
                        Action a = _act;
                        if (a != null) a();
                    };
                }
                _t.Stop();
                _t.Start();
            }

            public void Stop()
            {
                if (_t != null) _t.Stop();
            }
        }
    }
}
