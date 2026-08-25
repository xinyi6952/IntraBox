using System;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;

namespace IntraBox.Controls
{
    /// <summary>
    /// 统一代码编辑控件（AvalonEdit）：行号、等宽字体、当前行、缩进、括号匹配、折叠、主题。
    /// </summary>
    public partial class CodeEditor : UserControl
    {
        private bool _canMaximize;
        private bool _isMaximized;

        public CodeEditor()
        {
            InitializeComponent();
            EditorChrome.Attach(Editor);
            Editor.Document.TextChanged += (s, e) => TextChangedByUser?.Invoke(this, EventArgs.Empty);
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
    }
}
