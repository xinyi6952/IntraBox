using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Codec
{
    /// <summary>
    /// URL / HTML / Unicode 编解码。输入变更或切换类型后即时转换。纯逻辑已抽到 Core.CodecHelper。
    /// </summary>
    public partial class CodecView : UserControl, IModuleView
    {
        // 编解码是纯内存字符串变换（微秒级），无需后台线程；同步执行避免「取消+版本号」误丢结果。
        private bool _ready;
        private readonly DispatcherTimer _debounce = new DispatcherTimer();

        public CodecView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
            // 输入变化走防抖，避免每敲一字就全量转换
            _debounce.Interval = TimeSpan.FromMilliseconds(300);
            _debounce.Tick += (s, e) => { _debounce.Stop(); ConvertNow(false); };
            InputBox.TextChangedByUser += (s, e) =>
            {
                if (!_ready) return;
                _debounce.Stop();
                _debounce.Start();
            };
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

            int type = TypeCombo == null ? 0 : TypeCombo.SelectedIndex;

            try
            {
                OutputBox.Text = CodecHelper.Transform(input, type);
                SetMsg("已转换", false);
            }
            catch (Exception ex)
            {
                SetMsg("转换失败：" + ex.RootMessage(), true);
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
            _debounce.Stop();
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
