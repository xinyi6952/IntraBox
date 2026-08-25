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

        private string[] Lines()
        {
            return LineProcessHelper.SplitLines(InputBox.Text);
        }

        private void SetOut(IList<string> lines, string msg)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(lines[i]);
            }
            OutputBox.Text = sb.ToString();
            MsgText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = msg + "，共 " + lines.Count + " 行";
        }

        private void Dedup_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.Deduplicate(Lines()), "已去重（保序）");
        }

        private void DedupSort_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.DeduplicateSort(Lines()), "已去重并排序");
        }

        private void Asc_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.SortAsc(Lines()), "已升序");
        }

        private void Desc_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.SortDesc(Lines()), "已降序");
        }

        private void Natural_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.SortNatural(Lines()), "已自然序");
        }

        private void Blank_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.RemoveBlank(Lines()), "已删空行");
        }

        private void Trim_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.TrimLines(Lines()), "已删首尾空白");
        }

        private void Affix_Click(object sender, RoutedEventArgs e)
        {
            SetOut(LineProcessHelper.AddAffix(Lines(), PrefixBox.Text, SuffixBox.Text), "已加前后缀");
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
