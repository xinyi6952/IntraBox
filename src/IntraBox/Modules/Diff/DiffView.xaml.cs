using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DiffPlex;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Diff
{
    /// <summary>
    /// 文本比对：左右 AvalonEdit、自动比对、同类型约束、完整路径、编码、合并后写回。
    /// </summary>
    public partial class DiffView : UserControl, IModuleView, ILeaveGuard
    {
        private long MaxFileBytes { get { return SizeLimits.MaxFileBytes; } }

        private readonly DiffBackgroundRenderer _leftRenderer;
        private readonly DiffBackgroundRenderer _rightRenderer;
        private List<DiffPlex.Model.DiffBlock> _blocks = new List<DiffPlex.Model.DiffBlock>();
        private int _currentDiff = -1;
        private string _leftFilePath;
        private string _rightFilePath;
        private EncodingChoice _leftChoice = TextFileCodec.Auto;
        private EncodingChoice _rightChoice = TextFileCodec.Auto;
        private readonly DispatcherTimer _debounce = new DispatcherTimer();
        private bool _syncingScroll;
        private bool _loading;
        private bool _leftDirty;
        private bool _rightDirty;
        private string _leftEncWarn;
        private string _rightEncWarn;
        private bool _leftMax;
        private bool _rightMax;
        private ScrollViewer _leftSv;
        private ScrollViewer _rightSv;

        // 比对/加载后台化：取消 + 版本号丢弃过期结果，大文本不卡 UI
        private CancellationTokenSource _cmpCts;
        private int _cmpVersion;
        private int _loadVersion;
        // 超过此字符数禁用语法高亮，保证超大文件加载/滚动/比对流畅
        private const int HighlightMaxChars = 300 * 1000;

        public DiffView()
        {
            InitializeComponent();

            EditorChrome.Attach(LeftEditor);
            EditorChrome.Attach(RightEditor);

            _leftRenderer = new DiffBackgroundRenderer(LeftEditor);
            _rightRenderer = new DiffBackgroundRenderer(RightEditor);
            LeftEditor.TextArea.TextView.BackgroundRenderers.Add(_leftRenderer);
            RightEditor.TextArea.TextView.BackgroundRenderers.Add(_rightRenderer);
            ApplyDiffColors();
            ThemeManager.Changed += OnThemeChanged;
            Unloaded += (s, e) => ThemeManager.Changed -= OnThemeChanged;

            LeftEncCombo.ItemsSource = TextFileCodec.All;
            RightEncCombo.ItemsSource = TextFileCodec.All;
            LeftEncCombo.SelectedItem = TextFileCodec.Auto;
            RightEncCombo.SelectedItem = TextFileCodec.Auto;

            _debounce.Interval = TimeSpan.FromMilliseconds(280);
            _debounce.Tick += (s, e) => { _debounce.Stop(); Compare(); };

            LeftEditor.TextChanged += (s, e) =>
            {
                if (!_loading) _leftDirty = true;
                OnTextChanged();
            };
            RightEditor.TextChanged += (s, e) =>
            {
                if (!_loading) _rightDirty = true;
                OnTextChanged();
            };
            LeftEditor.SizeChanged += (s, e) => UpdateArrows();
            RightEditor.SizeChanged += (s, e) => UpdateArrows();
            SearchBar.Attach(LeftEditor, RightEditor);
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (s, e) => { HookScroll(); UpdateArrows(); };
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+F 呼出搜索；最大化状态下先还原，再显示搜索条
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (_leftMax) SetPaneMaximized(true, false);
                if (_rightMax) SetPaneMaximized(false, false);
                SearchBar.Open();
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyDiffColors();
            Compare();
        }

        private void ApplyDiffColors()
        {
            _leftRenderer.Background = FindBrush("DiffDeleteBrush");
            _rightRenderer.Background = FindBrush("DiffInsertBrush");
            // 当前差异块高亮：半透明强调色覆盖 + 实色左侧竖条，导航落点一眼可见
            var accent = FindBrush("AccentBrush") ?? Brushes.Orange;
            _leftRenderer.CurrentMarker = accent;
            _rightRenderer.CurrentMarker = accent;
            _leftRenderer.CurrentBackground = MakeOverlay(accent, 0.35);
            _rightRenderer.CurrentBackground = MakeOverlay(accent, 0.35);
        }

        private static Brush MakeOverlay(Brush baseBrush, double opacity)
        {
            if (baseBrush is SolidColorBrush sc)
            {
                var c = sc.Color;
                return new SolidColorBrush(Color.FromArgb(
                    (byte)(opacity * 255), c.R, c.G, c.B));
            }
            return baseBrush;
        }

        private static Brush FindBrush(string key)
        {
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }

        private void OnTextChanged()
        {
            if (_loading) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Compare();
        }

        private async void Compare()
        {
            bool leftFile = _leftFilePath != null;
            bool rightFile = _rightFilePath != null;
            if (leftFile != rightFile)
            {
                CancelCompare();
                SetStat("左右类型不一致：只允许「文本 ↔ 文本」或「文件 ↔ 文件」，请清空一侧或两侧都打开文件。", true);
                ClearCurrentHighlight();
                ClearHighlights();
                _blocks.Clear();
                ArrowCanvas.Children.Clear();
                _currentDiff = -1;
                return;
            }

            string leftText = LeftEditor.Text ?? "";
            string rightText = RightEditor.Text ?? "";
            bool ignoreWs = IgnoreWhitespace.IsChecked == true;
            bool ignoreCase = IgnoreCase.IsChecked == true;

            CancelCompare();
            int version = ++_cmpVersion;
            var cts = new CancellationTokenSource();
            _cmpCts = cts;
            var token = cts.Token;

            DiffResult result = null;
            try
            {
                // DiffPlex 行级 diff 是纯计算，放后台线程，避免大文本卡 UI
                result = await Task.Run(() => RunDiff(leftText, rightText, ignoreWs, ignoreCase, token), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (version == _cmpVersion) SetStat("比对失败：" + ex.RootMessage(), true);
                return;
            }

            if (version != _cmpVersion) return; // 过期结果丢弃

            _blocks = result.Blocks;
            _leftRenderer.SetDiffLines(result.LeftLines);
            _rightRenderer.SetDiffLines(result.RightLines);
            _currentDiff = -1;
            ClearCurrentHighlight();
            LeftEditor.TextArea.TextView.Redraw();
            RightEditor.TextArea.TextView.Redraw();

            SetStat(
                _blocks.Count == 0
                    ? "无差异"
                    : "新增 " + result.Added + " 行　删除 " + result.Deleted + " 行　修改 " + result.Modified + " 处　共 " + _blocks.Count + " 个差异块",
                false);

            _currentDiff = -1;
            UpdateArrows();
        }

        /// <summary>后台线程执行的纯计算：行级 diff + 统计。</summary>
        private static DiffResult RunDiff(string left, string right, bool ignoreWs, bool ignoreCase, CancellationToken token)
        {
            var result = new DiffResult
            {
                Blocks = new List<DiffPlex.Model.DiffBlock>(),
                LeftLines = new HashSet<int>(),
                RightLines = new HashSet<int>()
            };
            var differ = new Differ();
            result.Blocks = differ.CreateLineDiffs(left, right, ignoreWs, ignoreCase).DiffBlocks.ToList();

            foreach (var b in result.Blocks)
            {
                if (token.IsCancellationRequested) return result;
                for (int i = b.DeleteStartA; i < b.DeleteStartA + b.DeleteCountA; i++) result.LeftLines.Add(i + 1);
                for (int j = b.InsertStartB; j < b.InsertStartB + b.InsertCountB; j++) result.RightLines.Add(j + 1);
                if (b.DeleteCountA > 0 && b.InsertCountB > 0) result.Modified++;
                else
                {
                    result.Added += b.InsertCountB;
                    result.Deleted += b.DeleteCountA;
                }
            }
            return result;
        }

        private void CancelCompare()
        {
            _cmpVersion++;
            if (_cmpCts != null)
            {
                _cmpCts.Cancel();
                _cmpCts.Dispose();
                _cmpCts = null;
            }
        }

        private sealed class DiffResult
        {
            public List<DiffPlex.Model.DiffBlock> Blocks;
            public HashSet<int> LeftLines;
            public HashSet<int> RightLines;
            public int Added;
            public int Deleted;
            public int Modified;
        }

        private void ClearHighlights()
        {
            _leftRenderer.SetDiffLines(new int[0]);
            _rightRenderer.SetDiffLines(new int[0]);
            LeftEditor.TextArea.TextView.Redraw();
            RightEditor.TextArea.TextView.Redraw();
        }

        private void LeftFile_Click(object sender, RoutedEventArgs e) => PickFile(true);
        private void RightFile_Click(object sender, RoutedEventArgs e) => PickFile(false);

        private void PickFile(bool isLeft)
        {
            var dlg = new OpenFileDialog
            {
                Title = isLeft ? "选择左侧文件（原文）" : "选择右侧文件（新文）",
                Filter = "文本文件|*.txt;*.log;*.json;*.xml;*.yml;*.yaml;*.md;*.cs;*.js;*.html;*.css;*.sql;*.conf;*.ini;*.java;*.py|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            LoadFile(dlg.FileName, isLeft, false);
        }

        private async void LoadFile(string path, bool isLeft, bool keepEncoding)
        {
            var choice = keepEncoding
                ? (isLeft ? CurrentLeftChoice() : CurrentRightChoice())
                : TextFileCodec.Auto;

            int version = ++_loadVersion;

            string text = null;
            EncodingChoice used = TextFileCodec.Auto;
            bool uncertain = false;
            try
            {
                // 读盘 + 解码放后台线程，避免大文件卡 UI
                text = await Task.Run(() => TextFileCodec.ReadAll(path, choice, MaxFileBytes, out used, out uncertain));
            }
            catch (Exception ex)
            {
                if (version == _loadVersion) SetStat("读取失败：" + ex.RootMessage() + "。可切换编码后重试。", true);
                return;
            }

            if (version != _loadVersion) return; // 过期加载丢弃

            string warn = uncertain ? "未识别编码，已按 GBK 读取，可能乱码" : null;

            _loading = true;
            if (isLeft)
            {
                SetEditorText(LeftEditor, text, true);
                _leftFilePath = path;
                _leftChoice = used;
                _leftEncWarn = warn;
                LeftEncCombo.SelectedItem = used;
                ApplySyntaxHighlighting(LeftEditor, path, text.Length);
            }
            else
            {
                SetEditorText(RightEditor, text, true);
                _rightFilePath = path;
                _rightChoice = used;
                _rightEncWarn = warn;
                RightEncCombo.SelectedItem = used;
                ApplySyntaxHighlighting(RightEditor, path, text.Length);
            }
            _loading = false;
            if (isLeft) _leftDirty = false;
            else _rightDirty = false;
            UpdatePathLabels();
            Compare();
            AppendEncodingWarn();
        }

        private EncodingChoice CurrentLeftChoice()
        {
            return LeftEncCombo.SelectedItem as EncodingChoice ?? TextFileCodec.Auto;
        }

        private EncodingChoice CurrentRightChoice()
        {
            return RightEncCombo.SelectedItem as EncodingChoice ?? TextFileCodec.Auto;
        }

        private void LeftEnc_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _leftChoice = CurrentLeftChoice();
            if (_leftFilePath != null) LoadFile(_leftFilePath, true, true);
            else Compare();
        }

        private void RightEnc_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _rightChoice = CurrentRightChoice();
            if (_rightFilePath != null) LoadFile(_rightFilePath, false, true);
            else Compare();
        }

        private void Left_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Right_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Left_Drop(object sender, DragEventArgs e)
        {
            var path = FirstDroppedFile(e);
            if (path != null) LoadFile(path, true, false);
            e.Handled = true;
        }

        private void Right_Drop(object sender, DragEventArgs e)
        {
            var path = FirstDroppedFile(e);
            if (path != null) LoadFile(path, false, false);
            e.Handled = true;
        }

        private static string FirstDroppedFile(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            return files != null && files.Length > 0 ? files[0] : null;
        }

        private static void ApplySyntaxHighlighting(TextEditor editor, string path, int textLength)
        {
            if (editor == null) return;
            if (textLength > HighlightMaxChars)
            {
                editor.SyntaxHighlighting = null; // 超大文本禁用高亮，保证流畅
                return;
            }
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".md" || ext == ".markdown" || ext == ".txt" || ext == ".log") return;
                var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
                if (def != null) editor.SyntaxHighlighting = def;
            }
            catch { }
        }

        private void UpdatePathLabels()
        {
            if (_leftFilePath != null)
            {
                LeftPathText.Text = _leftFilePath;
                LeftSaveBtn.Visibility = Visibility.Visible;
            }
            else
            {
                LeftPathText.Text = "文本输入（未关联文件）";
                LeftSaveBtn.Visibility = Visibility.Collapsed;
            }

            if (_rightFilePath != null)
            {
                RightPathText.Text = _rightFilePath;
                RightSaveBtn.Visibility = Visibility.Visible;
            }
            else
            {
                RightPathText.Text = "文本输入（未关联文件）";
                RightSaveBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void LeftSave_Click(object sender, RoutedEventArgs e) => SaveSide(true, true);
        private void RightSave_Click(object sender, RoutedEventArgs e) => SaveSide(false, true);

        private void LeftMax_Click(object sender, RoutedEventArgs e)
        {
            SetPaneMaximized(true, !_leftMax);
        }

        private void RightMax_Click(object sender, RoutedEventArgs e)
        {
            SetPaneMaximized(false, !_rightMax);
        }

        private void SetPaneMaximized(bool isLeft, bool on)
        {
            if (on)
            {
                if (isLeft && _rightMax) SetPaneMaximized(false, false);
                if (!isLeft && _leftMax) SetPaneMaximized(true, false);
            }

            if (isLeft) _leftMax = on;
            else _rightMax = on;

            if (on) SearchBar.Close(); // 进入最大化时关闭搜索条，避免遮挡
            DiffToolbar.Visibility = (_leftMax || _rightMax) ? Visibility.Collapsed : Visibility.Visible;
            ArrowCanvas.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (isLeft)
            {
                RightPane.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                Grid.SetColumnSpan(LeftPane, on ? 3 : 1);
                LeftMaxExpand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                LeftMaxRestore.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                LeftMaxBtn.ToolTip = on ? "还原布局" : "最大化原文";
            }
            else
            {
                LeftPane.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                Grid.SetColumn(RightPane, on ? 0 : 2);
                Grid.SetColumnSpan(RightPane, on ? 3 : 1);
                RightMaxExpand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                RightMaxRestore.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                RightMaxBtn.ToolTip = on ? "还原布局" : "最大化新文";
            }
            if (!on)
            {
                ArrowCanvas.Visibility = Visibility.Visible;
                if (!_leftMax && !_rightMax) DiffToolbar.Visibility = Visibility.Visible;
            }
        }

        private void SaveSide(bool isLeft, bool confirm)
        {
            var path = isLeft ? _leftFilePath : _rightFilePath;
            if (string.IsNullOrEmpty(path))
            {
                SetStat("当前侧不是文件比对，无法写回原文件。", true);
                return;
            }

            var choice = isLeft ? CurrentLeftChoice() : CurrentRightChoice();
            if (choice.IsAuto) choice = isLeft ? _leftChoice : _rightChoice;
            var text = isLeft ? LeftEditor.Text : RightEditor.Text;

            if (confirm)
            {
                var result = MessageBox.Show(
                    "确定用当前内容覆盖磁盘上的原文件？\n编辑器内仍可用 Ctrl+Z 撤销修改（再次保存才会写回磁盘）。\n\n路径：\n" + path + "\n编码：" + choice.Display,
                    "保存到文件",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.OK) return;
            }

            try
            {
                TextFileCodec.WriteAll(path, text, choice);
                if (isLeft) _leftDirty = false;
                else _rightDirty = false;
                SetStat("已保存：" + path + "（" + choice.Display + "）", false);
            }
            catch (UnauthorizedAccessException)
            {
                SetStat("保存失败：没有写入权限。", true);
            }
            catch (IOException ex)
            {
                SetStat("保存失败：文件被占用或无法写入。" + ex.RootMessage(), true);
            }
            catch (Exception ex)
            {
                SetStat("保存失败：" + ex.RootMessage(), true);
            }
        }

        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            var tmpText = LeftEditor.Text;
            LeftEditor.Text = RightEditor.Text;
            RightEditor.Text = tmpText;

            var tmpHighlight = LeftEditor.SyntaxHighlighting;
            LeftEditor.SyntaxHighlighting = RightEditor.SyntaxHighlighting;
            RightEditor.SyntaxHighlighting = tmpHighlight;

            var tmpPath = _leftFilePath;
            _leftFilePath = _rightFilePath;
            _rightFilePath = tmpPath;

            var tmpChoice = _leftChoice;
            _leftChoice = _rightChoice;
            _rightChoice = tmpChoice;

            var tmpSel = LeftEncCombo.SelectedItem;
            LeftEncCombo.SelectedItem = RightEncCombo.SelectedItem;
            RightEncCombo.SelectedItem = tmpSel;
            var tmpDirty = _leftDirty;
            _leftDirty = _rightDirty;
            _rightDirty = tmpDirty;
            var tmpWarn = _leftEncWarn;
            _leftEncWarn = _rightEncWarn;
            _rightEncWarn = tmpWarn;
            _loading = false;

            UpdatePathLabels();
            Compare();
            AppendEncodingWarn();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmClearIfDirty()) return;
            DoClear();
        }

        /// <summary>
        /// 有未保存修改或非空文本时询问。文件模式：保存 / 不保存 / 取消。文本模式：确认清空。
        /// </summary>
        private bool ConfirmClearIfDirty()
        {
            bool leftFileDirty = _leftDirty && !string.IsNullOrEmpty(_leftFilePath);
            bool rightFileDirty = _rightDirty && !string.IsNullOrEmpty(_rightFilePath);
            if (leftFileDirty || rightFileDirty)
            {
                var msg = "有未保存的修改，清空将丢失改动。是否保存？";
                if (leftFileDirty) msg += "\n\n左侧：" + _leftFilePath;
                if (rightFileDirty) msg += "\n\n右侧：" + _rightFilePath;

                var result = ConfirmHelper.Unsaved(msg, "清空前保存");
                if (result == MessageBoxResult.Cancel) return false;
                if (result == MessageBoxResult.Yes)
                {
                    if (leftFileDirty) SaveSide(true, false);
                    if (rightFileDirty) SaveSide(false, false);
                    if ((_leftDirty && !string.IsNullOrEmpty(_leftFilePath))
                        || (_rightDirty && !string.IsNullOrEmpty(_rightFilePath)))
                        return false;
                }
                return true;
            }

            bool hasText = !string.IsNullOrEmpty(LeftEditor.Text) || !string.IsNullOrEmpty(RightEditor.Text);
            if (!hasText) return true;
            return ConfirmHelper.Action("确定清空左右两侧内容？未保存的文本会丢失。", "清空确认");
        }

        private void DoClear()
        {
            _loading = true;
            LeftEditor.Text = "";
            RightEditor.Text = "";
            _leftFilePath = null;
            _rightFilePath = null;
            _leftChoice = TextFileCodec.Auto;
            _rightChoice = TextFileCodec.Auto;
            _leftEncWarn = null;
            _rightEncWarn = null;
            LeftEncCombo.SelectedItem = TextFileCodec.Auto;
            RightEncCombo.SelectedItem = TextFileCodec.Auto;
            LeftEditor.SyntaxHighlighting = null;
            RightEditor.SyntaxHighlighting = null;
            _loading = false;
            _leftDirty = false;
            _rightDirty = false;
            _blocks.Clear();
            _currentDiff = -1;
            ClearCurrentHighlight();
            ClearHighlights();
            ArrowCanvas.Children.Clear();
            UpdatePathLabels();
            StatText.Text = "";
        }

        private void PrevDiff_Click(object sender, RoutedEventArgs e)
        {
            if (_blocks.Count == 0) return;
            if (_currentDiff <= 0)
            {
                SetStat("已经是第一处差异", false);
                return;
            }
            GotoDiff(_currentDiff - 1);
        }

        private void NextDiff_Click(object sender, RoutedEventArgs e)
        {
            if (_blocks.Count == 0) return;
            if (_currentDiff >= _blocks.Count - 1 && _currentDiff >= 0)
            {
                SetStat("已经是最后一处差异", false);
                return;
            }
            GotoDiff(_currentDiff + 1);
        }

        private void GotoDiff(int index)
        {
            if (_blocks.Count == 0) return;
            _currentDiff = index;
            var b = _blocks[index];
            if (b.DeleteCountA > 0)
            {
                LeftEditor.ScrollToLine(b.DeleteStartA + 1);
                RightEditor.ScrollToLine(Math.Max(1, b.InsertStartB + 1));
            }
            else
            {
                RightEditor.ScrollToLine(b.InsertStartB + 1);
                LeftEditor.ScrollToLine(Math.Max(1, b.DeleteStartA + 1));
            }
            HighlightCurrentBlock(b);
            SetStat("差异 " + (index + 1) + " / " + _blocks.Count, false);
        }

        /// <summary>把当前差异块覆盖的左右行号推给渲染器并重绘，导航落点高亮可见。</summary>
        private void HighlightCurrentBlock(DiffPlex.Model.DiffBlock b)
        {
            if (b == null) { ClearCurrentHighlight(); return; }
            var left = new List<int>();
            for (int i = b.DeleteStartA; i < b.DeleteStartA + b.DeleteCountA; i++)
                if (i >= 0) left.Add(i + 1);
            var right = new List<int>();
            for (int j = b.InsertStartB; j < b.InsertStartB + b.InsertCountB; j++)
                if (j >= 0) right.Add(j + 1);
            _leftRenderer.SetCurrentLines(left);
            _rightRenderer.SetCurrentLines(right);
            LeftEditor.TextArea.TextView.Redraw();
            RightEditor.TextArea.TextView.Redraw();
        }

        private void ClearCurrentHighlight()
        {
            _leftRenderer.SetCurrentLines(null);
            _rightRenderer.SetCurrentLines(null);
            LeftEditor.TextArea.TextView.Redraw();
            RightEditor.TextArea.TextView.Redraw();
        }

        private void UpdateArrows()
        {
            ArrowCanvas.Children.Clear();
            for (int i = 0; i < _blocks.Count; i++)
            {
                var b = _blocks[i];
                int lineNum;
                TextEditor editor;
                if (b.DeleteCountA > 0) { lineNum = b.DeleteStartA + 1; editor = LeftEditor; }
                else { lineNum = b.InsertStartB + 1; editor = RightEditor; }

                double y = GetVisualY(editor, lineNum);
                if (y < 0 || y > editor.ActualHeight) continue;

                var btnRight = MakeArrowButton("→", i, true);
                Canvas.SetLeft(btnRight, 0);
                Canvas.SetTop(btnRight, y);
                ArrowCanvas.Children.Add(btnRight);

                var btnLeft = MakeArrowButton("←", i, false);
                Canvas.SetLeft(btnLeft, 14);
                Canvas.SetTop(btnLeft, y);
                ArrowCanvas.Children.Add(btnLeft);
            }
        }

        private Button MakeArrowButton(string text, int blockIndex, bool leftToRight)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 14,
                Width = 14,
                MinHeight = 16,
                Height = 16,
                Padding = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = FindBrush("PrimaryBrush"),
                Foreground = FindBrush("PrimaryButtonFgBrush"),
                BorderBrush = FindBrush("PrimaryBrush"),
                ToolTip = leftToRight ? "将左侧合并到右侧" : "将右侧合并到左侧",
                Tag = new Tuple<int, bool>(blockIndex, leftToRight)
            };
            btn.Click += Arrow_Click;
            return btn;
        }

        private void Arrow_Click(object sender, RoutedEventArgs e)
        {
            var tag = (Tuple<int, bool>)((Button)sender).Tag;
            MergeTo(tag.Item1, tag.Item2);
        }

        private void MergeTo(int blockIndex, bool leftToRight)
        {
            if (blockIndex < 0 || blockIndex >= _blocks.Count) return;
            var b = _blocks[blockIndex];
            var leftLines = SplitLines(LeftEditor.Text);
            var rightLines = SplitLines(RightEditor.Text);

            if (leftToRight)
            {
                int start, count;
                if (!ClampRange(b.InsertStartB, b.InsertCountB, rightLines.Length, out start, out count)) return;
                int srcStart = b.DeleteStartA < 0 ? 0 : b.DeleteStartA;
                int srcCount = b.DeleteCountA < 0 ? 0 : b.DeleteCountA;
                var list = new List<string>(rightLines);
                list.RemoveRange(start, count);
                list.InsertRange(start, leftLines.Skip(srcStart).Take(Math.Min(srcCount, Math.Max(0, leftLines.Length - srcStart))));
                SetEditorText(RightEditor, string.Join("\n", list), false);
            }
            else
            {
                int start, count;
                if (!ClampRange(b.DeleteStartA, b.DeleteCountA, leftLines.Length, out start, out count)) return;
                int srcStart = b.InsertStartB < 0 ? 0 : b.InsertStartB;
                int srcCount = b.InsertCountB < 0 ? 0 : b.InsertCountB;
                var list = new List<string>(leftLines);
                list.RemoveRange(start, count);
                list.InsertRange(start, rightLines.Skip(srcStart).Take(Math.Min(srcCount, Math.Max(0, rightLines.Length - srcStart))));
                SetEditorText(LeftEditor, string.Join("\n", list), false);
            }
        }

        /// <summary>
        /// 写入编辑器全文。resetUndo=true 用于打开文件（新基线）；false 则进入 UndoStack，保存到磁盘也不清空。
        /// </summary>
        private static void SetEditorText(TextEditor editor, string text, bool resetUndo)
        {
            if (editor == null || editor.Document == null) return;
            var doc = editor.Document;
            if (text == null) text = "";
            doc.Replace(0, doc.TextLength, text);
            if (resetUndo) doc.UndoStack.ClearAll();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            var focus = Keyboard.FocusedElement as TextEditor;
            if (focus == RightEditor || (focus != LeftEditor && RightEditor.CanUndo))
            {
                if (RightEditor.CanUndo) RightEditor.Undo();
                else if (LeftEditor.CanUndo) LeftEditor.Undo();
            }
            else if (LeftEditor.CanUndo) LeftEditor.Undo();
            else if (RightEditor.CanUndo) RightEditor.Undo();
        }

        private static bool ClampRange(int start, int count, int len, out int s, out int c)
        {
            s = start;
            c = count;
            if (s < 0) s = 0;
            if (s > len) return false;
            if (c < 0) c = 0;
            if (s + c > len) c = len - s;
            return true;
        }

        public bool CanLeave()
        {
            if (!ConfirmSaveIfDirty(true)) return false;
            if (!ConfirmSaveIfDirty(false)) return false;

            bool leftoverText = false;
            if (string.IsNullOrEmpty(_leftFilePath) && !string.IsNullOrEmpty(LeftEditor.Text)) leftoverText = true;
            if (string.IsNullOrEmpty(_rightFilePath) && !string.IsNullOrEmpty(RightEditor.Text)) leftoverText = true;
            if (leftoverText)
                return ConfirmHelper.Action("有未保存到文件的文本，离开后将丢失。确定离开？", "未保存确认");
            return true;
        }

        /// <summary>文件比对且该侧有未保存修改时询问。Cancel 则拒绝离开。</summary>
        private bool ConfirmSaveIfDirty(bool isLeft)
        {
            bool dirty = isLeft ? _leftDirty : _rightDirty;
            var path = isLeft ? _leftFilePath : _rightFilePath;
            if (!dirty || string.IsNullOrEmpty(path)) return true;

            var result = ConfirmHelper.Unsaved(
                "文件有未保存的修改，是否保存？\n\n" + path,
                "保存到文件");
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes) SaveSide(isLeft, false);
            else
            {
                if (isLeft) _leftDirty = false;
                else _rightDirty = false;
            }
            return true;
        }

        private void HookScroll()
        {
            UnhookScroll();
            _leftSv = FindScrollViewer(LeftEditor);
            _rightSv = FindScrollViewer(RightEditor);
            if (_leftSv != null) _leftSv.ScrollChanged += LeftSv_ScrollChanged;
            if (_rightSv != null) _rightSv.ScrollChanged += RightSv_ScrollChanged;
        }

        private void UnhookScroll()
        {
            if (_leftSv != null) _leftSv.ScrollChanged -= LeftSv_ScrollChanged;
            if (_rightSv != null) _rightSv.ScrollChanged -= RightSv_ScrollChanged;
            _leftSv = null;
            _rightSv = null;
        }

        private void LeftSv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            if (_rightSv != null) _rightSv.ScrollToVerticalOffset(e.VerticalOffset);
            _syncingScroll = false;
            UpdateArrows();
        }

        private void RightSv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            if (_leftSv != null) _leftSv.ScrollToVerticalOffset(e.VerticalOffset);
            _syncingScroll = false;
            UpdateArrows();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < n; i++)
            {
                var r = FindScrollViewer(VisualTreeHelper.GetChild(obj, i));
                if (r != null) return r;
            }
            return null;
        }

        private double GetVisualY(TextEditor editor, int lineNumber)
        {
            if (lineNumber < 1 || lineNumber > editor.Document.LineCount) return -1;
            var tv = editor.TextArea.TextView;
            return tv.GetVisualTopByDocumentLine(lineNumber) - tv.ScrollOffset.Y;
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private void SetStat(string text, bool isError)
        {
            StatText.Foreground = isError ? FindBrush("DangerBrush") : FindBrush("TextSecondaryBrush");
            StatText.Text = text;
        }

        private void AppendEncodingWarn()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_leftEncWarn))
                parts.Add((_rightFilePath != null ? "左侧：" : "") + _leftEncWarn);
            if (!string.IsNullOrEmpty(_rightEncWarn))
                parts.Add((_leftFilePath != null ? "右侧：" : "") + _rightEncWarn);
            if (parts.Count == 0) return;
            var warn = string.Join("　", parts);
            var cur = StatText.Text;
            if (string.IsNullOrEmpty(cur)) SetStat(warn, true);
            else SetStat(cur + "　" + warn, true);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("diff", out state)) return;

            // 只灌数据、不重新读盘；_loading 期间跳过编码切换/自动比对
            _loading = true;
            IgnoreWhitespace.IsChecked = HistoryManager.GetBool(state, "ignoreWs", false);
            IgnoreCase.IsChecked = HistoryManager.GetBool(state, "ignoreCase", false);
            _leftFilePath = NullIfEmpty(HistoryManager.GetString(state, "leftPath"));
            _rightFilePath = NullIfEmpty(HistoryManager.GetString(state, "rightPath"));
            LeftEditor.Text = HistoryManager.GetString(state, "leftText");
            RightEditor.Text = HistoryManager.GetString(state, "rightText");
            LeftEncCombo.SelectedItem = FindEncoding(LeftEncCombo, HistoryManager.GetString(state, "leftEnc"));
            RightEncCombo.SelectedItem = FindEncoding(RightEncCombo, HistoryManager.GetString(state, "rightEnc"));
            _leftChoice = CurrentLeftChoice();
            _rightChoice = CurrentRightChoice();
            _loading = false;
            if (_leftFilePath != null && string.IsNullOrEmpty(LeftEditor.Text))
                LoadFile(_leftFilePath, true, true);
            else if (_leftFilePath != null) ApplySyntaxHighlighting(LeftEditor, _leftFilePath, LeftEditor.Text.Length);
            if (_rightFilePath != null && string.IsNullOrEmpty(RightEditor.Text))
                LoadFile(_rightFilePath, false, true);
            else if (_rightFilePath != null) ApplySyntaxHighlighting(RightEditor, _rightFilePath, RightEditor.Text.Length);
            if (string.IsNullOrEmpty(HistoryManager.GetString(state, "leftText")) && _leftFilePath != null)
                _leftDirty = false;
            else
                _leftDirty = HistoryManager.GetBool(state, "leftDirty", false);
            if (string.IsNullOrEmpty(HistoryManager.GetString(state, "rightText")) && _rightFilePath != null)
                _rightDirty = false;
            else
                _rightDirty = HistoryManager.GetBool(state, "rightDirty", false);

            UpdatePathLabels();
            Compare();
        }

        public void OnDeactivated()
        {
            _debounce.Stop();
            CancelCompare();
            ThemeManager.Changed -= OnThemeChanged;
            UnhookScroll();
            HistoryManager.Save("diff", new Dictionary<string, object>
            {
                { "leftText", LeftEditor.Text ?? "" },
                { "rightText", RightEditor.Text ?? "" },
                { "ignoreWs", IgnoreWhitespace.IsChecked == true },
                { "ignoreCase", IgnoreCase.IsChecked == true },
                { "leftPath", _leftFilePath ?? "" },
                { "rightPath", _rightFilePath ?? "" },
                { "leftEnc", CurrentLeftChoice().Display ?? "" },
                { "rightEnc", CurrentRightChoice().Display ?? "" },
                { "leftDirty", _leftDirty },
                { "rightDirty", _rightDirty }
            });
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static EncodingChoice FindEncoding(ComboBox combo, string display)
        {
            if (combo == null || string.IsNullOrEmpty(display)) return TextFileCodec.Auto;
            foreach (var item in combo.Items)
            {
                var choice = item as EncodingChoice;
                if (choice != null && choice.Display == display) return choice;
            }
            return TextFileCodec.Auto;
        }
    }
}
