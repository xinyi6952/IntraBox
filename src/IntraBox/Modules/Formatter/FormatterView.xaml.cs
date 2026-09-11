using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// 格式化视图：粘贴/输入即自动格式化，切换格式或缩进自动重新格式化。
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

        // 大文本后台格式化：取消 + 版本号丢弃过期结果。仅当输入超过 LargeFormatChars 时启用，
        // 小文本仍同步执行（毫秒级，避免后台化在快速连输时误丢有效结果）。
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();
        private const int LargeFormatChars = 1_000_000;

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

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

            // 只读一次 InputBox.Text：每次读取都会把 AvalonEdit 的 Rope 拉平成完整字符串，大文本下重复读取代价极高
            string input = InputBox.Text;

            // 输入为空：清空输出与错误，不提示（初始状态）
            if (string.IsNullOrWhiteSpace(input))
            {
                OutputBox.Text = "";
                ShowError("");
                return;
            }

            string sizeErr;
            if (!SizeLimits.TryCheckText(input, out sizeErr))
            {
                ShowError(sizeErr);
                return;
            }

            // 格式化会生成多份全尺寸副本（输入串 + StringBuilder 输出 + 缩进转换 Split 数组），
            // 32 位进程 ~2GB 地址空间下超大文本会 OOM。这里用与文件上限解耦的硬顶拦截，避免闪退。
            int maxFmt = SizeLimits.MaxFormatChars;
            if (input.Length > maxFmt)
            {
                int mb = input.Length * 2 / 1024 / 1024;
                string hint = IntPtr.Size == 4
                    ? "当前为 32 位进程，地址空间有限，建议缩小输入或改用 64 位版本。"
                    : "文本过大，建议缩小输入。";
                ShowError("文本过大（约 " + Math.Max(1, mb) + " MB / " + input.Length + " 字符），已停止格式化以避免内存溢出。" + hint);
                OutputBox.Text = "";
                CancelPending();
                return;
            }

            bool compress = CompressCheck.IsChecked == true;
            string indent = GetIndent();

            // 小文本（<= 1M 字符）同步格式化：毫秒级，避免后台化在快速连输时误丢有效结果。
            // 大文本走后台 + 遮罩 + 取消，避免 UI 长时间未响应。
            if (input.Length <= LargeFormatChars)
            {
                FormatSync(f, input, compress, indent);
            }
            else
            {
                FormatLarge(f, input, compress, indent);
            }
        }

        private void FormatSync(ITextFormatter f, string input, bool compress, string indent)
        {
            CancelPending();
            string error = null;
            string result;
            try
            {
                result = compress ? f.Minify(input, out error) : f.Beautify(input, indent, out error);
            }
            catch (OutOfMemoryException)
            {
                ShowError("内存不足：文本过大，格式化失败。建议缩小输入或改用 64 位版本。");
                OutputBox.Text = "";
                GcHelper.CollectSafely("formatter-oom-sync");
                return;
            }
            catch (Exception ex)
            {
                ShowError("错误：" + ex.RootMessage());
                return;
            }

            if (error != null)
            {
                ShowError("错误：" + error);
                return;
            }

            ShowError("");
            try
            {
                OutputBox.Text = result;
            }
            catch (OutOfMemoryException)
            {
                ShowError("内存不足：结果过大无法载入编辑器。建议缩小输入或改用 64 位版本。");
                OutputBox.Text = "";
                GcHelper.CollectSafely("formatter-oom-output-sync");
            }
        }

        private async void FormatLarge(ITextFormatter f, string input, bool compress, string indent)
        {
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "格式化中…");
            ShowError("");

            string result = null;
            string error = null;
            try
            {
                result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return compress ? f.Minify(input, out error) : f.Beautify(input, indent, out error);
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (OutOfMemoryException)
            {
                if (version == _gate.Version)
                {
                    ShowError("内存不足：文本过大，格式化失败。建议缩小输入或改用 64 位版本。");
                    OutputBox.Text = "";
                    LoadingOverlay.Hide(this);
                    GcHelper.CollectSafely("formatter-oom-async");
                }
                return;
            }
            catch (Exception ex)
            {
                if (version == _gate.Version)
                {
                    ShowError("错误：" + ex.RootMessage());
                    LoadingOverlay.Hide(this);
                }
                return;
            }

            if (version != _gate.Version) return; // 过期结果丢弃

            if (error != null)
            {
                ShowError("错误：" + error);
                LoadingOverlay.Hide(this);
                return;
            }

            ShowError("");
            try
            {
                OutputBox.Text = result;
            }
            catch (OutOfMemoryException)
            {
                ShowError("内存不足：结果过大无法载入编辑器。建议缩小输入或改用 64 位版本。");
                OutputBox.Text = "";
                GcHelper.CollectSafely("formatter-oom-output-async");
            }
            LoadingOverlay.Hide(this);
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
            // 超大文本下语法高亮的逐行渲染极重且可能 OOM，直接关闭高亮保流畅。
            // 阈值取格式化硬顶的 1/5：能格式化的范围内仍高亮，超出则只保纯文本。
            int skipAt = SizeLimits.MaxFormatChars / 5;
            if (InputBox.Text.Length > skipAt)
            {
                InputBox.SetHighlightingByName(null);
                OutputBox.SetHighlightingByName(null);
                return;
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
            CancelPending();
            string input = InputBox.Text ?? "";
            // 超限文本不落盘 history.json，避免数百 MB 写入与序列化卡顿
            string sizeErr;
            if (!SizeLimits.TryCheckText(input, out sizeErr)) input = "";
            HistoryManager.Save("formatter", new Dictionary<string, object>
            {
                { "input", input },
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
