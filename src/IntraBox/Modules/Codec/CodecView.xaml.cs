using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Codec
{
    /// <summary>
    /// URL / HTML / Unicode 编解码。输入变更或切换类型后即时转换。纯逻辑已抽到 Core.CodecHelper。
    /// </summary>
    public partial class CodecView : UserControl, IModuleView
    {
        private bool _ready;

        public CodecView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
            InputBox.TextChangedByUser += (s, e) => ConvertNow(false);
            _ready = true;
        }

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) ConvertNow(false);
        }

        private void Convert_Click(object sender, RoutedEventArgs e)
        {
            ConvertNow(true);
        }

        private void ConvertNow(bool fromButton)
        {
            if (InputBox == null || OutputBox == null) return;
            var input = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(input))
            {
                OutputBox.Text = "";
                if (fromButton) SetMsg("输入不能为空", true);
                else MsgText.Text = "";
                return;
            }

            try
            {
                OutputBox.Text = CodecHelper.Transform(input, TypeCombo == null ? 0 : TypeCombo.SelectedIndex);
                SetMsg("已转换", false);
            }
            catch (Exception ex)
            {
                SetMsg("转换失败：" + ex.Message, true);
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
            if (!HistoryManager.TryLoad("codec", out state)) return;
            _ready = false;
            SetComboIndex(TypeCombo, HistoryManager.GetInt(state, "type", 0));
            InputBox.Text = HistoryManager.GetString(state, "input");
            _ready = true;
            ConvertNow(false);
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("codec", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "type", TypeCombo.SelectedIndex }
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
