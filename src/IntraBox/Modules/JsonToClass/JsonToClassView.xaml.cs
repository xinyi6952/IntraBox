using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntraBox.Modules.JsonToClass
{
    /// <summary>
    /// JSON 转实体类：推断字段类型，生成 C# 或 Java 模型。类型推断纯逻辑已抽到 Core.JsonToClassHelper / ClassEmitter。
    /// </summary>
    public partial class JsonToClassView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        private bool _ready;

        public JsonToClassView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
            InputBox.SetHighlightingByName("JavaScript");
            OutputBox.SetHighlightingByName("C#");
            _ready = true;
        }

        private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            OutputBox.SetHighlightingByName(LangCombo.SelectedIndex == 1 ? "Java" : "C#");
            Generate_Click(null, null);
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            var json = InputBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(json))
            {
                SetMsg("请粘贴 JSON", true);
                return;
            }

            var root = JsonToClassHelper.SanitizeIdent(RootNameBox.Text, "Root");
            bool java = LangCombo.SelectedIndex == 1;

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "生成中…");

            string output;
            try
            {
                // JSON 解析 + 递归生成类放后台线程
                output = await Task.Run(() => new ClassEmitter(java).Emit(JToken.Parse(json), root), token);
            }
            catch (JsonReaderException ex) { if (version == _gate.Version) SetMsg("JSON 非法：" + ex.RootMessage(), true); return; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("生成失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;
            OutputBox.SetHighlightingByName(java ? "Java" : "C#");
            OutputBox.Text = output;
            SetMsg("已生成", false);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputBox.Text)) return;
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text, out err))
                SetMsg("已复制", false);
            else
                SetMsg(err, true);
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("jsontoclass", out state)) return;
            _ready = false;
            SetComboIndex(LangCombo, HistoryManager.GetInt(state, "lang", 0));
            var root = HistoryManager.GetString(state, "root");
            if (!string.IsNullOrEmpty(root)) RootNameBox.Text = root;
            InputBox.Text = HistoryManager.GetString(state, "input");
            _ready = true;
            OutputBox.SetHighlightingByName(LangCombo.SelectedIndex == 1 ? "Java" : "C#");
        }

        public void OnDeactivated()
        {
            CancelPending();
            HistoryManager.Save("jsontoclass", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "lang", LangCombo.SelectedIndex },
                { "root", RootNameBox.Text ?? "Root" }
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
