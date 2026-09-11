using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IntraBox.Core;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace IntraBox.Modules.ScreenRuler
{
    /// <summary>全屏标尺：十字线 + 水平/垂直/矩形像素测量，并按 DPI 换算厘米/英寸。</summary>
    public partial class ScreenRulerOverlayWindow : Window
    {
        private static ScreenRulerOverlayWindow _open;

        public static bool IsOpen { get { return _open != null; } }
        private int _mode; // 0 H 1 V 2 Rect
        private bool _dragging;
        private Point _start;
        private Point _cur;
        private Line _hCross;
        private Line _vCross;
        private Line _measure;
        private System.Windows.Shapes.Rectangle _rect;
        private double _dpiX = 96, _dpiY = 96;

        public ScreenRulerOverlayWindow()
        {
            InitializeComponent();
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                _dpiX = g.DpiX;
                _dpiY = g.DpiY;
            }
            _hCross = NewLine();
            _vCross = NewLine();
            _measure = NewLine();
            _measure.StrokeThickness = 2;
            _rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 30, 144, 255)),
                Visibility = Visibility.Collapsed
            };
            Layer.Children.Add(_hCross);
            Layer.Children.Add(_vCross);
            Layer.Children.Add(_measure);
            Layer.Children.Add(_rect);
            UpdateHint();
        }

        public static void ShowNew()
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            var w = new ScreenRulerOverlayWindow();
            w.Closed += (s, e) => { _open = null; };
            _open = w;
            var px = ScreenCapture.VirtualScreenPixels();
            double dpiX = 96, dpiY = 96;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = g.DpiX;
                dpiY = g.DpiY;
            }
            w.Left = px.X * 96.0 / dpiX;
            w.Top = px.Y * 96.0 / dpiY;
            w.Width = px.Width * 96.0 / dpiX;
            w.Height = px.Height * 96.0 / dpiY;
            w.Show();
            w.Activate();
        }

        private static Line NewLine()
        {
            return new Line { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key == Key.H || e.Key == Key.D1) { _mode = 0; UpdateHint(); }
            if (e.Key == Key.V || e.Key == Key.D2) { _mode = 1; UpdateHint(); }
            if (e.Key == Key.R || e.Key == Key.D3) { _mode = 2; UpdateHint(); }
        }

        private void UpdateHint()
        {
            string m = _mode == 0 ? "水平" : (_mode == 1 ? "垂直" : "矩形");
            HintText.Text = "当前：" + m + "  ·  H/V/R 切换  ·  拖拽测量  ·  Esc 关闭";
        }

        private void Layer_MouseMove(object sender, MouseEventArgs e)
        {
            _cur = e.GetPosition(Layer);
            double w = Layer.ActualWidth, h = Layer.ActualHeight;
            _hCross.X1 = 0; _hCross.Y1 = _cur.Y; _hCross.X2 = w; _hCross.Y2 = _cur.Y;
            _vCross.X1 = _cur.X; _vCross.Y1 = 0; _vCross.X2 = _cur.X; _vCross.Y2 = h;
            double rx = Math.Min(_cur.X + 16, Math.Max(8, w - 220));
            double ry = Math.Min(_cur.Y + 16, Math.Max(8, h - 48));
            Readout.Margin = new Thickness(rx, ry, 0, 0);
            if (_dragging) UpdateMeasure();
            else ReadoutText.Text = ((int)_cur.X) + " , " + ((int)_cur.Y);
        }

        private void Layer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _start = e.GetPosition(Layer);
            _cur = _start;
            Layer.CaptureMouse();
            UpdateMeasure();
        }

        private void Layer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            Layer.ReleaseMouseCapture();
        }

        private void UpdateMeasure()
        {
            double x1 = _start.X, y1 = _start.Y, x2 = _cur.X, y2 = _cur.Y;
            if (_mode == 0)
            {
                _measure.Visibility = Visibility.Visible;
                _rect.Visibility = Visibility.Collapsed;
                _measure.X1 = x1; _measure.Y1 = y1; _measure.X2 = x2; _measure.Y2 = y1;
                int px = DipToPx(Math.Abs(x2 - x1), _dpiX);
                ReadoutText.Text = px + " px  " + ToCm(px, _dpiX) + " cm  " + ToIn(px, _dpiX) + " in";
            }
            else if (_mode == 1)
            {
                _measure.Visibility = Visibility.Visible;
                _rect.Visibility = Visibility.Collapsed;
                _measure.X1 = x1; _measure.Y1 = y1; _measure.X2 = x1; _measure.Y2 = y2;
                int px = DipToPx(Math.Abs(y2 - y1), _dpiY);
                ReadoutText.Text = px + " px  " + ToCm(px, _dpiY) + " cm  " + ToIn(px, _dpiY) + " in";
            }
            else
            {
                _measure.Visibility = Visibility.Collapsed;
                _rect.Visibility = Visibility.Visible;
                double l = Math.Min(x1, x2), t = Math.Min(y1, y2);
                double ww = Math.Abs(x2 - x1), hh = Math.Abs(y2 - y1);
                Canvas.SetLeft(_rect, l);
                Canvas.SetTop(_rect, t);
                _rect.Width = ww;
                _rect.Height = hh;
                int pw = DipToPx(ww, _dpiX), ph = DipToPx(hh, _dpiY);
                ReadoutText.Text = pw + " × " + ph + " px  " + ToCm(pw, _dpiX) + " × " + ToCm(ph, _dpiY) + " cm";
            }
        }

        private static int DipToPx(double dip, double dpi)
        {
            if (dpi < 1) dpi = 96;
            return (int)Math.Round(dip * dpi / 96.0);
        }

        private static string ToCm(int px, double dpi)
        {
            if (dpi < 1) dpi = 96;
            return (px * 2.54 / dpi).ToString("0.00");
        }

        private static string ToIn(int px, double dpi)
        {
            if (dpi < 1) dpi = 96;
            return (px / dpi).ToString("0.00");
        }
    }
}
