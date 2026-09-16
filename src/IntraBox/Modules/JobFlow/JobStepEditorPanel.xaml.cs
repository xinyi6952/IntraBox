using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobStepEditorPanel : UserControl
    {
        private readonly ObservableCollection<JobHeader> _headers = new ObservableCollection<JobHeader>();
        private readonly ObservableCollection<ExtractRow> _extracts = new ObservableCollection<ExtractRow>();
        private JobStep _step;
        private bool _readOnly;
        private bool _suppress;
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        public JobStepEditorPanel()
        {
            InitializeComponent();
            HeaderGrid.ItemsSource = _headers;
            ExtractGrid.ItemsSource = _extracts;
            if (BodyCue != null) BodyCue.Text = "{\"user\":\"${user}\",\"pwd\":\"${pwd}\"}";
            MethodCombo.SelectedIndex = 1;
            SoapVerCombo.SelectedIndex = 0;
            LoopKindCombo.SelectedIndex = 0;
            WireAutoGrow(BodyBox);
            WireAutoGrow(EnvelopeBox);
            WireAutoGrow(NsBox);
            WireAutoGrow(ScriptBodyBox);
            if (TimeoutBox != null)
                TimeoutBox.ToolTip = JobLimits.TimeoutHint();
        }

        public JobStep Current { get { return _step; } }

        public void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        public void Load(JobStep step, bool readOnly)
        {
            _step = step ?? JobStep.Create(JobStepKind.Rest);
            _readOnly = readOnly;
            _suppress = true;
            JobHeaderHelper.Normalize(_step);
            StepNameBox.Text = _step.Name ?? "";
            KindLabel.Text = "类型：" + JobStep.KindText(_step.Kind);
            StepEnabledCheck.IsChecked = _step.Enabled;
            TimeoutBox.Text = _step.TimeoutSec < 1 ? "" : _step.TimeoutSec.ToString();
            StepContinueCheck.IsChecked = _step.ContinueOnFail;
            StepInsecureCheck.IsChecked = _step.Insecure;
            SelectCombo(MethodCombo, _step.Method);
            UrlBox.Text = _step.Kind == JobStepKind.Soap || _step.Kind == JobStepKind.OpenUrl ? "" : (_step.Url ?? "");
            SoapUrlBox.Text = _step.Kind == JobStepKind.Soap ? (_step.Url ?? "") : "";
            OpenUrlBox.Text = _step.Kind == JobStepKind.OpenUrl ? (_step.Url ?? "") : "";
            BodyBox.Text = _step.Body ?? "";
            EnvelopeBox.Text = _step.Envelope ?? "";
            NsBox.Text = _step.XmlNamespaces ?? "";
            SelectCombo(SoapVerCombo, string.IsNullOrEmpty(_step.SoapVersion) ? "1.1" : _step.SoapVersion);
            SoapActionBox.Text = _step.SoapAction ?? "";
            PathBox.Text = _step.Path ?? "";
            ArgsBox.Text = _step.Arguments ?? "";
            WorkDirBox.Text = _step.WorkDir ?? "";
            AllowedExitBox.Text = string.IsNullOrEmpty(_step.AllowedExitCodes) ? "0" : _step.AllowedExitCodes;
            ScriptBodyBox.Text = _step.ScriptBody ?? "";
            PythonExeBox.Text = _step.PythonExe ?? "";
            SelectCombo(LoopKindCombo, JobLoopHelper.KindText((JobLoopHelper.LoopKind)_step.LoopKind));
            LoopCountBox.Text = (_step.LoopCount < 1 ? 1 : _step.LoopCount).ToString();
            LoopSourceBox.Text = _step.LoopSource ?? "";
            LoopItemBox.Text = string.IsNullOrEmpty(_step.LoopItemName) ? "item" : _step.LoopItemName;
            LoopPropBox.Text = _step.LoopItemProperty ?? "";
            _headers.Clear();
            if (_step.Headers != null)
            {
                for (int i = 0; i < _step.Headers.Count; i++)
                {
                    var h = _step.Headers[i] ?? JobHeader.Make("", "", true);
                    _headers.Add(JobHeader.Make(h.Name, h.Value, h.Enabled));
                }
            }
            _extracts.Clear();
            if (_step.Extracts != null)
            {
                for (int i = 0; i < _step.Extracts.Count; i++)
                    _extracts.Add(ExtractRow.From(_step.Extracts[i]));
            }
            ApplyKindUi(_step.Kind);
            IsEnabled = !readOnly;
            _suppress = false;
            RefreshCues();
            AdjustAll();
        }

        public bool TryCollect(out string error)
        {
            error = null;
            if (_step == null) { error = "步骤为空"; return false; }
            ApplyFields();
            error = JobHeaderHelper.ValidateStep(_step);
            return error == null;
        }

        public JobStep CollectCopy()
        {
            if (_step == null) return null;
            ApplyFields();
            return JobFlowStore.CloneStep(_step);
        }

        private void ApplyFields()
        {
            if (_step == null) return;
            _step.Name = StepNameBox.Text ?? "";
            _step.Enabled = StepEnabledCheck.IsChecked == true;
            string timeoutText = (TimeoutBox.Text ?? "").Trim();
            int to;
            if (timeoutText.Length == 0 || !int.TryParse(timeoutText, out to) || to < 1)
                _step.TimeoutSec = 0;
            else
                _step.TimeoutSec = JobLimits.ClampTimeoutSec(to);
            _step.ContinueOnFail = StepContinueCheck.IsChecked == true;
            _step.Insecure = StepInsecureCheck.IsChecked == true;
            _step.Method = ComboText(MethodCombo);
            if (_step.Kind == JobStepKind.Soap) _step.Url = SoapUrlBox.Text ?? "";
            else if (_step.Kind == JobStepKind.OpenUrl) _step.Url = OpenUrlBox.Text ?? "";
            else _step.Url = UrlBox.Text ?? "";
            _step.Body = BodyBox.Text ?? "";
            _step.Envelope = EnvelopeBox.Text ?? "";
            _step.XmlNamespaces = NsBox.Text ?? "";
            _step.SoapVersion = ComboText(SoapVerCombo);
            _step.SoapAction = SoapActionBox.Text ?? "";
            _step.Path = PathBox.Text ?? "";
            _step.Arguments = ArgsBox.Text ?? "";
            _step.WorkDir = WorkDirBox.Text ?? "";
            _step.AllowedExitCodes = AllowedExitBox.Text ?? "0";
            _step.ScriptBody = ScriptBodyBox.Text ?? "";
            _step.PythonExe = PythonExeBox.Text ?? "";
            _step.LoopKind = (int)JobLoopHelper.ParseKind(ComboText(LoopKindCombo));
            int lc;
            if (!int.TryParse(LoopCountBox.Text, out lc) || lc < 1) lc = 1;
            _step.LoopCount = lc;
            _step.LoopSource = LoopSourceBox.Text ?? "";
            _step.LoopItemName = string.IsNullOrWhiteSpace(LoopItemBox.Text) ? "item" : LoopItemBox.Text.Trim();
            _step.LoopItemProperty = LoopPropBox.Text ?? "";
            _step.Headers = new System.Collections.Generic.List<JobHeader>();
            for (int i = 0; i < _headers.Count; i++)
                _step.Headers.Add(_headers[i]);
            JobHeaderHelper.SyncText(_step);
            _step.Extracts = new System.Collections.Generic.List<JobExtract>();
            for (int i = 0; i < _extracts.Count; i++)
                _step.Extracts.Add(_extracts[i].ToModel());
        }

        private void ApplyKindUi(JobStepKind kind)
        {
            bool rest = kind == JobStepKind.Rest;
            bool soap = kind == JobStepKind.Soap;
            bool open = kind == JobStepKind.OpenUrl;
            bool proc = kind == JobStepKind.Program || kind == JobStepKind.Bat;
            bool py = kind == JobStepKind.Python;
            RestPanel.Visibility = rest ? Visibility.Visible : Visibility.Collapsed;
            SoapPanel.Visibility = soap ? Visibility.Visible : Visibility.Collapsed;
            OpenPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            PathPanel.Visibility = (proc || py) ? Visibility.Visible : Visibility.Collapsed;
            ScriptPanel.Visibility = py ? Visibility.Visible : Visibility.Collapsed;
            PythonExePanel.Visibility = py ? Visibility.Visible : Visibility.Collapsed;
            ArgsPanel.Visibility = (proc || py) ? Visibility.Visible : Visibility.Collapsed;
            WorkDirPanel.Visibility = (proc || py) ? Visibility.Visible : Visibility.Collapsed;
            ExitCodePanel.Visibility = (proc || py) ? Visibility.Visible : Visibility.Collapsed;
            PathStar.Visibility = py ? Visibility.Collapsed : Visibility.Visible;
            PathLabel.Text = py ? ".py路径" : "路径";
            PathCue.Text = py ? "C:\\scripts\\job.py" : (kind == JobStepKind.Bat ? "C:\\tools\\prepare.bat" : "C:\\tools\\app.exe");
        }

        private void Cue_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_suppress) RefreshCues();
            var box = sender as TextBox;
            if (box != null && box.AcceptsReturn) AdjustAutoGrow(box);
        }

        private void RefreshCues()
        {
            SetCue(StepNameBox, StepNameCue);
            SetCue(TimeoutBox, TimeoutCue);
            SetCue(UrlBox, UrlCue);
            SetCue(BodyBox, BodyCue);
            SetCue(SoapUrlBox, SoapUrlCue);
            SetCue(OpenUrlBox, OpenUrlCue);
            SetCue(PathBox, PathCue);
        }

        private static void SetCue(TextBox box, TextBlock cue)
        {
            if (box == null || cue == null) return;
            cue.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddHeader_Click(object sender, RoutedEventArgs e)
        {
            _headers.Add(JobHeader.Make("", "", true));
        }

        private void DelHeader_Click(object sender, RoutedEventArgs e)
        {
            var h = HeaderGrid.SelectedItem as JobHeader;
            if (h != null) _headers.Remove(h);
        }

        private void AddExtract_Click(object sender, RoutedEventArgs e)
        {
            _extracts.Add(new ExtractRow { Name = "var" + (_extracts.Count + 1), Path = "$.", AllowEmpty = false });
        }

        private void DelExtract_Click(object sender, RoutedEventArgs e)
        {
            var row = ExtractGrid.SelectedItem as ExtractRow;
            if (row != null) _extracts.Remove(row);
        }

        private void PasteCurl_Click(object sender, RoutedEventArgs e)
        {
            var win = new JobCurlPasteWindow();
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() != true || win.ResultStep == null) return;
            string uid = _step != null ? _step.Uid : null;
            string lib = _step != null ? _step.LibraryUid : null;
            _step = win.ResultStep;
            if (!string.IsNullOrEmpty(uid)) _step.Uid = uid;
            _step.LibraryUid = lib;
            Load(_step, _readOnly);
            FormHint.Apply(PanelMsg, "已从 curl 识别填入（未调用 curl.exe）。");
        }

        private async void ImportWsdl_Click(object sender, RoutedEventArgs e)
        {
            ApplyFields();
            string src = PromptText("导入 WSDL", "WSDL 网址或本地 .wsdl/.xml 路径：", _step != null ? (_step.Url ?? "") : "");
            if (src == null) return;
            if (string.IsNullOrWhiteSpace(src))
            {
                FormHint.Apply(PanelMsg, "未填写 WSDL 地址。");
                return;
            }
            _gate.Cancel();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            LoadingOverlay.Show(this, "导入 WSDL…");
            JobWsdlHelper.WsdlImportResult result = null;
            try
            {
                result = await Task.Run(() => JobWsdlHelper.Import(src.Trim(), 30000), cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (version == _gate.Version) FormHint.Apply(PanelMsg, "导入失败：" + ex.RootMessage());
                return;
            }
            finally
            {
                if (version == _gate.Version)
                {
                    LoadingOverlay.Hide(this);
                    _gate.DisposeCurrent();
                }
            }
            if (version != _gate.Version) return;
            if (result == null || !result.Ok)
            {
                FormHint.Apply(PanelMsg, result != null ? (result.Error ?? "导入失败") : "导入失败");
                return;
            }
            var names = new System.Collections.Generic.List<string>();
            if (result.Operations != null)
            {
                for (int i = 0; i < result.Operations.Count; i++)
                    names.Add(result.Operations[i].Name);
            }
            if (names.Count == 0)
            {
                FormHint.Apply(PanelMsg, "WSDL 中没有可用操作。");
                return;
            }
            string picked = names[0];
            if (names.Count > 1)
            {
                picked = PromptText("选择 SOAP 操作", "操作名（" + string.Join(" / ", names.ToArray()) + "）：", names[0]);
                if (picked == null) return;
            }
            JobWsdlHelper.WsdlOperation op = null;
            for (int i = 0; i < result.Operations.Count; i++)
            {
                if (string.Equals(result.Operations[i].Name, picked.Trim(), StringComparison.Ordinal))
                {
                    op = result.Operations[i];
                    break;
                }
            }
            if (op == null) op = result.Operations[0];
            _step.Url = op.Endpoint ?? "";
            _step.SoapAction = op.SoapAction ?? "";
            _step.SoapVersion = string.IsNullOrEmpty(op.SoapVersion) ? "1.1" : op.SoapVersion;
            _step.Envelope = op.Envelope ?? "";
            _step.XmlNamespaces = op.XmlNamespaces ?? "";
            Load(_step, _readOnly);
            FormHint.Apply(PanelMsg, "已填入操作 " + op.Name);
        }

        private async void ProbeTcp_Click(object sender, RoutedEventArgs e) { await ProbeAsync(false); }
        private async void ProbeHttp_Click(object sender, RoutedEventArgs e) { await ProbeAsync(true); }

        private async Task ProbeAsync(bool sendHttp)
        {
            string err;
            if (!TryCollect(out err))
            {
                SetProbeMsg(err, false);
                return;
            }
            if (_step.Kind != JobStepKind.Rest && _step.Kind != JobStepKind.Soap)
            {
                SetProbeMsg("测通仅用于 REST / SOAP。", false);
                return;
            }
            var vars = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            JobVarHelper.SeedSystemVars(vars, DateTime.Now);
            _gate.Cancel();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            LoadingOverlay.Show(this, sendHttp ? "测通中（TCP+HTTP）…" : "测通中（TCP）…");
            JobStepResult result = null;
            try
            {
                var step = _step;
                result = await Task.Run(() => JobRunner.ProbeStep(step, vars, sendHttp, cts.Token), cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (version == _gate.Version) SetProbeMsg("测通失败：" + ex.RootMessage(), false);
                return;
            }
            finally
            {
                if (version == _gate.Version)
                {
                    LoadingOverlay.Hide(this);
                    _gate.DisposeCurrent();
                }
            }
            if (version != _gate.Version || result == null) return;
            SetProbeMsg(result.Message, result.Ok);
        }

        private void SetProbeMsg(string text, bool ok)
        {
            if (PanelMsg == null) return;
            FormHint.Apply(PanelMsg, text ?? "", !ok);
        }

        private static string PromptText(string title, string label, string value)
        {
            var win = new Window
            {
                Title = title,
                Width = 480,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Application.Current.TryFindResource("WindowBgBrush") as System.Windows.Media.Brush
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "确定", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 88, IsCancel = true };
            var box = new TextBox { Text = value ?? "" };
            ok.Click += (s, e) => { win.DialogResult = true; };
            cancel.Click += (s, e) => { win.DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            var lab = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(lab, Dock.Top);
            root.Children.Add(lab);
            root.Children.Add(box);
            win.Content = root;
            win.Owner = Application.Current.MainWindow;
            if (win.ShowDialog() != true) return null;
            return box.Text;
        }

        private void WireAutoGrow(TextBox box)
        {
            if (box == null) return;
            box.TextChanged += (s, e) => AdjustAutoGrow(box);
            box.SizeChanged += (s, e) => { if (e.WidthChanged) AdjustAutoGrow(box); };
        }

        private void AdjustAll()
        {
            AdjustAutoGrow(BodyBox);
            AdjustAutoGrow(EnvelopeBox);
            AdjustAutoGrow(NsBox);
            AdjustAutoGrow(ScriptBodyBox);
        }

        private static void AdjustAutoGrow(TextBox box)
        {
            if (box == null) return;
            try
            {
                double minH = !double.IsNaN(box.MinHeight) && box.MinHeight > 0 ? box.MinHeight : 56;
                double maxH = !double.IsInfinity(box.MaxHeight) && box.MaxHeight > 0 ? box.MaxHeight : 360;
                box.UpdateLayout();
                double width = box.ActualWidth < 40 ? 480 : box.ActualWidth;
                var ft = new System.Windows.Media.FormattedText(
                    string.IsNullOrEmpty(box.Text) ? " ag" : box.Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                    box.FontSize,
                    System.Windows.Media.Brushes.Black,
                    WinCompatHelper.PixelsPerDip(box));
                ft.MaxTextWidth = Math.Max(40, width - box.Padding.Left - box.Padding.Right - 16);
                double contentH = string.IsNullOrEmpty(box.Text) ? (box.FontSize * 1.35) : ft.Height;
                double h = contentH + box.Padding.Top + box.Padding.Bottom + 12;
                if (h < minH) h = minH;
                if (h > maxH) h = maxH;
                box.Height = h;
            }
            catch { }
        }

        private static void SelectCombo(ComboBox box, string text)
        {
            if (box == null) return;
            for (int i = 0; i < box.Items.Count; i++)
            {
                var item = box.Items[i] as ComboBoxItem;
                string t = item != null ? (item.Content as string) : null;
                if (string.Equals(t, text, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            if (box.SelectedIndex < 0) box.SelectedIndex = 0;
        }

        private static string ComboText(ComboBox box)
        {
            if (box == null || box.SelectedItem == null) return "";
            var item = box.SelectedItem as ComboBoxItem;
            return item != null ? (item.Content as string ?? "") : "";
        }

        internal sealed class ExtractRow
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public bool AllowEmpty { get; set; }

            public static ExtractRow From(JobExtract e)
            {
                if (e == null) return new ExtractRow { Name = "var", Path = "$.", AllowEmpty = false };
                return new ExtractRow { Name = e.Name, Path = e.Path, AllowEmpty = e.AllowEmpty };
            }

            public JobExtract ToModel()
            {
                return new JobExtract { Name = Name, Path = Path, AllowEmpty = AllowEmpty };
            }
        }
    }
}
