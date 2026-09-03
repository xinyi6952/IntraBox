using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>
    /// 标注会话：矩形/椭圆/箭头/画笔/文字/马赛克，选中后可移动、改色、改粗细、删除，支持撤销重做。
    /// 画布坐标即 DIP；导出时由调用方做像素换算。
    /// </summary>
    internal sealed class AnnotationSession
    {
        private readonly Canvas _canvas;
        private readonly List<UIElement> _items = new List<UIElement>();
        private readonly Stack<UIElement> _redo = new Stack<UIElement>();
        private readonly Func<WpfRect> _clip;
        private readonly Func<WpfRect, ImageSource> _mosaicPreview;

        private bool _drawing;
        private bool _moving;
        private WpfPoint _start;
        private WpfPoint _moveLast;
        private UIElement _preview;
        private Polyline _pen;
        private UIElement _selected;
        private Rectangle _selBox;

        public string Tool = "select";
        public MediaColor Color = (MediaColor)ColorConverter.ConvertFromString("#E53935");
        public double Thickness = 2;

        public AnnotationSession(Canvas canvas, Func<WpfRect> clip, Func<WpfRect, ImageSource> mosaicPreview)
        {
            _canvas = canvas;
            _clip = clip;
            _mosaicPreview = mosaicPreview;
        }

        public IList<UIElement> Items { get { return _items; } }
        public UIElement Selected { get { return _selected; } }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++)
                _canvas.Children.Remove(_items[i]);
            _items.Clear();
            _redo.Clear();
            Deselect();
        }

        public bool OnMouseDown(WpfPoint p)
        {
            var clip = _clip();
            if (clip.Width < 2 || !clip.Contains(p)) return false;

            var hit = HitItem(p);
            if (hit != null)
            {
                Select(hit);
                _moving = true;
                _moveLast = p;
                _canvas.CaptureMouse();
                return true;
            }

            if (Tool == "select")
            {
                Deselect();
                return false;
            }

            Deselect();
            _drawing = true;
            _start = p;
            _canvas.CaptureMouse();

            if (Tool == "pen")
            {
                _pen = new Polyline
                {
                    Stroke = new SolidColorBrush(Color),
                    StrokeThickness = Thickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Tag = Tag("pen")
                };
                _pen.Points.Add(p);
                _canvas.Children.Add(_pen);
                _preview = _pen;
            }
            else if (Tool == "text")
            {
                PlaceText(p);
                _drawing = false;
                _canvas.ReleaseMouseCapture();
            }
            return true;
        }

        public void OnMouseMove(WpfPoint p)
        {
            if (_moving && _selected != null)
            {
                Offset(_selected, p.X - _moveLast.X, p.Y - _moveLast.Y);
                _moveLast = p;
                UpdateSelBox();
                return;
            }
            if (!_drawing) return;
            if (Tool == "pen" && _pen != null)
            {
                _pen.Points.Add(p);
                return;
            }
            if (Tool == "text") return;
            if (_preview != null) _canvas.Children.Remove(_preview);
            _preview = MakeShape(Tool, _start, Clamp(p, _clip()));
            if (_preview != null) _canvas.Children.Add(_preview);
        }

        public void OnMouseUp(WpfPoint p)
        {
            _canvas.ReleaseMouseCapture();
            if (_moving)
            {
                _moving = false;
                return;
            }
            if (!_drawing) return;
            _drawing = false;
            if (Tool == "pen" && _pen != null)
            {
                if (_pen.Points.Count > 1)
                {
                    _items.Add(_pen);
                    _redo.Clear();
                }
                else _canvas.Children.Remove(_pen);
                _pen = null;
                _preview = null;
                return;
            }
            if (_preview != null)
            {
                var r = Normalize(_start, p);
                if (r.Width < 3 && r.Height < 3)
                    _canvas.Children.Remove(_preview);
                else
                {
                    _items.Add(_preview);
                    _redo.Clear();
                }
                _preview = null;
            }
        }

        public void Undo()
        {
            if (_items.Count == 0) return;
            Deselect();
            var last = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            _canvas.Children.Remove(last);
            _redo.Push(last);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var el = _redo.Pop();
            _items.Add(el);
            _canvas.Children.Add(el);
        }

        public void DeleteSelected()
        {
            if (_selected == null) return;
            _items.Remove(_selected);
            _canvas.Children.Remove(_selected);
            _redo.Push(_selected);
            Deselect();
        }

        public void ApplyStyle()
        {
            if (_selected == null) return;
            var brush = new SolidColorBrush(Color);
            var line = _selected as Shape;
            if (line != null)
            {
                line.Stroke = brush;
                line.StrokeThickness = Thickness;
            }
            var tb = _selected as TextBox;
            if (tb != null)
            {
                tb.Foreground = brush;
                tb.FontSize = 12 + Thickness * 2;
            }
            var tag = _selected is FrameworkElement ? ((FrameworkElement)_selected).Tag as AnnoTag : null;
            if (tag != null)
            {
                tag.Color = Color;
                tag.Thickness = Thickness;
            }
            var path = _selected as Path;
            if (path != null && tag != null && tag.Kind == "arrow")
            {
                path.Data = ArrowGeometry(tag.A, tag.B);
                path.Stroke = brush;
                path.Fill = brush;
                path.StrokeThickness = Thickness;
            }
        }

        public void Deselect()
        {
            _selected = null;
            if (_selBox != null)
            {
                _canvas.Children.Remove(_selBox);
                _selBox = null;
            }
        }

        private void Select(UIElement el)
        {
            _selected = el;
            UpdateSelBox();
        }

        private void UpdateSelBox()
        {
            if (_selBox != null)
            {
                _canvas.Children.Remove(_selBox);
                _selBox = null;
            }
            if (_selected == null) return;
            var b = Bounds(_selected);
            _selBox = new Rectangle
            {
                Width = b.Width + 6,
                Height = b.Height + 6,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_selBox, b.X - 3);
            Canvas.SetTop(_selBox, b.Y - 3);
            _canvas.Children.Add(_selBox);
        }

        private UIElement HitItem(WpfPoint p)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var b = Bounds(_items[i]);
                b.Inflate(6, 6);
                if (b.Contains(p)) return _items[i];
            }
            return null;
        }

        private UIElement MakeShape(string tool, WpfPoint a, WpfPoint b)
        {
            var r = Normalize(a, b);
            var brush = new SolidColorBrush(Color);
            if (tool == "rect")
            {
                var sh = new Rectangle
                {
                    Width = r.Width,
                    Height = r.Height,
                    Stroke = brush,
                    StrokeThickness = Thickness,
                    Fill = Brushes.Transparent,
                    Tag = Tag("rect")
                };
                Canvas.SetLeft(sh, r.X);
                Canvas.SetTop(sh, r.Y);
                return sh;
            }
            if (tool == "ellipse")
            {
                var sh = new Ellipse
                {
                    Width = r.Width,
                    Height = r.Height,
                    Stroke = brush,
                    StrokeThickness = Thickness,
                    Fill = Brushes.Transparent,
                    Tag = Tag("ellipse")
                };
                Canvas.SetLeft(sh, r.X);
                Canvas.SetTop(sh, r.Y);
                return sh;
            }
            if (tool == "arrow")
            {
                var tag = Tag("arrow");
                tag.A = a;
                tag.B = b;
                return new Path
                {
                    Data = ArrowGeometry(a, b),
                    Stroke = brush,
                    Fill = brush,
                    StrokeThickness = Thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    Tag = tag
                };
            }
            if (tool == "mosaic")
            {
                UIElement el;
                ImageSource src = _mosaicPreview != null ? _mosaicPreview(r) : null;
                if (src != null)
                {
                    var img = new Image
                    {
                        Source = src,
                        Width = r.Width,
                        Height = r.Height,
                        Stretch = Stretch.Fill,
                        Tag = Tag("mosaic")
                    };
                    Canvas.SetLeft(img, r.X);
                    Canvas.SetTop(img, r.Y);
                    el = img;
                }
                else
                {
                    // 预览图暂时拿不到（如底图未就绪）：用中性灰占位，不再用易误解的黄色块。
                    var sh = new Rectangle
                    {
                        Width = r.Width,
                        Height = r.Height,
                        Fill = new SolidColorBrush(MediaColor.FromArgb(60, 128, 128, 128)),
                        Stroke = new SolidColorBrush(MediaColor.FromRgb(160, 160, 160)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 3, 2 },
                        Tag = Tag("mosaic")
                    };
                    Canvas.SetLeft(sh, r.X);
                    Canvas.SetTop(sh, r.Y);
                    el = sh;
                }
                return el;
            }
            return null;
        }

        private void PlaceText(WpfPoint p)
        {
            var tb = new TextBox
            {
                FontSize = 12 + Thickness * 2,
                MinWidth = 80,
                Background = new SolidColorBrush(MediaColor.FromArgb(160, 0, 0, 0)),
                Foreground = new SolidColorBrush(Color),
                BorderBrush = new SolidColorBrush(Color),
                CaretBrush = new SolidColorBrush(Color),
                Tag = Tag("text")
            };
            Canvas.SetLeft(tb, p.X);
            Canvas.SetTop(tb, p.Y);
            _canvas.Children.Add(tb);
            _items.Add(tb);
            _redo.Clear();
            tb.Focus();
        }

        private AnnoTag Tag(string kind)
        {
            return new AnnoTag { Kind = kind, Color = Color, Thickness = Thickness };
        }

        internal static Geometry ArrowGeometry(WpfPoint a, WpfPoint b)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, false, false);
                ctx.LineTo(b, true, true);
                var v = b - a;
                if (v.Length > 1)
                {
                    v.Normalize();
                    var n = new Vector(-v.Y, v.X);
                    var p1 = b - v * 14 + n * 7;
                    var p2 = b - v * 14 - n * 7;
                    ctx.BeginFigure(p1, true, true);
                    ctx.LineTo(b, true, false);
                    ctx.LineTo(p2, true, false);
                }
            }
            g.Freeze();
            return g;
        }

        internal static WpfRect Bounds(UIElement el)
        {
            var line = el as Line;
            if (line != null)
                return Normalize(new WpfPoint(line.X1, line.Y1), new WpfPoint(line.X2, line.Y2));
            var poly = el as Polyline;
            if (poly != null && poly.Points.Count > 0)
            {
                double x1 = poly.Points[0].X, y1 = poly.Points[0].Y, x2 = x1, y2 = y1;
                for (int i = 1; i < poly.Points.Count; i++)
                {
                    if (poly.Points[i].X < x1) x1 = poly.Points[i].X;
                    if (poly.Points[i].Y < y1) y1 = poly.Points[i].Y;
                    if (poly.Points[i].X > x2) x2 = poly.Points[i].X;
                    if (poly.Points[i].Y > y2) y2 = poly.Points[i].Y;
                }
                return new WpfRect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
            }
            var path = el as Path;
            if (path != null && path.Data != null)
                return path.Data.Bounds;
            double left = Canvas.GetLeft(el);
            double top = Canvas.GetTop(el);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            var fe = el as FrameworkElement;
            double w = fe != null ? fe.ActualWidth : 0;
            double h = fe != null ? fe.ActualHeight : 0;
            if (w < 1 && fe != null) w = fe.Width;
            if (h < 1 && fe != null) h = fe.Height;
            if (double.IsNaN(w) || w < 1) w = 8;
            if (double.IsNaN(h) || h < 1) h = 8;
            return new WpfRect(left, top, w, h);
        }

        internal static void Offset(UIElement el, double dx, double dy)
        {
            var line = el as Line;
            if (line != null)
            {
                line.X1 += dx; line.Y1 += dy; line.X2 += dx; line.Y2 += dy;
                return;
            }
            var poly = el as Polyline;
            if (poly != null)
            {
                var pts = new PointCollection();
                foreach (var pt in poly.Points)
                    pts.Add(new WpfPoint(pt.X + dx, pt.Y + dy));
                poly.Points = pts;
                return;
            }
            var path = el as Path;
            var tag = el is FrameworkElement ? ((FrameworkElement)el).Tag as AnnoTag : null;
            if (path != null && tag != null && tag.Kind == "arrow")
            {
                tag.A = new WpfPoint(tag.A.X + dx, tag.A.Y + dy);
                tag.B = new WpfPoint(tag.B.X + dx, tag.B.Y + dy);
                path.Data = ArrowGeometry(tag.A, tag.B);
                return;
            }
            double left = Canvas.GetLeft(el);
            double top = Canvas.GetTop(el);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            Canvas.SetLeft(el, left + dx);
            Canvas.SetTop(el, top + dy);
        }

        internal static WpfRect Normalize(WpfPoint a, WpfPoint b)
        {
            return new WpfRect(new WpfPoint(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
                new WpfPoint(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        }

        private static WpfPoint Clamp(WpfPoint p, WpfRect r)
        {
            return new WpfPoint(
                Math.Max(r.Left, Math.Min(r.Right, p.X)),
                Math.Max(r.Top, Math.Min(r.Bottom, p.Y)));
        }

        private static double Distance(WpfPoint a, WpfPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    internal sealed class AnnoTag
    {
        public string Kind;
        public WpfPoint A;
        public WpfPoint B;
        public MediaColor Color;
        public double Thickness;
    }
}
