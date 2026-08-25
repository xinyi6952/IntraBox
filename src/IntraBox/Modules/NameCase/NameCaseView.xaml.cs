using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.NameCase
{
    /// <summary>
    /// 命名转换：驼峰 / 帕斯卡 / 下划线 / 短横线 / 大小写互转，实时输出全部风格。
    /// 纯逻辑已抽到 Core.NameCaseHelper。
    /// </summary>
    public partial class NameCaseView : UserControl, IModuleView
    {
        public NameCaseView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
            InputBox.TextChangedByUser += (s, e) => ConvertNow();
        }

        private void ConvertNow()
        {
            var input = InputBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(input))
            {
                OutputBox.Text = "";
                MsgText.Text = "";
                return;
            }
            OutputBox.Text = NameCaseHelper.FormatAll(input);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputBox.Text)) return;
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text, out err))
            {
                MsgText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
                MsgText.Text = "已复制";
            }
            else
            {
                MsgText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
                MsgText.Text = err;
            }
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("namecase", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
            ConvertNow();
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("namecase", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
        }
    }
}
