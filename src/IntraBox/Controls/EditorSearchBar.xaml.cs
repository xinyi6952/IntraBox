using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IntraBox.Core;

namespace IntraBox.Controls
{
    /// <summary>
    /// 顶部内嵌搜索条（Notepad++ 风格查找）：向上/向下、循环、匹配计数、大小写/全字/正则、高亮所有匹配。
    /// 全文扫描放后台线程并支持取消，大文本不卡 UI。
    /// </summary>
    public partial class EditorSearchBar : UserControl
    {
        // 一个匹配的位置（后台计算产出，升序）
        private sealed class SearchMatch
        {
            public readonly int Start;
            public readonly int Length;
            public SearchMatch(int start, int length) { Start = start; Length = length; }
        }

        // 匹配数上限：短词/常见词（如 "a"、"."）在超大文本里会产生海量匹配，
        // 超过上限则截断，只高亮/跳转前 MaxMatches 个，避免内存暴涨与 UI 卡顿。
        private const int MaxMatches = 10000;

        private TextEditor _editor;
        private TextEditor _left;
        private TextEditor _right;
        private TextSegmentCollection<TextSegment> _results;
        private HighlightRenderer _renderer;
        private List<SearchMatch> _matches = new List<SearchMatch>();
        private int _current = -1; // 当前选中匹配序号，-1 表示无
        private bool _truncated;

        private CancellationTokenSource _cts;
        private int _version;
        // 输入框改动后置脏：只有点击「查找」或按 Enter 才真正搜索，避免输入中途的非法正则弹错误。
        private bool _dirty;

        public EditorSearchBar()
        {
            InitializeComponent();
        }

        /// <summary>绑定单个要搜索的编辑器（无左右切换）。</summary>
        public void Attach(TextEditor editor)
        {
            _left = editor;
            _right = null;
            if (SideCombo != null) SideCombo.Visibility = Visibility.Collapsed;
            SetActiveEditor(editor);
        }

        /// <summary>绑定左右两个编辑器（如文本对比）：显示「左侧/右侧」切换，默认搜左侧。</summary>
        public void Attach(TextEditor left, TextEditor right)
        {
            _left = left;
            _right = right;
            if (SideCombo != null) SideCombo.Visibility = Visibility.Visible;
            SetActiveEditor(_left);
        }

        private void SetActiveEditor(TextEditor editor)
        {
            if (_editor == editor) return;
            if (_editor != null && _editor.Document != null)
                _editor.Document.TextChanged -= OnDocumentChanged;
            // 切走时把高亮 renderer 从旧 TextView 摘下，否则切到另一侧后橙色背景仍画在旧侧
            if (_renderer != null && _editor != null && _editor.TextArea != null)
            {
                _editor.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
                _renderer = null;
                _results = null;
            }
            _editor = editor;
            if (_editor != null && _editor.Document != null)
                _editor.Document.TextChanged += OnDocumentChanged;
            ClearMatches();
            if (Visibility == Visibility.Visible) Recompute();
        }

