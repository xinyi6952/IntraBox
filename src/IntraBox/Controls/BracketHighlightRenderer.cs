using System;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace IntraBox.Controls
{
    /// <summary>
    /// 光标处括号匹配高亮（() [] {}）。
    /// </summary>
    public sealed class BracketHighlightRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private int _a = -1;
        private int _b = -1;

        public KnownLayer Layer => KnownLayer.Selection;

        public Brush Background { get; set; }

        public BracketHighlightRenderer(TextEditor editor)
        {
            _editor = editor;
            _editor.TextArea.Caret.PositionChanged += (s, e) => Update();
            _editor.Document.TextChanged += (s, e) => Update();
        }

        public void Update()
        {
            _a = -1;
            _b = -1;
            var doc = _editor.Document;
            int offset = _editor.CaretOffset;
            if (doc == null || doc.TextLength == 0)
            {
                _editor.TextArea.TextView.InvalidateLayer(Layer);
                return;
            }

            int match;
            if (offset > 0 && TryMatch(doc, offset - 1, out match))
            {
                _a = offset - 1;
                _b = match;
            }
            else if (offset < doc.TextLength && TryMatch(doc, offset, out match))
            {
                _a = offset;
                _b = match;
            }
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_a < 0 || Background == null) return;
            textView.EnsureVisualLines();
            DrawOffset(textView, drawingContext, _a);
            DrawOffset(textView, drawingContext, _b);
        }

        private void DrawOffset(TextView textView, DrawingContext drawingContext, int offset)
        {
            var doc = _editor.Document;
            if (offset < 0 || offset >= doc.TextLength) return;
            var seg = new TextSegment { StartOffset = offset, Length = 1 };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
            {
            drawingContext.DrawRectangle(Background, null,
                new Rect(rect.X - 1, rect.Y, Math.Max(2, rect.Width + 2), rect.Height));
            }
        }

        private static bool TryMatch(TextDocument doc, int offset, out int match)
        {
            match = -1;
            char c = doc.GetCharAt(offset);
            char open, close;
            int dir;
            if (c == '(' || c == '[' || c == '{')
            {
                open = c;
                close = c == '(' ? ')' : c == '[' ? ']' : '}';
                dir = 1;
            }
            else if (c == ')' || c == ']' || c == '}')
            {
                close = c;
                open = c == ')' ? '(' : c == ']' ? '[' : '{';
                dir = -1;
            }
            else return false;

            int depth = 0;
            int i = offset;
            int last = dir > 0 ? Math.Min(doc.TextLength - 1, offset + 8000) : Math.Max(0, offset - 8000);
            while (true)
            {
                char ch = doc.GetCharAt(i);
                if (ch == open) depth++;
                else if (ch == close) depth--;
                if (depth == 0)
                {
                    match = i;
                    return true;
                }
                if (i == last) return false;
                i += dir;
            }
        }
    }
}
