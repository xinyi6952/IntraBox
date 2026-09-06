using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using IntraBox.Core;

namespace IntraBox.Controls
{
    /// <summary>
    /// AvalonEdit 统一观感：行号、等宽、缩进、当前行、括号匹配、代码折叠、主题色。
    /// </summary>
    public static class EditorChrome
    {
        private const int FoldMaxChars = 400000;

        public static void Attach(TextEditor editor)
        {
            if (editor == null) return;
            editor.ShowLineNumbers = true;
            editor.FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New");
            editor.FontSize = 13;
            editor.WordWrap = false;
            editor.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            editor.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            editor.Padding = new Thickness(4);
            editor.Options.HighlightCurrentLine = true;
            editor.Options.EnableHyperlinks = false;
            editor.Options.EnableEmailHyperlinks = false;
            editor.Options.ConvertTabsToSpaces = true;
            editor.Options.IndentationSize = 4;
            editor.Options.ShowBoxForControlCharacters = false;
            editor.Options.AllowScrollBelowDocument = true;
            editor.Options.CutCopyWholeLine = true;
            editor.TextArea.SelectionCornerRadius = 0;

            var state = new EditorAttachState();
            state.Brackets = new BracketHighlightRenderer(editor);
            editor.TextArea.TextView.BackgroundRenderers.Add(state.Brackets);
            try
            {
                state.Folding = FoldingManager.Install(editor.TextArea);
            }
            catch (InvalidOperationException)
            {
                // 已安装过折叠管理器时忽略
            }
            state.FoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            state.FoldTimer.Tick += (s, e) =>
            {
                state.FoldTimer.Stop();
                UpdateFoldings(editor, state);
            };
            editor.Document.TextChanged += (s, e) =>
            {
                state.FoldTimer.Stop();
                state.FoldTimer.Start();
            };
            editor.Tag = state;

            ApplyTheme(editor);
            UpdateFoldings(editor, state);
            EventHandler onTheme = (s, e) => ApplyTheme(editor);
            ThemeManager.Changed += onTheme;
            editor.Unloaded += (s, e) =>
            {
                ThemeManager.Changed -= onTheme;
                state.FoldTimer.Stop();
            };
        }

        public static void ApplyTheme(TextEditor editor)
        {
            if (editor == null || Application.Current == null) return;
            editor.Background = FindBrush("EditorBackgroundBrush");
            editor.Foreground = FindBrush("EditorForegroundBrush");
            editor.LineNumbersForeground = FindBrush("EditorLineNumberBrush");
            editor.TextArea.Foreground = FindBrush("EditorForegroundBrush");
            editor.TextArea.TextView.CurrentLineBackground = FindBrush("EditorCurrentLineBrush");
            editor.TextArea.TextView.CurrentLineBorder = new Pen(FindBrush("EditorCurrentLineBorderBrush"), 1);
            editor.TextArea.SelectionBrush = FindBrush("EditorSelectionBrush");
            editor.TextArea.Caret.CaretBrush = FindBrush("EditorForegroundBrush");

            var state = editor.Tag as EditorAttachState;
            if (state != null && state.Brackets != null)
                state.Brackets.Background = FindBrush("BracketHighlightBrush");
            HighlightingTheme.Apply(editor.SyntaxHighlighting, ThemeManager.IsDark);
            editor.TextArea.TextView.Redraw();
        }

        public static void SetHighlighting(TextEditor editor, IHighlightingDefinition def)
        {
            if (editor == null) return;
            editor.SyntaxHighlighting = def;
            HighlightingTheme.Apply(def, ThemeManager.IsDark);
            var state = editor.Tag as EditorAttachState;
            UpdateFoldings(editor, state);
        }

        public static void SetHighlightingByName(TextEditor editor, string name)
        {
            IHighlightingDefinition def = null;
            if (!string.IsNullOrEmpty(name))
            {
                try { def = HighlightingManager.Instance.GetDefinition(name); }
                catch { def = null; }
            }
            SetHighlighting(editor, def);
        }

        public static void RefreshFoldings(TextEditor editor)
        {
            UpdateFoldings(editor, editor != null ? editor.Tag as EditorAttachState : null);
        }

        private static void UpdateFoldings(TextEditor editor, EditorAttachState state)
        {
            if (editor == null || state == null || state.Folding == null || editor.Document == null) return;
            if (editor.Document.TextLength > FoldMaxChars)
            {
                state.Folding.UpdateFoldings(new NewFolding[0], -1);
                return;
            }
            new BraceFoldingStrategy().UpdateFoldings(state.Folding, editor.Document);
        }

        private static Brush FindBrush(string key)
        {
            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }

        private sealed class EditorAttachState
        {
            public BracketHighlightRenderer Brackets;
            public FoldingManager Folding;
            public DispatcherTimer FoldTimer;
        }
    }
}
