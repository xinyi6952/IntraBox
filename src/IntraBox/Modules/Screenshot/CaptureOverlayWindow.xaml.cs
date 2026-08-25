using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IntraBox.Core;
using Microsoft.Win32;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using WpfRect = System.Windows.Rect;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>
    /// 全屏覆盖层：冻结画面、框选（可调边角）、图标工具栏标注。
    /// 钉住后打开独立置顶窗口并保留工具栏，覆盖层按需销毁。
    /// </summary>
    public partial class CaptureOverlayWindow : Window
    {
        private static CaptureOverlayWindow _open;

        private Bitmap _fullBmp;
        private Rectangle _virtualPx;
        private Point _dragStart;
        private bool _dragging;
        private bool _hasSel;
        private bool _resizing;
        private bool _movingSel;
        private int _handle = -1;
        private WpfRect _sel;
        private AnnotationSession _anno;
        private bool _restoreMain;
        private Window _main;

        public CaptureOverlayWindow()
        {
            InitializeComponent();
            Bar.ToolChanged += (s, e) => { if (_anno != null) _anno.Tool = Bar.Tool; };
            Bar.OcrClicked += (s, e) => RunOcr();
            Bar.ColorChanged += (s, e) => { if (_anno != null) { _anno.Color = Bar.StrokeColor; _anno.ApplyStyle(); } };
            Bar.WidthChanged += (s, e) => { if (_anno != null) { _anno.Thickness = Bar.StrokeWidth; _anno.ApplyStyle(); } };
            Bar.UndoClicked += (s, e) => { if (_anno != null) _anno.Undo(); };
            Bar.RedoClicked += (s, e) => { if (_anno != null) _anno.Redo(); };
            Bar.CopyClicked += (s, e) => CopyResult(false);
            Bar.SaveClicked += (s, e) => SaveResult(false);
            Bar.ConfirmClicked += (s, e) => CopyResult(true);
            Bar.CancelClicked += (s, e) => CloseAndDispose();
            Bar.PinClicked += (s, e) => Pin();
            Bar.SetPinnedMode(false);
        }

        public static void ShowNew()
        {
            if (_open != null) return;
            var main = Application.Current != null ? Application.Current.MainWindow : null;
            bool restore = false;
            if (main != null && main.IsVisible)
            {
                main.Hide();
                restore = true;
            }

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    var w = new CaptureOverlayWindow();
                    w._main = main;
                    w._restoreMain = restore;
                    w.Closed += (s2, e2) =>
                    {
                        _open = null;
                        if (w._restoreMain && main != null) main.Show();
                    };
                    _open = w;
                    w.BeginCapture();
                }
                catch (Exception ex)
                {
                    if (restore && main != null) main.Show();
                    MessageBox.Show("截屏失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            timer.Start();
        }

        private void BeginCapture()
        {
            _virtualPx = ScreenCapture.VirtualScreenPixels();
            _fullBmp = ScreenCapture.CaptureVirtualScreen();
            BgImage.Source = ScreenCapture.ToBitmapSource(_fullBmp);

            double dpiX = 96, dpiY = 96;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = g.DpiX;
                dpiY = g.DpiY;
            }
            Left = _virtualPx.X * 96.0 / dpiX;
            Top = _virtualPx.Y * 96.0 / dpiY;
            Width = _virtualPx.Width * 96.0 / dpiX;
            Height = _virtualPx.Height * 96.0 / dpiY;
            _anno = new AnnotationSession(Layer, () => _sel, MosaicPreview);
            _anno.Tool = Bar.Tool;
            _anno.Color = Bar.StrokeColor;
            _anno.Thickness = Bar.StrokeWidth;
            Show();
            Activate();
            Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_anno != null && _anno.Selected != null) _anno.Deselect();
                else CloseAndDispose();
            }
            else if (e.Key == Key.Enter && _hasSel) CopyResult(true);
            else if (e.Key == Key.Delete && _anno != null && _anno.Selected != null)
            {
                if (ConfirmHelper.DeleteOverTopmost(this, "删除当前选中的标注？"))
                    _anno.DeleteSelected();
            }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && _anno != null)
                _anno.Undo();
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && _anno != null)
                _anno.Redo();
        }

        private void Layer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(Layer);
            if (e.ClickCount == 2)
            {
                _sel = new WpfRect(0, 0, ActualWidth, ActualHeight);
                _hasSel = true;
                FinishSelect();
                e.Handled = true;
                return;
            }

            if (_hasSel)
            {
                int h = HitHandle(p);
                if (h >= 0)
                {
                    _resizing = true;
                    _handle = h;
                    Layer.CaptureMouse();
                    return;
                }
                if (Bar.Tool == "select")
                {
                    if (_anno != null && _anno.OnMouseDown(p))
                        return;
                    if (_sel.Contains(p) || HitBorder(p))
                    {
                        _movingSel = true;
                        _dragStart = p;
                        Layer.CaptureMouse();
                        return;
                    }
                }
                else
                {
                    if (HitBorder(p))
                    {
                        _movingSel = true;
                        _dragStart = p;
                        Layer.CaptureMouse();
                        return;
                    }
                    if (_anno != null && _anno.OnMouseDown(p))
                        return;
                }
            }

            _dragging = true;
            _dragStart = p;
            Layer.CaptureMouse();
        }

        private void Layer_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(Layer);
            if (_dragging)
            {
                _sel = AnnotationSession.Normalize(_dragStart, p);
                RedrawMask();
            }
            else if (_resizing)
            {
                ResizeSel(p);
                RedrawMask();
                PlaceToolbar();
            }
            else if (_movingSel)
            {
                double dx = p.X - _dragStart.X;
                double dy = p.Y - _dragStart.Y;
                _dragStart = p;
                _sel = new WpfRect(_sel.X + dx, _sel.Y + dy, _sel.Width, _sel.Height);
                ClampSel();
                RedrawMask();
                PlaceToolbar();
            }
            else if (_anno != null)
            {
                _anno.OnMouseMove(p);
            }

            if (_hasSel && !_dragging)
            {
                int h = HitHandle(p);
                if (h == 0 || h == 4) Cursor = Cursors.SizeNWSE;
                else if (h == 2 || h == 6) Cursor = Cursors.SizeNESW;
                else if (h == 1 || h == 5) Cursor = Cursors.SizeNS;
                else if (h == 3 || h == 7) Cursor = Cursors.SizeWE;
                else if (HitBorder(p)) Cursor = Cursors.SizeAll;
                else Cursor = Bar.Tool == "select" ? Cursors.Arrow : Cursors.Cross;
            }
        }

        private void Layer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Layer.ReleaseMouseCapture();
            var p = e.GetPosition(Layer);
            if (_dragging)
            {
                _dragging = false;
                _sel = AnnotationSession.Normalize(_dragStart, p);
                if (_sel.Width < 8 || _sel.Height < 8)
                {
                    _hasSel = false;
                    Bar.Visibility = Visibility.Collapsed;
                    ClearMask();
                    return;
                }
                _hasSel = true;
                FinishSelect();
                return;
            }
            if (_resizing || _movingSel)
            {
                _resizing = false;
                _movingSel = false;
                PlaceToolbar();
                return;
            }
            if (_anno != null) _anno.OnMouseUp(p);
        }

        private void FinishSelect()
        {
            HintText.Visibility = Visibility.Collapsed;
            RedrawMask();
            PlaceToolbar();
            Bar.Visibility = Visibility.Visible;
        }

        private void PlaceToolbar()
        {
            Bar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = Bar.DesiredSize.Width;
            double th = Bar.DesiredSize.Height;
            if (tw < 10) tw = 480;
            if (th < 10) th = 72;
            double x = _sel.X;
            double y = _sel.Bottom + 8;
            if (y + th > ActualHeight) y = _sel.Y - th - 8;
            if (y < 0) y = 8;
            if (x + tw > ActualWidth) x = Math.Max(0, ActualWidth - tw - 8);
            Bar.Margin = new Thickness(x, y, 0, 0);
        }

        private void RedrawMask()
        {
            for (int i = Layer.Children.Count - 1; i >= 0; i--)
            {
                var fe = Layer.Children[i] as FrameworkElement;
                if (fe == null) continue;
                var tag = fe.Tag as string;
                if (tag == "mask" || (tag != null && tag.StartsWith("h", StringComparison.Ordinal)))
                    Layer.Children.RemoveAt(i);
            }
            var dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            AddMask(0, 0, ActualWidth, _sel.Y, dim);
            AddMask(0, _sel.Bottom, ActualWidth, Math.Max(0, ActualHeight - _sel.Bottom), dim);
            AddMask(0, _sel.Y, _sel.X, _sel.Height, dim);
            AddMask(_sel.Right, _sel.Y, Math.Max(0, ActualWidth - _sel.Right), _sel.Height, dim);

            var border = new WpfRectangle
            {
                Width = _sel.Width,
                Height = _sel.Height,
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                Tag = "mask",
                IsHitTestVisible = false
            };
            Canvas.SetLeft(border, _sel.X);
            Canvas.SetTop(border, _sel.Y);
            Layer.Children.Insert(0, border);

            double[] xs = { _sel.Left, _sel.Left + _sel.Width / 2, _sel.Right };
            double[] ys = { _sel.Top, _sel.Top + _sel.Height / 2, _sel.Bottom };
            int[] ids = { 0, 1, 2, 7, -1, 3, 6, 5, 4 };
            int k = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int id = ids[k++];
                    if (id < 0) continue;
                    var h = new WpfRectangle
                    {
                        Width = 8,
                        Height = 8,
                        Fill = Brushes.White,
                        Stroke = Brushes.DeepSkyBlue,
                        StrokeThickness = 1,
                        Tag = "h" + id
                    };
                    Canvas.SetLeft(h, xs[col] - 4);
                    Canvas.SetTop(h, ys[row] - 4);
                    Layer.Children.Add(h);
                }
            }
        }

        private void AddMask(double x, double y, double w, double h, System.Windows.Media.Brush fill)
        {
            if (w <= 0 || h <= 0) return;
            var r = new WpfRectangle
            {
                Width = w,
                Height = h,
                Fill = fill,
                Tag = "mask",
                IsHitTestVisible = false
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            Layer.Children.Insert(0, r);
        }

        private void ClearMask()
        {
            for (int i = Layer.Children.Count - 1; i >= 0; i--)
            {
                var fe = Layer.Children[i] as FrameworkElement;
                if (fe != null && fe.Tag is string)
                    Layer.Children.RemoveAt(i);
            }
        }

        private int HitHandle(Point p)
        {
            for (int i = 0; i < Layer.Children.Count; i++)
            {
                var fe = Layer.Children[i] as FrameworkElement;
                if (fe == null) continue;
                var tag = fe.Tag as string;
                if (tag == null || !tag.StartsWith("h", StringComparison.Ordinal) || tag.Length < 2) continue;
                int id;
                if (!int.TryParse(tag.Substring(1), out id)) continue;
                double l = Canvas.GetLeft(fe);
                double t = Canvas.GetTop(fe);
                if (p.X >= l && p.X <= l + fe.Width && p.Y >= t && p.Y <= t + fe.Height)
                    return id;
            }
            return -1;
        }

        private bool HitBorder(Point p)
        {
            const double t = 6;
            var r = _sel;
            if (p.X < r.Left - t || p.X > r.Right + t || p.Y < r.Top - t || p.Y > r.Bottom + t)
                return false;
            return p.X <= r.Left + t || p.X >= r.Right - t || p.Y <= r.Top + t || p.Y >= r.Bottom - t;
        }

        private void ResizeSel(Point p)
        {
            double l = _sel.Left, t = _sel.Top, r = _sel.Right, b = _sel.Bottom;
            switch (_handle)
            {
                case 0: l = p.X; t = p.Y; break;
                case 1: t = p.Y; break;
                case 2: r = p.X; t = p.Y; break;
                case 3: r = p.X; break;
                case 4: r = p.X; b = p.Y; break;
                case 5: b = p.Y; break;
                case 6: l = p.X; b = p.Y; break;
                case 7: l = p.X; break;
            }
            _sel = AnnotationSession.Normalize(new Point(l, t), new Point(r, b));
            if (_sel.Width < 8) _sel.Width = 8;
            if (_sel.Height < 8) _sel.Height = 8;
            ClampSel();
        }

        private void ClampSel()
        {
            double x = Math.Max(0, Math.Min(_sel.X, ActualWidth - 8));
            double y = Math.Max(0, Math.Min(_sel.Y, ActualHeight - 8));
            double w = Math.Min(_sel.Width, ActualWidth - x);
            double h = Math.Min(_sel.Height, ActualHeight - y);
            _sel = new WpfRect(x, y, Math.Max(8, w), Math.Max(8, h));
        }

        private ImageSource MosaicPreview(WpfRect dip)
        {
            if (_fullBmp == null) return null;
            try
            {
                var px = DipToPx(dip);
                using (var crop = ScreenCapture.Crop(_fullBmp, px))
                {
                    ScreenCapture.Pixelate(crop, new Rectangle(0, 0, crop.Width, crop.Height), 12);
                    return ScreenCapture.ToBitmapSource(crop);
                }
            }
            catch
            {
                return null;
            }
        }

        private void RunOcr()
        {
            if (!_hasSel || _fullBmp == null)
            {
                MessageBox.Show("请先框选截图区域。", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Bitmap crop = null;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                crop = ScreenCapture.Crop(_fullBmp, DipToPx(_sel));
                string text = OcrService.Recognize(crop);
                var w = new OcrResultWindow(text);
                w.Owner = this;
                w.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("OCR 失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (crop != null) crop.Dispose();
                GcHelper.CollectSafely();
            }
        }

        private void CopyResult(bool close)
        {
            var src = BuildResult();
            if (src == null) return;
            string err;
            if (!ClipboardHelper.TrySetImage(src, out err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (close) CloseAndDispose();
        }

        private void SaveResult(bool close)
        {
            var bmp = BuildResultBitmap();
            if (bmp == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "保存截屏",
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                FileName = "screenshot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var fmt = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        ? ImageFormat.Jpeg : ImageFormat.Png;
                    bmp.Save(dlg.FileName, fmt);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                    bmp.Dispose();
                    return;
                }
            }
            bmp.Dispose();
            if (close) CloseAndDispose();
        }

        private void Pin()
        {
            var src = BuildResult();
            if (src == null) return;
            _restoreMain = false;
            var pin = new PinImageWindow(src, _sel.Width, _sel.Height);
            pin.Left = Left + _sel.X;
            pin.Top = Top + _sel.Y;
            pin.Show();
            CloseAndDispose();
            if (_main != null) _main.Show();
        }

        private BitmapSource BuildResult()
        {
            using (var bmp = BuildResultBitmap())
            {
                if (bmp == null) return null;
                return ScreenCapture.ToBitmapSource(bmp);
            }
        }

        private Bitmap BuildResultBitmap()
        {
            if (_fullBmp == null || !_hasSel) return null;
            var crop = DipToPx(_sel);
            var slice = ScreenCapture.Crop(_fullBmp, crop);
            DrawAnnotations(slice, crop);
            return slice;
        }

        private Rectangle DipToPx(WpfRect r)
        {
            double sx = _fullBmp.Width / ActualWidth;
            double sy = _fullBmp.Height / ActualHeight;
            int x = (int)Math.Round(r.X * sx);
            int y = (int)Math.Round(r.Y * sy);
            int w = (int)Math.Round(r.Width * sx);
            int h = (int)Math.Round(r.Height * sy);
            return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
        }

        private void DrawAnnotations(Bitmap slice, Rectangle cropPx)
        {
            if (_anno == null) return;
            double sx = _fullBmp.Width / ActualWidth;
            double sy = _fullBmp.Height / ActualHeight;
            using (var g = Graphics.FromImage(slice))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var el in _anno.Items)
                    RenderElement(g, el, cropPx, sx, sy, slice);
            }
        }

        internal static void RenderElement(Graphics g, UIElement el, Rectangle cropPx, double sx, double sy, Bitmap slice)
        {
            var fe = el as FrameworkElement;
            var tag = fe != null ? fe.Tag as AnnoTag : null;
            string kind = tag != null ? tag.Kind : "";
            var color = tag != null ? System.Drawing.Color.FromArgb(tag.Color.A, tag.Color.R, tag.Color.G, tag.Color.B)
                : System.Drawing.Color.Red;
            float thick = tag != null ? (float)Math.Max(1, tag.Thickness) : 2f;

            if (kind == "mosaic")
            {
                var b = AnnotationSession.Bounds(el);
                var r = new Rectangle(
                    (int)Math.Round(b.X * sx) - cropPx.X,
                    (int)Math.Round(b.Y * sy) - cropPx.Y,
                    Math.Max(1, (int)Math.Round(b.Width * sx)),
                    Math.Max(1, (int)Math.Round(b.Height * sy)));
                ScreenCapture.Pixelate(slice, r, 12);
                return;
            }

            using (var pen = new System.Drawing.Pen(color, thick))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (kind == "arrow" && tag != null)
                {
                    int x1 = (int)Math.Round(tag.A.X * sx) - cropPx.X;
                    int y1 = (int)Math.Round(tag.A.Y * sy) - cropPx.Y;
                    int x2 = (int)Math.Round(tag.B.X * sx) - cropPx.X;
                    int y2 = (int)Math.Round(tag.B.Y * sy) - cropPx.Y;
                    g.DrawLine(pen, x1, y1, x2, y2);
                    DrawArrowHead(g, brush, x1, y1, x2, y2, 14);
                    return;
                }
                var poly = el as Polyline;
                if (poly != null && poly.Points.Count > 1)
                {
                    var pts = new System.Drawing.Point[poly.Points.Count];
                    for (int i = 0; i < poly.Points.Count; i++)
                    {
                        pts[i] = new System.Drawing.Point(
                            (int)Math.Round(poly.Points[i].X * sx) - cropPx.X,
                            (int)Math.Round(poly.Points[i].Y * sy) - cropPx.Y);
                    }
                    g.DrawLines(pen, pts);
                    return;
                }
                var tb = el as TextBox;
                if (tb != null)
                {
                    double left = Canvas.GetLeft(tb);
                    double top = Canvas.GetTop(tb);
                    int x = (int)Math.Round(left * sx) - cropPx.X;
                    int y = (int)Math.Round(top * sy) - cropPx.Y;
                    using (var font = new Font("Segoe UI", Math.Max(10, (float)tb.FontSize * 0.75f), System.Drawing.FontStyle.Bold))
                        g.DrawString(tb.Text ?? "", font, brush, x, y);
                    return;
                }
                var b2 = AnnotationSession.Bounds(el);
                int dx = (int)Math.Round(b2.X * sx) - cropPx.X;
                int dy = (int)Math.Round(b2.Y * sy) - cropPx.Y;
                int dw = Math.Max(1, (int)Math.Round(b2.Width * sx));
                int dh = Math.Max(1, (int)Math.Round(b2.Height * sy));
                if (kind == "ellipse" || el is Ellipse)
                    g.DrawEllipse(pen, dx, dy, dw, dh);
                else
                    g.DrawRectangle(pen, dx, dy, dw, dh);
            }
        }

        internal static void DrawArrowHead(Graphics g, System.Drawing.Brush brush, int x1, int y1, int x2, int y2, int size)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            dx /= len; dy /= len;
            var pts = new[]
            {
                new System.Drawing.Point(x2, y2),
                new System.Drawing.Point((int)(x2 - dx * size + dy * size * 0.5), (int)(y2 - dy * size - dx * size * 0.5)),
                new System.Drawing.Point((int)(x2 - dx * size - dy * size * 0.5), (int)(y2 - dy * size + dx * size * 0.5))
            };
            g.FillPolygon(brush, pts);
        }

        private void CloseAndDispose()
        {
            if (_fullBmp != null)
            {
                _fullBmp.Dispose();
                _fullBmp = null;
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_fullBmp != null)
            {
                _fullBmp.Dispose();
                _fullBmp = null;
            }
            if (BgImage != null) BgImage.Source = null;
            base.OnClosed(e);
            GcHelper.CollectSafely();
        }
    }
}
