using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IntraBox.Modules.Diff
{
    /// <summary>
    /// 行级差异高亮：给指定行号画整行背景色。
    /// </summary>
    public sealed class DiffBackgroundRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private HashSet<int> _diffLines = new HashSet<int>();

        public KnownLayer Layer => KnownLayer.Background;

        public Brush Background { get; set; }

        public DiffBackgroundRenderer(TextEditor editor)
        {
            _editor = editor;
        }

        public void SetDiffLines(IEnumerable<int> lines)
        {
            _diffLines = new HashSet<int>(lines);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_diffLines.Count == 0 || Background == null) return;
            var doc = _editor.Document;
            textView.EnsureVisualLines();
            foreach (var vl in textView.VisualLines)
            {
                int lineNum = vl.FirstDocumentLine.LineNumber;
                if (!_diffLines.Contains(lineNum)) continue;
                var line = doc.GetLineByNumber(lineNum);
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                {
                    var full = new Rect(0, rect.Top, textView.ActualWidth, rect.Height);
                    drawingContext.DrawRectangle(Background, null, full);
                }
            }
        }
    }
}
