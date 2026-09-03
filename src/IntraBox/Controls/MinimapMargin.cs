using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IntraBox.Controls
{
    /// <summary>
    /// CodeGlance 风格 minimap：编辑器右侧窄列绘制整份文档鸟瞰缩略图，
    /// 半透明矩形标出主编辑器当前可视行范围，点击/拖动滚动定位。
    /// 基础版：只读缩略 + 点击/拖动滚动 + 可视区指示。无语法高亮、无独立滚动。
    /// 超过 MaxRenderChars 字符的文档关闭 minimap 内容，避免卡顿/OOM。
    /// 注：AvalonEdit TextArea 只有 LeftMargins，无右侧集合，故作为独立 FrameworkElement
    /// 由 CodeEditor 放在编辑器右侧列，自行挂接 TextView/Document 事件。
    /// 启用前需优化：EnsureBitmap 对每行 FormattedText 在近 MaxRenderChars 字符时是 O(n) 全量重建
    /// （每次编辑触发），且 MaxBitmapHeight 截断后大文档只渲染前约 5000 行；MeasureOverride 高度返回
    /// 0 的拉伸影响待验证。本期 MinimapEnabled=false 保持关闭。
    /// </summary>
    public sealed class MinimapMargin : FrameworkElement
    {
        private const double MarginWidth = 70.0;
        private const double MiniFontSize = 3.0;
        private const double MiniLineHeight = 3.2;
        private const int MaxRenderChars = 200_000;
        private const int MaxBitmapHeight = 16000;

        private readonly TextEditor _editor;
        private readonly FontFamily _fontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New");

        private bool _tooLarge;
        private bool _bitmapDirty = true;
        private BitmapSource _textBitmap;
        private double _lastWidth;
        private bool _dragging;

        private double PixelsPerDip
        {
            get
            {
                try
                {
                    var src = PresentationSource.FromVisual(this);
                    if (src != null && src.CompositionTarget != null)
                        return src.CompositionTarget.TransformToDevice.M11;
                }
                catch { }
                return 1.0;
            }
        }

        public MinimapMargin(TextEditor editor)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            _editor = editor;
            Focusable = false;
            IsHitTestVisible = true;
            Hook();
        }

        private TextView TextView { get { return _editor != null && _editor.TextArea != null ? _editor.TextArea.TextView : null; } }

        private void Hook()
        {
            var tv = TextView;
            if (tv != null)
            {
                tv.ScrollOffsetChanged += OnScrollChanged;
                tv.VisualLinesChanged += OnVisualLinesChanged;
            }
            if (_editor.Document != null)
                _editor.Document.TextChanged += OnDocChanged;
        }

        /// <summary>从父控件卸载：摘事件、释放位图。</summary>
        public void Detach()
        {
            var tv = TextView;
            if (tv != null)
            {
                tv.ScrollOffsetChanged -= OnScrollChanged;
                tv.VisualLinesChanged -= OnVisualLinesChanged;
            }
            if (_editor.Document != null)
                _editor.Document.TextChanged -= OnDocChanged;
            _textBitmap = null;
            _bitmapDirty = true;
        }

        private void OnDocChanged(object sender, EventArgs e)
        {
            _bitmapDirty = true;
            InvalidateVisual();
        }

        private void OnScrollChanged(object sender, EventArgs e)
        {
            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object sender, EventArgs e)
        {
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // 宽度固定，高度交给父容器拉伸
            return new Size(MarginWidth, 0);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var bg = FindBrush("EditorBackgroundBrush");
            drawingContext.DrawRectangle(bg, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

            if (_editor == null || _editor.Document == null) return;
            int len = _editor.Document.TextLength;
            if (len <= 0) return;

            if (len > MaxRenderChars)
            {
                _tooLarge = true;
                _textBitmap = null;
                DrawTooLargeMessage(drawingContext);
                return;
            }
            _tooLarge = false;

            EnsureBitmap();
            if (_textBitmap != null && _textBitmap.Width > 0 && _textBitmap.Height > 0)
            {
                drawingContext.DrawImage(_textBitmap, new Rect(0, 0, _textBitmap.Width, _textBitmap.Height));
            }
            DrawViewportIndicator(drawingContext);
        }

        private void DrawTooLargeMessage(DrawingContext dc)
        {
            var fg = FindBrush("TextSecondaryBrush");
            var typeface = new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(
                "文档过大\nminimap 已关闭",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                9,
                fg,
                PixelsPerDip)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = MarginWidth
            };
            dc.DrawText(ft, new Point(0, 8));
        }

        private void EnsureBitmap()
        {
            if (!_bitmapDirty && _textBitmap != null && Math.Abs(_lastWidth - RenderSize.Width) < 0.5) return;
            _bitmapDirty = false;
            _lastWidth = RenderSize.Width;
            _textBitmap = null;

            if (_editor == null || _editor.Document == null) return;
            string text = _editor.Document.Text;
            if (string.IsNullOrEmpty(text)) return;

            int lineCount = _editor.Document.LineCount;
            double height = lineCount * MiniLineHeight;
            if (height < 1) height = 1;

            int widthPx = Math.Max(1, (int)Math.Ceiling(RenderSize.Width));
            int heightPx = Math.Max(1, (int)Math.Ceiling(height));
            if (heightPx > MaxBitmapHeight) heightPx = MaxBitmapHeight;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var fg = FindBrush("EditorForegroundBrush");
                var fgWeak = new SolidColorBrush(BlendWithBackground(fg)) { Opacity = 0.85 };
                fgWeak.Freeze();
                var typeface = new Typeface(_fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double y = 0;
                int maxLine = Math.Min(lineCount, (int)(MaxBitmapHeight / MiniLineHeight));
                for (int i = 1; i <= maxLine; i++)
                {
                    var line = _editor.Document.GetLineByNumber(i);
                    string s = _editor.Document.GetText(line.Offset, line.Length);
                    if (string.IsNullOrEmpty(s)) { y += MiniLineHeight; continue; }
                    var ft = new FormattedText(
                        s,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        MiniFontSize,
                        fgWeak,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip)
                    {
                        MaxTextWidth = MarginWidth
                    };
                    dc.DrawText(ft, new Point(0, y));
                    y += MiniLineHeight;
                }
            }

            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            _textBitmap = bmp;
        }

        private Color BlendWithBackground(Brush fg)
        {
            var fgColor = (fg as SolidColorBrush)?.Color ?? Colors.Gray;
            var bgBrush = FindBrush("EditorBackgroundBrush") as SolidColorBrush;
            var bgColor = bgBrush != null ? bgBrush.Color : Colors.White;
            double ratio = 0.55;
            byte r = (byte)(fgColor.R * ratio + bgColor.R * (1 - ratio));
            byte g = (byte)(fgColor.G * ratio + bgColor.G * (1 - ratio));
            byte b = (byte)(fgColor.B * ratio + bgColor.B * (1 - ratio));
            return Color.FromRgb(r, g, b);
        }

        private void DrawViewportIndicator(DrawingContext dc)
        {
            if (_editor == null) return;
            double extent = _editor.ExtentHeight;
            double viewport = _editor.ViewportHeight;
            double offset = _editor.VerticalOffset;
            if (extent <= 0 || viewport <= 0) return;
            if (viewport >= extent) return;

            double mapHeight = _tooLarge ? 0 : (_textBitmap != null ? _textBitmap.Height : extent);
            if (mapHeight <= 0) return;

            double scale = mapHeight / extent;
            double top = offset * scale;
            double h = viewport * scale;
            if (h < 6) h = 6;
            if (top < 0) top = 0;
            if (top + h > mapHeight) top = mapHeight - h;

            var accent = FindBrush("AccentBrush");
            var fill = new SolidColorBrush((accent as SolidColorBrush)?.Color ?? Colors.Gray) { Opacity = 0.18 };
            fill.Freeze();
            var pen = new Pen(accent, 1.0);
            dc.DrawRectangle(fill, pen, new Rect(0, top, RenderSize.Width, h));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_editor == null || _tooLarge) return;
            e.Handled = true;
            _dragging = true;
            CaptureMouse();
            ScrollToY(e.GetPosition(this).Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            e.Handled = true;
            ScrollToY(e.GetPosition(this).Y);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ScrollToY(double mapY)
        {
            if (_editor == null) return;
            double extent = _editor.ExtentHeight;
            double viewport = _editor.ViewportHeight;
            if (extent <= 0 || viewport <= 0) return;
            double mapHeight = _textBitmap != null ? _textBitmap.Height : extent;
            if (mapHeight <= 0) return;
            double scale = extent / mapHeight;
            double target = mapY * scale - viewport / 2;
            if (target < 0) target = 0;
            if (target > extent - viewport) target = extent - viewport;
            _editor.ScrollToVerticalOffset(target);
        }

        private Brush FindBrush(string key)
        {
            var b = Application.Current != null ? Application.Current.TryFindResource(key) as Brush : null;
            return b ?? Brushes.Transparent;
        }
    }
}
