using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.LineProcess
{
    /// <summary>行处理：去重/排序/删空行/首尾空白/前后缀/自然序。纯逻辑已抽到 Core.LineProcessHelper。</summary>
    public partial class LineProcessView : UserControl, IModuleView
    {
        public LineProcessView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("lineprocess", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
            PrefixBox.Text = HistoryManager.GetString(state, "pre");
            SuffixBox.Text = HistoryManager.GetString(state, "suf");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("lineprocess", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "pre", PrefixBox.Text ?? "" },
                { "suf", SuffixBox.Text ?? "" }
            });
        }

        /// <summary>行处理是纯内存字符串操作（拆行+处理+拼接），毫秒级，同步执行即可，避免后台化引入的取消/版本号复杂度。</summary>
        private void Run(Func<string[], List<string>> op, string msg)
        {
            string text = InputBox.Text ?? "";
            try
            {
                var lines = LineProcessHelper.SplitLines(text);
                var processed = op(lines);
                var sb = new StringBuilder();
                for (int i = 0; i < processed.Count; i++)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append(processed[i]);
                }
                OutputBox.Text = sb.ToString();
                SetMsg(msg + "，共 " + processed.Count + " 行", false);
            }
            catch (Exception ex)
            {
                SetMsg("错误：" + ex.RootMessage(), true);
            }
        }

        private void Dedup_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.Deduplicate, "已去重（保序）");
        private void DedupSort_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.DeduplicateSort, "已去重并排序");
        private void Asc_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.SortAsc, "已升序");
        private void Desc_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.SortDesc, "已降序");
        private void Natural_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.SortNatural, "已自然序");
        private void Blank_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.RemoveBlank, "已删空行");
        private void Trim_Click(object sender, RoutedEventArgs e) => Run(LineProcessHelper.TrimLines, "已删首尾空白");

        private void Affix_Click(object sender, RoutedEventArgs e)
        {
            string prefix = PrefixBox.Text ?? "";
            string suffix = SuffixBox.Text ?? "";
            Run(lines => LineProcessHelper.AddAffix(lines, prefix, suffix), "已加前后缀");
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text ?? "", out err))
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
    }
}
