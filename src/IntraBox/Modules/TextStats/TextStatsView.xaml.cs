using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.TextStats
{
    /// <summary>文本统计：字符/词/行/段落/词频/字符频率。纯逻辑已抽到 Core.TextStatsHelper。</summary>
    public partial class TextStatsView : UserControl, IModuleView
    {
        public TextStatsView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("textstats", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("textstats", new Dictionary<string, object> { { "input", InputBox.Text ?? "" } });
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            string sizeErr;
            if (!SizeLimits.TryCheckText(InputBox.Text ?? "", out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }
            var text = InputBox.Text ?? "";
            int top;
            if (!int.TryParse((TopBox.Text ?? "").Trim(), out top) || top < 1) top = 20;
            if (top > 200) top = 200;

            var r = TextStatsHelper.Analyze(text);

            var sb = new StringBuilder();
            sb.AppendLine("字符数（含空白）\t" + r.Chars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("字符数（不含空白）\t" + r.CharsNoWs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("词数\t" + r.Words.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("行数\t" + r.Lines.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("段落数\t" + r.Paragraphs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("词频 Top " + top);
            AppendTop(sb, ToPairs(r.WordFreq), top);
            sb.AppendLine();
            sb.AppendLine("字符频率 Top " + top);
            var cp = new List<KeyValuePair<string, int>>();
            foreach (var kv in r.CharFreq)
                cp.Add(new KeyValuePair<string, int>(kv.Key.ToString(), kv.Value));
            AppendTop(sb, cp, top);
            OutputBox.Text = sb.ToString();
            SetMsg("已统计", false);
        }

        private static List<KeyValuePair<string, int>> ToPairs(Dictionary<string, int> d)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in d) list.Add(kv);
            return list;
        }

        private static void AppendTop(StringBuilder sb, List<KeyValuePair<string, int>> list, int top)
        {
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            int n = Math.Min(top, list.Count);
            for (int i = 0; i < n; i++)
                sb.AppendLine(list[i].Key + "\t" + list[i].Value.ToString(CultureInfo.InvariantCulture));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text ?? "", out err)) SetMsg("已复制", false);
            else SetMsg(err, true);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
