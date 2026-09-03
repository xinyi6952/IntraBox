using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.UnicodeInspect
{
    /// <summary>Unicode 字符检查：码点/UTF-8/UTF-16/HTML 实体/类别。纯逻辑已抽到 Core.UnicodeInspectHelper。</summary>
    public partial class UnicodeInspectView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }


        public UnicodeInspectView() { InitializeComponent(); FilterBar.Attach(ResultGrid); }

        public void OnActivated()
        {
            Dictionary<string, object> st;
            if (HistoryManager.TryLoad("unicodeinspect", out st))
                InputBox.Text = HistoryManager.GetString(st, "input");
        }

        public void OnDeactivated()
        {
            CancelPending();
            HistoryManager.Save("unicodeinspect", new Dictionary<string, object> { { "input", InputBox.Text ?? "" } });
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text ?? "";
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            MsgText.Text = "分析中…";
            LoadingOverlay.Show(this, "分析中…");
            try
            {
                // 逐码点分析放后台线程，大文本不卡
                var rows = await Task.Run(() => UnicodeInspectHelper.Analyze(text), token);
                if (version != _gate.Version) return;
                ResultGrid.ItemsSource = rows;
                FilterBar.Apply();
                MsgText.Text = "共 " + rows.Count + " 个码点";
                LoadingOverlay.Hide(this);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) { MsgText.Text = "错误：" + ex.RootMessage(); LoadingOverlay.Hide(this); } }
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