        private void Side_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_left == null || _right == null || SideCombo == null) return;
            SetActiveEditor(SideCombo.SelectedIndex == 1 ? _right : _left);
        }

        public void Open()
        {
            Visibility = Visibility.Visible;
            SearchBox.Focus();
            SearchBox.SelectAll();
            // 不自动搜索：等用户点「查找」或按 Enter。已有文本标记为脏，首次操作即搜索。
            _dirty = !string.IsNullOrEmpty(SearchBox.Text);
        }

        public void Close()
        {
            CancelPending();
            Visibility = Visibility.Collapsed;
            ClearMatches();
        }

        // ---------- 事件 ----------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 输入时不自动搜索，只清掉上一次的残留错误与高亮，避免输入中途弹「错误」。
            CancelPending();
            _dirty = true;
            ClearMatches();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SearchBox.Text)) return;
            // 选项切换是显式动作，模式已完整，直接重搜。
            CancelPending();
            _dirty = false;
            Recompute();
        }

        private void FindBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelPending();
            _dirty = false;
            Recompute();
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty) { _dirty = false; Recompute(); return; }
            FindPrevious();
        }
        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty) { _dirty = false; Recompute(); return; }
            FindNext();
        }
        private void CloseBtn_Click(object sender, RoutedEventArgs e) { Close(); }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_dirty)
                {
                    _dirty = false;
                    Recompute();
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift) FindPrevious();
                else FindNext();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnDocumentChanged(object sender, EventArgs e)
        {
            // 文档内容变了，旧匹配失效：清掉高亮并标记脏，等用户再次点「查找」。
            ClearMatches();
            _dirty = !string.IsNullOrEmpty(SearchBox.Text);
        }

        // ---------- 搜索 ----------

        private async void Recompute()
        {
            if (_editor == null) return;
            string pattern = SearchBox.Text;

            if (string.IsNullOrEmpty(pattern))
            {
                ClearMatches();
                return;
            }

            bool caseSensitive = CaseChk.IsChecked == true;
            bool wholeWord = WordChk.IsChecked == true;
            bool useRegex = RegexChk.IsChecked == true;
            var editor = _editor;
            // 必须在 UI 线程取文本快照：AvalonEdit 的 TextEditor.Text 有线程亲和性，
            // 在后台线程访问会抛 InvalidOperationException，被吞成「错误」。
            string text = editor.Text;

            // 先取消并释放上一次的 CTS，避免连点查找泄漏旧 CTS、旧任务继续扫大文本
            CancelPending();
            int version = ++_version;
            var cts = new CancellationTokenSource();
            _cts = cts;
            var token = cts.Token;
            SetCount("…", false);

            List<SearchMatch> found = null;
            string error = null;
            bool truncated = false;
            try
            {
                // 全文扫描在后台线程，输入文本已是快照，无跨线程访问
                found = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return new List<SearchMatch>();
                    return FindAll(text, pattern, caseSensitive, wholeWord, useRegex, out error, out truncated, token);
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { error = ex.RootMessage(); }

            if (version != _version || _editor == null) return; // 过期结果丢弃

            _matches = found ?? new List<SearchMatch>();
            _truncated = truncated;
            _current = -1;
            UpdateHighlight();

            if (error != null)
            {
                SetCount("错误", true);
                CountText.ToolTip = error;
                return;
            }

            CountText.ToolTip = null;

            if (_matches.Count == 0)
            {
                SetCount("无结果", false);
                return;
            }

            // 定位到 caret 之后第一个匹配并选中（循环）
            int caret = editor.CaretOffset;
            int idx = LowerBound(caret);
            if (idx >= _matches.Count) idx = 0;
            SelectMatch(idx);
        }

        private void FindNext()
        {
            if (_matches.Count == 0) return;
            int idx = (_current >= 0 && _current < _matches.Count - 1) ? _current + 1 : 0;
            SelectMatch(idx);
        }

        private void FindPrevious()
        {
            if (_matches.Count == 0) return;
            int idx = (_current > 0) ? _current - 1 : _matches.Count - 1;
            SelectMatch(idx);
        }

        /// <summary>第一个 Start &gt;= offset 的匹配下标（二分）。</summary>
        private int LowerBound(int offset)
        {
            int lo = 0, hi = _matches.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_matches[mid].Start < offset) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private void SelectMatch(int index)
        {
            if (index < 0 || index >= _matches.Count || _editor == null) return;
            var m = _matches[index];
            _current = index;
            _editor.Select(m.Start, m.Length);
            _editor.TextArea.Caret.BringCaretToView();
            string suffix = _truncated ? "+" : "";
            SetCount((index + 1) + "/" + _matches.Count + suffix, false);
        }

        // ---------- 高亮 ----------

        private void UpdateHighlight()
        {
            if (_editor == null || _editor.TextArea == null) return;
            var tv = _editor.TextArea.TextView;
            if (_renderer == null)
            {
                _renderer = new HighlightRenderer { MarkerBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xA0, 0x00)) };
                tv.BackgroundRenderers.Add(_renderer);
            }
            if (_results == null)
            {
                _results = _renderer.Segments;
            }
            _results.Clear();
            foreach (var m in _matches)
                _results.Add(new TextSegment { StartOffset = m.Start, Length = m.Length });
            tv.Redraw();
        }

        private void ClearMatches()
        {
            _matches.Clear();
            _current = -1;
            if (_results != null) _results.Clear();
            SetCount("", false);
            CountText.ToolTip = null;
        }

        private void SetCount(string text, bool isError)
        {
            CountText.Text = text;
            CountText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "TextSecondaryBrush");
        }

        private void CancelPending()
        {
            _version++;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        // ---------- 纯计算（后台线程） ----------

        private static List<SearchMatch> FindAll(string text, string pattern, bool caseSensitive, bool wholeWord, bool useRegex, out string error, out bool truncated, CancellationToken token)
        {
            error = null;
            truncated = false;
            var result = new List<SearchMatch>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return result;

            try
            {
                if (useRegex)
                {
                    var opts = RegexOptions.None;
                    if (!caseSensitive) opts |= RegexOptions.IgnoreCase;
                    var regex = new Regex(pattern, opts, TimeSpan.FromSeconds(5));
                    foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
                    {
                        if (token.IsCancellationRequested) return result;
                        if (m.Length > 0)
                        {
                            if (result.Count >= MaxMatches) { truncated = true; return result; }
                            result.Add(new SearchMatch(m.Index, m.Length));
                        }
                    }
                }
                else
                {
                    var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    int idx = text.IndexOf(pattern, 0, comparison);
                    while (idx >= 0)
                    {
                        if (token.IsCancellationRequested) return result;
                        if (!wholeWord || IsWordBoundary(text, idx, pattern.Length))
                        {
                            if (result.Count >= MaxMatches) { truncated = true; return result; }
                            result.Add(new SearchMatch(idx, pattern.Length));
                        }
                        idx = text.IndexOf(pattern, idx + 1, comparison);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                error = "正则过于复杂，已超时（5 秒）";
                return result;
            }
            catch (ArgumentException ex)
            {
                error = "正则表达式无效：" + ex.RootMessage();
                return result;
            }
            return result;
        }

        private static bool IsWordBoundary(string text, int start, int length)
        {
            bool before = start == 0 || !IsWordChar(text[start - 1]);
            bool after = start + length >= text.Length || !IsWordChar(text[start + length]);
            return before && after;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        /// <summary>高亮所有匹配：画在 Selection 层，按可见区域裁剪。</summary>
        private sealed class HighlightRenderer : IBackgroundRenderer
        {
            private readonly TextSegmentCollection<TextSegment> _segments = new TextSegmentCollection<TextSegment>();

            public Brush MarkerBrush { get; set; }

            public TextSegmentCollection<TextSegment> Segments { get { return _segments; } }

            public KnownLayer Layer { get { return KnownLayer.Selection; } }

            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                if (textView == null || drawingContext == null || _segments.Count == 0 || MarkerBrush == null) return;

                // 只遍历可见区域内（文档 offset 范围）的匹配段，避免超大文本 + 海量匹配时每次重绘都全量遍历
                var visualLines = textView.VisualLines;
                if (visualLines == null || visualLines.Count == 0) return;
                int topOffset = visualLines[0].FirstDocumentLine.Offset;
                int bottomOffset = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

                var builder = new BackgroundGeometryBuilder { CornerRadius = 1 };
                TextSegment seg = _segments.FindFirstSegmentWithStartAfter(topOffset - 1);
                while (seg != null && seg.StartOffset <= bottomOffset)
                {
                    builder.AddSegment(textView, seg);
                    seg = _segments.GetNextSegment(seg);
                }

                var geometry = builder.CreateGeometry();
                if (geometry != null)
                    drawingContext.DrawGeometry(MarkerBrush, null, geometry);
            }
        }
    }
}
