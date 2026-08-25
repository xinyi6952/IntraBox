using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.KeepAwake
{
    public partial class KeepAwakeView : UserControl, IModuleView
    {
        public KeepAwakeView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            LoadHistory();
            KeepAwakeService.Changed += OnServiceChanged;
            Sync();
        }

        public void OnDeactivated()
        {
            KeepAwakeService.Changed -= OnServiceChanged;
            SaveHistory();
        }

        private void OnServiceChanged(object sender, EventArgs e)
        {
            Sync();
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            string tag = btn.Tag.ToString();
            int p = tag.IndexOf('|');
            if (p <= 0) return;
            AmountBox.Text = tag.Substring(0, p);
            SelectUnit(tag.Substring(p + 1));
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (KeepAwakeService.IsEnabled)
            {
                KeepAwakeService.SetEnabled(false);
                Sync();
                return;
            }

            TimeSpan span;
            string error;
            if (!KeepAwakeService.TryParseDuration(AmountBox.Text, SelectedUnit(), out span, out error))
            {
                StateText.Text = error;
                StateText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
                return;
            }
            KeepAwakeService.Start(span);
            SaveHistory();
            Sync();
        }

        private void Sync()
        {
            bool on = KeepAwakeService.IsEnabled;
            ToggleBtn.Content = on ? "关闭防止睡眠" : "开启防止睡眠";
            DurationPanel.IsEnabled = !on;
            AmountBox.IsEnabled = !on;
            UnitCombo.IsEnabled = !on;

            if (!on)
            {
                StateText.Text = "当前：未阻止";
                StateText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
                return;
            }

            TimeSpan left = KeepAwakeService.Remaining;
            string until = "";
            DateTime? t = KeepAwakeService.UntilLocal;
            if (t.HasValue)
                until = "，将于 " + t.Value.ToString("HH:mm") + " 自动关闭";
            StateText.Text = "当前：已阻止睡眠，" + FormatRemaining(left) + until;
            StateText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
        }

        private static string FormatRemaining(TimeSpan left)
        {
            int sec = (int)Math.Ceiling(left.TotalSeconds);
            if (sec < 0) sec = 0;
            int h = sec / 3600;
            int m = (sec % 3600) / 60;
            int s = sec % 60;
            if (h > 0) return "剩余 " + h + " 小时 " + m + " 分 " + s + " 秒";
            if (m > 0) return "剩余 " + m + " 分 " + s + " 秒";
            return "剩余 " + s + " 秒";
        }

        private string SelectedUnit()
        {
            var item = UnitCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Content != null)
                return item.Content.ToString();
            return "分钟";
        }

        private void SelectUnit(string unit)
        {
            for (int i = 0; i < UnitCombo.Items.Count; i++)
            {
                var item = UnitCombo.Items[i] as ComboBoxItem;
                if (item == null || item.Content == null) continue;
                if (string.Equals(item.Content.ToString(), unit, StringComparison.Ordinal))
                {
                    UnitCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void LoadHistory()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("keepawake", out state)) return;
            string amount = HistoryManager.GetString(state, "amount");
            if (!string.IsNullOrWhiteSpace(amount))
                AmountBox.Text = amount;
            string unit = HistoryManager.GetString(state, "unit");
            if (!string.IsNullOrWhiteSpace(unit))
                SelectUnit(unit);
        }

        private void SaveHistory()
        {
            HistoryManager.Save("keepawake", new Dictionary<string, object>
            {
                { "amount", AmountBox.Text ?? "" },
                { "unit", SelectedUnit() }
            });
        }
    }
}
