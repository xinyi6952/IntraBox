using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntraBox.Modules.JsonToClass
{
    /// <summary>
    /// JSON 转类：推断字段类型，生成 C# 或 Java 模型。类型推断纯逻辑已抽到 Core.JsonToClassHelper / ClassEmitter。
    /// </summary>
    public partial class JsonToClassView : UserControl, IModuleView
    {
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

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var json = InputBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(json))
            {
                SetMsg("请粘贴 JSON", true);
                return;
            }

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                SetMsg("JSON 非法：" + ex.Message, true);
                return;
            }
            catch (Exception ex)
            {
                SetMsg("解析失败：" + ex.Message, true);
                return;
            }

            try
            {
                var root = JsonToClassHelper.SanitizeIdent(RootNameBox.Text, "Root");
                bool java = LangCombo.SelectedIndex == 1;
                OutputBox.SetHighlightingByName(java ? "Java" : "C#");
                OutputBox.Text = new ClassEmitter(java).Emit(token, root);
                SetMsg("已生成", false);
            }
            catch (Exception ex)
            {
                SetMsg("生成失败：" + ex.Message, true);
            }
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
