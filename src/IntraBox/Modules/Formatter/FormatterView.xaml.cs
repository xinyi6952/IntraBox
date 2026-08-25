using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// 多格式格式化视图：粘贴/输入即自动格式化，切换格式或缩进自动重新格式化。
    /// 仅保留「复制结果」一个主操作。语法错误以红字非阻断提示。
    /// </summary>
    public partial class FormatterView : UserControl, IModuleView
    {
        private readonly List<ITextFormatter> _formatters = new List<ITextFormatter>
        {
            new JsonFormatter(),
            new YamlFormatter(),
            new XmlFormatter(),
            new SqlFormatter(),
            new HtmlFormatter(),
            new CssFormatter(),
            new MarkdownFormatter(),
            new NginxFormatter()
        };

        // 防抖：停止输入 300ms 后自动格式化，避免每敲一个字就重排
        private readonly DispatcherTimer _debounce = new DispatcherTimer();
        private bool _restoring;

        public FormatterView()
        {
            InitializeComponent();
            FormatCombo.ItemsSource = _formatters;
            FormatCombo.DisplayMemberPath = "FormatName";
            FormatCombo.SelectedIndex = 0;

            _debounce.Interval = TimeSpan.FromMilliseconds(300);
            _debounce.Tick += (s, e) => { _debounce.Stop(); Run(); };

            InputBox.TextChangedByUser += (s, e) =>
            {
                if (_restoring) return;
                _debounce.Stop();
                _debounce.Start();
            };
            FormatCombo.SelectionChanged += (s, e) => { if (!_restoring) Run(); };
            IndentCombo.SelectionChanged += (s, e) => { if (!_restoring) Run(); };
            OutputBox.MaximizeToggle += (s, e) => ToggleOutputMax();
            ApplyHighlighting();
        }

        private void ToggleOutputMax()
        {
            bool on = !OutputBox.IsMaximized;
            InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
            OutputBox.IsMaximized = on;
        }

        private ITextFormatter Current => FormatCombo.SelectedItem as ITextFormatter;

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            if (_restoring) return;
            Run();
        }

        private void Run()
        {
            if (_restoring) return;
            var f = Current;
            if (f == null) return;
            ApplyHighlighting();

            // 输入为空：清空输出与错误，不提示（初始状态）
            if (string.IsNullOrWhiteSpace(InputBox.Text))
            {
                OutputBox.Text = "";
                ShowError("");
                return;
            }

            string sizeErr;
            if (!SizeLimits.TryCheckText(InputBox.Text, out sizeErr))
            {
                ShowError(sizeErr);
                return;
            }

            string error;
            string result = (CompressCheck.IsChecked == true)
                ? f.Minify(InputBox.Text, out error)
                : f.Beautify(InputBox.Text, GetIndent(), out error);

            if (error != null)
            {
                ShowError("错误：" + error);
                return;
            }

            ShowError("");
            OutputBox.Text = result;
        }

        private void ApplyHighlighting()
        {
            string name = null;
            var f = Current;
            if (f != null)
            {
                switch (f.FormatName)
                {
                    case "JSON": name = "JavaScript"; break;
                    case "XML": name = "XML"; break;
                    case "HTML": name = "HTML"; break;
                    case "CSS": name = "CSS"; break;
                    case "SQL": name = "TSQL"; break;
                    case "Markdown": name = "MarkDown"; break;
                }
            }
            InputBox.SetHighlightingByName(name);
            OutputBox.SetHighlightingByName(name);
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputBox.Text))
            {
                string err;
                if (!ClipboardHelper.TrySetText(OutputBox.Text, out err))
                    ShowError(err);
                else
                    ShowError("");
            }
        }

        private string GetIndent()
        {
            switch (IndentCombo.SelectedIndex)
            {
                case 1: return "  ";    // 2 空格
                case 2: return "\t";    // Tab
                default: return "    "; // 4 空格（默认）
            }
        }

        private void ShowError(string msg)
        {
            if (string.IsNullOrEmpty(msg))
            {
                ErrorText.Text = "";
                ErrorText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text = msg;
                ErrorText.Visibility = Visibility.Visible;
            }
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("formatter", out state)) return;

            // 先恢复选项再灌文本，最后只格式化一次，避免切换抖动
            _restoring = true;
            SetComboIndex(FormatCombo, HistoryManager.GetInt(state, "format", 0));
            SetComboIndex(IndentCombo, HistoryManager.GetInt(state, "indent", 0));
            CompressCheck.IsChecked = HistoryManager.GetBool(state, "compress", false);
            InputBox.Text = HistoryManager.GetString(state, "input");
            _restoring = false;
            Run();
        }

        public void OnDeactivated()
        {
            _debounce.Stop();
            HistoryManager.Save("formatter", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "format", FormatCombo.SelectedIndex },
                { "indent", IndentCombo.SelectedIndex },
                { "compress", CompressCheck.IsChecked == true }
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
