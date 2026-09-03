using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using IntraBox.Core;

namespace IntraBox.Controls
{
    /// <summary>
    /// 统一代码编辑控件（AvalonEdit）：行号、等宽字体、当前行、缩进、括号匹配、折叠、主题、minimap。
    /// </summary>
    public partial class CodeEditor : UserControl
    {
        private bool _canMaximize;
        private bool _isMaximized;
        private MinimapMargin _minimap;
        // minimap 本期隐藏：实现尚不完善。置 true 可重新启用（MinimapMargin 代码已保留）。
        private const bool MinimapEnabled = false;

        public CodeEditor()
        {
            InitializeComponent();
            EditorChrome.Attach(Editor);
            Editor.Document.TextChanged += (s, e) => TextChangedByUser?.Invoke(this, EventArgs.Empty);
            SearchBar.Attach(Editor);
            PreviewKeyDown += OnPreviewKeyDown;
            // 粘贴前拦截：超大文本不进入 Rope，避免 UI 线程同步插入卡死与内存暴涨
            DataObject.AddPastingHandler(Editor, OnPasteCheck);
            // 拦截 ApplicationCommands.Paste：AvalonEdit 默认实现直接 Clipboard.GetDataObject()，
            // 剪贴板被占用时抛 CLIPBRD_E_CANT_OPEN 冒泡成全局未处理异常。这里自行取数据（带重试）。
            CommandManager.AddPreviewExecutedHandler(Editor.TextArea, OnPreviewExecutedCommand);
            // CodeGlance 风格 minimap：本期实现尚不完善，先隐藏（保留代码，以后启用把 MinimapEnabled 置 true）。
#pragma warning disable 162 // MinimapEnabled 为常量 false 时 if 分支不可达，属预期
            if (MinimapEnabled)
            {
                _minimap = new MinimapMargin(Editor);
                MinimapHost.Content = _minimap;
            }
            else
            {
                MinimapHost.Visibility = Visibility.Collapsed;
            }
#pragma warning restore 162
            Unloaded += CodeEditor_Unloaded;
        }

        private void CodeEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            CommandManager.RemovePreviewExecutedHandler(Editor.TextArea, OnPreviewExecutedCommand);
            if (_minimap != null)
            {
                _minimap.Detach();
                MinimapHost.Content = null;
                _minimap = null;
            }
            Unloaded -= CodeEditor_Unloaded;
        }

        private void OnPreviewExecutedCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste) return;
            // 拦截所有 Paste 命令：文本自行取数据（带重试）后插入；
            // 剪贴板无文本 / 占用失败 / 只读时均静默吞掉，避免落到 AvalonEdit 默认
            // Clipboard.GetDataObject()（占用时会抛 CLIPBRD_E_CANT_OPEN 冒成全局异常）。
            e.Handled = DoPasteWithRetry();
        }

        /// <summary>
        /// 自行处理粘贴：从剪贴板取文本（带重试退避），做大小校验后插入。
        /// 剪贴板被占用时静默失败，绝不抛未处理异常。返回 true 表示已处理。
        /// </summary>
        private bool DoPasteWithRetry()
        {
            // 只读也吞掉命令：否则落到 AvalonEdit 默认实现，其 GetDataObject() 在占用时仍会抛全局异常。
            if (Editor.IsReadOnly) return true;
            string text;
            // 无文本或占用失败均静默吞掉（本编辑器只接受文本，无需交默认处理）。
            if (!ClipboardHelper.TryGetText(out text)) return true;
            if (string.IsNullOrEmpty(text)) return true;
            if (_limitPasteLength)
            {
                string err;
                if (!SizeLimits.TryCheckText(text, out err))
                {
                    MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return true;
                }
            }
            var area = Editor.TextArea;
            try
            {
                if (area.Selection.Length > 0)
                    area.Selection.ReplaceSelectionWithText(text);
                else
                    Editor.Document.Insert(Editor.CaretOffset, text);
                area.Caret.BringCaretToView();
            }
            catch
            {
                // 插入失败（极少见）也不冒泡
            }
            return true;
        }

        /// <summary>Ctrl+F 呼出内置搜索，作用于当前编辑器。</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                SearchBar.Open();
            }
        }

        // 粘贴超限拦截（默认开）。拖拽落入的文本 AvalonEdit 不走此事件，由视图层另行防护。
        private bool _limitPasteLength = true;

        /// <summary>粘贴前校验文本长度，超过 SizeLimits.MaxTextChars 则取消并提示。默认 true。</summary>
        public bool LimitPasteLength
        {
            get { return _limitPasteLength; }
            set { _limitPasteLength = value; }
        }

        /// <summary>只读输出框右上角显示复制按钮（拷贝全部内容）。</summary>
        public bool ShowCopyButton
        {
            get { return CopyBtn != null && CopyBtn.Visibility == Visibility.Visible; }
            set { if (CopyBtn != null) CopyBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
        }

        private void OnPasteCheck(object sender, DataObjectPastingEventArgs e)
        {
            if (!_limitPasteLength || Editor.IsReadOnly) return;
            if (e.FormatToApply != DataFormats.UnicodeText && e.FormatToApply != DataFormats.Text) return;
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(text)) return;
            string err;
            if (!SizeLimits.TryCheckText(text, out err))
            {
                e.CancelCommand();
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public string Text
        {
            get { return Editor.Text; }
            set { Editor.Text = value ?? ""; }
        }

        public bool IsReadOnly
        {
            get { return Editor.IsReadOnly; }
            set { Editor.IsReadOnly = value; }
        }

        /// <summary>为 true 时右上角显示对角箭头，点击在模块内最大化（由父视图处理布局）。</summary>
        public bool CanMaximize
        {
            get { return _canMaximize; }
            set
            {
                _canMaximize = value;
                if (MaxBtn != null)
                    MaxBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>当前是否处于模块内最大化。父视图切换布局后同步此值以更换图标。</summary>
        public bool IsMaximized
        {
            get { return _isMaximized; }
            set
            {
                _isMaximized = value;
                if (IconExpand != null) IconExpand.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                if (IconRestore != null) IconRestore.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (MaxBtn != null) MaxBtn.ToolTip = value ? "还原布局" : "最大化输出";
            }
        }

        /// <summary>点击右上角箭头时通知父视图切换布局，不弹新窗口。</summary>
        public event EventHandler MaximizeToggle;

        /// <summary>内部 AvalonEdit 编辑器实例（供搜索等扩展 attach）。</summary>
        public TextEditor EditorControl
        {
            get { return Editor; }
        }

        public event EventHandler TextChangedByUser;

        public void SetHighlighting(IHighlightingDefinition def)
        {
            EditorChrome.SetHighlighting(Editor, def);
        }

        public void SetHighlightingByName(string name)
        {
            EditorChrome.SetHighlightingByName(Editor, name);
        }

        public IHighlightingDefinition GetHighlighting()
        {
            return Editor.SyntaxHighlighting;
        }

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MaximizeToggle != null) MaximizeToggle(this, EventArgs.Empty);
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!ClipboardHelper.TrySetText(Editor.Text, out err) && !string.IsNullOrEmpty(err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
