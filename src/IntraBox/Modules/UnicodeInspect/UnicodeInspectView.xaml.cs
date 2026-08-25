using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.UnicodeInspect
{
    /// <summary>Unicode 字符检查：码点/UTF-8/UTF-16/HTML 实体/类别。纯逻辑已抽到 Core.UnicodeInspectHelper。</summary>
    public partial class UnicodeInspectView : UserControl, IModuleView
    {
        public UnicodeInspectView() { InitializeComponent(); }

        public void OnActivated()
        {
            Dictionary<string, object> st;
            if (HistoryManager.TryLoad("unicodeinspect", out st))
                InputBox.Text = HistoryManager.GetString(st, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("unicodeinspect", new Dictionary<string, object> { { "input", InputBox.Text ?? "" } });
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            var rows = UnicodeInspectHelper.Analyze(InputBox.Text);
            ResultGrid.ItemsSource = rows;
            MsgText.Text = "共 " + rows.Count + " 个码点";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultGrid.ItemsSource as List<UnicodeCodePoint>;
            if (rows == null) return;
            var sb = new StringBuilder();
            foreach (var r in rows)
                sb.AppendLine(r.Ch + "\t" + r.Code + "\t" + r.Utf8 + "\t" + r.Utf16 + "\t" + r.Html + "\t" + r.Cat);
            string err;
            ClipboardHelper.TrySetText(sb.ToString(), out err);
        }
    }
}
