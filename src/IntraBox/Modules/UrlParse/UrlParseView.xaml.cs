using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.UrlParse
{
    /// <summary>URL 解析：拆出协议/主机/端口/路径/Query/Fragment 及 query 参数。纯逻辑已抽到 Core.UrlParseHelper。</summary>
    public partial class UrlParseView : UserControl, IModuleView
    {
        public UrlParseView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("urlparse", out state))
                InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("urlparse", new Dictionary<string, object> { { "input", InputBox.Text ?? "" } });
        }

        private void Parse_Click(object sender, RoutedEventArgs e)
        {
            List<UrlPart> rows;
            string err;
            if (!UrlParseHelper.TryParse(InputBox.Text, out rows, out err))
            {
                SetMsg(err, true);
                ResultGrid.ItemsSource = null;
                return;
            }
            ResultGrid.ItemsSource = rows;
            SetMsg("已解析 " + rows.Count + " 项", false);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultGrid.ItemsSource as List<UrlPart>;
            if (rows == null || rows.Count == 0)
            {
                SetMsg("没有可复制的内容", true);
                return;
            }
            var sb = new StringBuilder();
            foreach (var r in rows) sb.Append(r.Key).Append('\t').AppendLine(r.Value);
            string err;
            if (ClipboardHelper.TrySetText(sb.ToString(), out err)) SetMsg("已复制", false);
            else SetMsg(err, true);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
