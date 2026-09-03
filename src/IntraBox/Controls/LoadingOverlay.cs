using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace IntraBox.Controls
{
    /// <summary>
    /// 可复用的加载遮罩：在目标元素上叠加半透明背景 + 旋转指示器 + 文本。
    /// 用 Adorner 实现，无需改动各模块 XAML 布局；Show/Hide 配对调用。
    /// 典型用法：后台任务开始时 LoadingOverlay.Show(this, "统计中…")，
    /// 完成/取消/出错时 LoadingOverlay.Hide(this)。
    /// </summary>
    public static class LoadingOverlay
    {
        private static readonly System.Collections.Generic.Dictionary<UIElement, LoadingAdorner> _map =
            new System.Collections.Generic.Dictionary<UIElement, LoadingAdorner>();
        // 每 target 一个世代号：Hide / 重新 Show 时递增，用于作废尚未布局完成时的延迟重试回调，
        // 避免模块已销毁或已 Hide 后延迟回调仍挂上 Adorner 残留遮罩。
        private static readonly System.Collections.Generic.Dictionary<UIElement, int> _gen =
            new System.Collections.Generic.Dictionary<UIElement, int>();

        public static void Show(FrameworkElement target, string text = null)
        {
            if (target == null) return;
            UIElement key = target;
            int gen = BumpGen(key);
            LoadingAdorner ad;
            if (_map.TryGetValue(key, out ad) && ad != null)
            {
                ad.Text = text;
                return;
            }
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null)
            {
                // 尚未布局完成，等一次 Dispatcher 再试；带世代守卫，期间若 Hide/重新 Show 则作废
                target.Dispatcher.BeginInvoke(new Action(() =>
                {
                    int cur;
                    if (!_gen.TryGetValue(key, out cur) || cur != gen) return;     // 已被取消
                    if (PresentationSource.FromVisual(target) == null) return;    // 已不在可视化树
                    Show(target, text);
                }), DispatcherPriority.Loaded);
                return;
            }
            ad = new LoadingAdorner(target) { Text = text };
            layer.Add(ad);
            _map[key] = ad;
        }

        public static void Hide(FrameworkElement target)
        {
            if (target == null) return;
            UIElement key = target;
            BumpGen(key); // 作废尚未触发的延迟重试
            LoadingAdorner ad;
            if (!_map.TryGetValue(key, out ad) || ad == null) return;
            var layer = AdornerLayer.GetAdornerLayer(target);
            try { if (layer != null) layer.Remove(ad); } catch { }
            ad.Stop();
            _map.Remove(key);
        }

        private static int BumpGen(UIElement key)
        {
            int g;
            _gen.TryGetValue(key, out g);
            g++;
            _gen[key] = g;
            return g;
        }
    }

    internal sealed class LoadingAdorner : Adorner
    {
        private const double Radius = 14.0;
        private const double Stroke = 3.0;
        private const double SpinStep = 8.0; // 度/帧
        private const double IntervalMs = 16.0;

        private readonly DispatcherTimer _timer;
        private readonly StreamGeometry _arc;
        private readonly EllipseGeometry _ring;
        private readonly Pen _arcPen;
        private readonly Pen _ringPen;
        private readonly Brush _overlay;
        private readonly Brush _textBrush;
        private double _angle;

        public string Text { get; set; }

        public LoadingAdorner(FrameworkElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = true; // 遮罩拦截鼠标，避免加载中误操作
            var accent = TryBrush("AccentBrush") ?? Brushes.Orange;
            var dim = TryBrush("WindowBgBrush") ?? new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
            _overlay = MakeSemi(dim, 0.55);
            _textBrush = TryBrush("TextPrimaryBrush") ?? Brushes.White;
            _arcPen = new Pen(accent, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _ringPen = new Pen(MakeSemi(accent, 0.25), Stroke);
            _arc = BuildArc(Radius, 270.0);
            _ring = new EllipseGeometry(new Point(0, 0), Radius, Radius);
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(IntervalMs) };
            _timer.Tick += (s, e) => { _angle = (_angle + SpinStep) % 360.0; InvalidateVisual(); };
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(_overlay, null, new Rect(0, 0, size.Width, size.Height));

            double cx = size.Width / 2.0;
            double cy = size.Height / 2.0 - 10;
            // 圆环 + 弧都以原点为中心，用变换平移/旋转，避免每帧克隆几何
            dc.PushTransform(new TranslateTransform(cx, cy));
            dc.DrawGeometry(null, _ringPen, _ring);
            dc.PushTransform(new RotateTransform(_angle));
            dc.DrawGeometry(null, _arcPen, _arc);
            dc.Pop();
            dc.Pop();

            if (!string.IsNullOrEmpty(Text))
            {
                double ppd = 1.0;
                try { ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                var ft = new FormattedText(
                    Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    13,
                    _textBrush,
                    ppd);
                dc.DrawText(ft, new Point(cx - ft.Width / 2, cy + Radius + 8));
            }
        }

        // 遮罩拦截输入：覆盖整个区域都视为命中本 adorner
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            var p = hitTestParameters.HitPoint;
            if (p.X >= 0 && p.X <= RenderSize.Width && p.Y >= 0 && p.Y <= RenderSize.Height)
                return new PointHitTestResult(this, p);
            return null;
        }

        private static StreamGeometry BuildArc(double r, double sweepDeg)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                double a0 = 0;
                var p0 = new Point(r * Math.Cos(a0), r * Math.Sin(a0));
                ctx.BeginFigure(p0, false, false);
                double a1 = sweepDeg * Math.PI / 180.0;
                var p1 = new Point(r * Math.Cos(a1), r * Math.Sin(a1));
                ctx.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private static Brush MakeSemi(Brush b, double opacity)
        {
            if (b is SolidColorBrush sc)
            {
                var c = sc.Color;
                return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
            }
            return b;
        }

        private static Brush TryBrush(string key)
        {
            return Application.Current != null
                ? Application.Current.TryFindResource(key) as Brush
                : null;
        }
    }
}
