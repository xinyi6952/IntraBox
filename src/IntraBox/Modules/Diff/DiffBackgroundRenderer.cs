using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IntraBox.Modules.Diff
{
    /// <summary>
    /// 行级差异高亮：给指定行号画整行背景色。
    /// 另可叠加「当前差异块」高亮（上一处/下一处导航定位用），让用户看清跳到了哪。
    /// </summary>
    public sealed class DiffBackgroundRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private HashSet<int> _diffLines = new HashSet<int>();
        private HashSet<int> _currentLines = new HashSet<int>();

        public KnownLayer Layer => KnownLayer.Background;

        public Brush Background { get; set; }
        /// <summary>当前差异块的覆盖色（半透明），画在普通差异色之上。</summary>
        public Brush CurrentBackground { get; set; }
        /// <summary>当前差异块左侧竖条颜色（实色，强定位感）。</summary>
        public Brush CurrentMarker { get; set; }

        public DiffBackgroundRenderer(TextEditor editor)
        {
            _editor = editor;
        }

        public void SetDiffLines(IEnumerable<int> lines)
        {
            _diffLines = new HashSet<int>(lines);
        }

        /// <summary>设置当前导航定位的差异块行号；传空集合表示清除当前高亮。</summary>
        public void SetCurrentLines(IEnumerable<int> lines)
        {
            _currentLines = lines == null ? new HashSet<int>() : new HashSet<int>(lines);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_diffLines.Count == 0 && _currentLines.Count == 0) return;
            var doc = _editor.Document;
            if (doc == null) return;
            textView.EnsureVisualLines();

            // 第一层：所有差异行画底色
            if (_diffLines.Count > 0 && Background != null)
            {
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

            // 第二层：当前差异块叠加高亮 + 左侧竖条，定位导航落点
            if (_currentLines.Count == 0) return;
            Brush overlay = CurrentBackground;
            Brush marker = CurrentMarker;
            if (overlay == null && marker == null) return;
            foreach (var vl in textView.VisualLines)
            {
                int lineNum = vl.FirstDocumentLine.LineNumber;
                if (!_currentLines.Contains(lineNum)) continue;
                var line = doc.GetLineByNumber(lineNum);
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                {
                    double top = rect.Top;
                    double h = rect.Height;
                    if (overlay != null)
                        drawingContext.DrawRectangle(overlay, null, new Rect(0, top, textView.ActualWidth, h));
                    if (marker != null)
                        drawingContext.DrawRectangle(marker, null, new Rect(0, top, 3, h));
                }
            }
        }
    }
}
