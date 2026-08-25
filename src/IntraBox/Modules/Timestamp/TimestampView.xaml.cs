using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Timestamp
{
    /// <summary>
    /// 时间戳转换：Unix 时间戳（秒/毫秒）与可读时间互转。
    /// 容错：非法数字/日期以非阻断方式提示。
    /// </summary>
    public partial class TimestampView : UserControl, IModuleView
    {
        public TimestampView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
        }

        // 当前单位是否为「秒」（index 0 = 秒，1 = 毫秒）
        private bool IsSeconds => UnitCombo.SelectedIndex == 0;

        // 时间戳 → 时间
        private void TsToTime_Click(object sender, RoutedEventArgs e)
        {
            ClearMsg();
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空"); return; }
            long v;
            if (!long.TryParse(InputBox.Text.Trim(), out v))
            {
                SetMsg("错误：请输入合法的整数时间戳");
                return;
            }

            try
            {
                var dto = IsSeconds
                    ? DateTimeOffset.FromUnixTimeSeconds(v)
                    : DateTimeOffset.FromUnixTimeMilliseconds(v);
                OutputBox.Text = dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (ArgumentOutOfRangeException)
            {
                SetMsg("错误：时间戳超出有效范围");
            }
        }

        // 时间 → 时间戳
        private void TimeToTs_Click(object sender, RoutedEventArgs e)
        {
            ClearMsg();
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空"); return; }
            DateTime dt;
            if (!DateTime.TryParse(InputBox.Text.Trim(), out dt))
            {
                SetMsg("错误：无法解析为时间，请使用如 2026-08-22 12:30:00 的格式");
                return;
            }

            var dto = new DateTimeOffset(dt);
            OutputBox.Text = IsSeconds
                ? dto.ToUnixTimeSeconds().ToString()
                : dto.ToUnixTimeMilliseconds().ToString();
        }

        // 填入当前时间
        private void FillNow_Click(object sender, RoutedEventArgs e)
        {
            ClearMsg();
            InputBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void SetMsg(string text)
        {
            MsgText.Text = text;
        }

        private void ClearMsg()
        {
            MsgText.Text = "";
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("timestamp", out state)) return;
            SetComboIndex(UnitCombo, HistoryManager.GetInt(state, "unit", 0));
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("timestamp", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "unit", UnitCombo.SelectedIndex }
            });
        }

        private static void SetComboIndex(ComboBox combo, int index)
        {
            if (combo == null || combo.Items.Count == 0) return;
            if (index < 0 || index >= combo.Items.Count) return;
            combo.SelectedIndex = index;
        }
    }
}
