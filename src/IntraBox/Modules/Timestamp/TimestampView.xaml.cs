using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;

namespace IntraBox.Modules.Timestamp
{
    /// <summary>
    /// 时间转换：识别常见乱格式，列出本地/UTC/ISO/RFC/Unix/中文等可互抄写法。
    /// </summary>
    public partial class TimestampView : UserControl, IModuleView
    {
        private bool _loading;

        public TimestampView()
        {
            InitializeComponent();
            FilterBar.Attach(ResultGrid);
            InputBox.TextChangedByUser += (s, e) =>
            {
                if (!_loading) ConvertCore(false);
            };
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            _loading = true;
            if (HistoryManager.TryLoad("timestamp", out state))
                InputBox.Text = HistoryManager.GetString(state, "input");
            _loading = false;
            ConvertCore(false);
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("timestamp", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            ConvertCore(true);
        }

        private void FillNow_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            InputBox.Text = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _loading = false;
            ConvertCore(true);
        }

        private void ConvertCore(bool fromButton)
        {
            string text = InputBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultGrid.ItemsSource = null;
                if (fromButton) SetMsg("请填写时间", true);
                else SetMsg("", false);
                return;
            }

            TimeParseResult parsed;
            string err;
            if (!TimestampHelper.TryParse(text, DateTimeOffset.Now, out parsed, out err))
            {
                ResultGrid.ItemsSource = null;
                SetMsg(err, true);
                return;
            }

            TimeSpan localOff = TimeZoneInfo.Local.GetUtcOffset(parsed.Instant.UtcDateTime);
            var rows = TimestampHelper.FormatRows(parsed, localOff);
            ResultGrid.ItemsSource = rows;
            FilterBar.Apply();
            SetMsg("已识别为" + parsed.Kind + "，列出 " + rows.Count + " 种写法。", false);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            CopyRows(ResultGrid.ItemsSource as List<TimeFormatRow>);
        }

        private void Value_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var fe = sender as FrameworkElement;
            CopyValue(fe != null ? fe.DataContext as TimeFormatRow : null);
            e.Handled = true;
        }

        private void CopyValue(TimeFormatRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.Value)) return;
            string err;
            if (ClipboardHelper.TrySetText(row.Value, out err)) SetMsg("已复制 " + row.Name, false);
            else SetMsg(err, true);
        }

        private void CopyRows(List<TimeFormatRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                SetMsg("没有可复制的内容", true);
                return;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                sb.Append(r.Name).Append('\t').AppendLine(r.Value);
            }
            string err;
            if (ClipboardHelper.TrySetText(sb.ToString(), out err)) SetMsg("已复制", false);
            else SetMsg(err, true);
        }

        private void SetMsg(string t, bool err)
        {
            FormHint.Apply(MsgText, t, err);
        }
    }
}
