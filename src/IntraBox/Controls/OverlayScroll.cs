using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IntraBox.Controls
{
    /// <summary>
    /// Overlay 滚动条：滚动时短暂显示滑块。挂在 ScrollViewer 上，子级 ScrollBar 用 DataTrigger 读取。
    /// </summary>
    public static class OverlayScroll
    {
        public static readonly DependencyProperty WatchProperty = DependencyProperty.RegisterAttached(
            "Watch", typeof(bool), typeof(OverlayScroll),
            new PropertyMetadata(false, OnWatchChanged));

        public static readonly DependencyProperty IsScrollingProperty = DependencyProperty.RegisterAttached(
            "IsScrolling", typeof(bool), typeof(OverlayScroll),
            new PropertyMetadata(false));

        private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
            "Timer", typeof(DispatcherTimer), typeof(OverlayScroll));

        public static void SetWatch(DependencyObject d, bool value) { d.SetValue(WatchProperty, value); }
        public static bool GetWatch(DependencyObject d) { return (bool)d.GetValue(WatchProperty); }
        public static void SetIsScrolling(DependencyObject d, bool value) { d.SetValue(IsScrollingProperty, value); }
        public static bool GetIsScrolling(DependencyObject d) { return (bool)d.GetValue(IsScrollingProperty); }

        private static void OnWatchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var sv = d as ScrollViewer;
            if (sv == null) return;
            sv.ScrollChanged -= OnScrollChanged;
            sv.Unloaded -= OnUnloaded;
            if ((bool)e.NewValue)
            {
                sv.ScrollChanged += OnScrollChanged;
                sv.Unloaded += OnUnloaded;
            }
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;
            var sv = (ScrollViewer)sender;
            SetIsScrolling(sv, true);

            var timer = sv.GetValue(TimerProperty) as DispatcherTimer;
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timer.Tag = sv;
                timer.Tick += OnTimerTick;
                sv.SetValue(TimerProperty, timer);
            }
            timer.Stop();
            timer.Start();
        }

        private static void OnTimerTick(object sender, EventArgs e)
        {
            var timer = sender as DispatcherTimer;
            if (timer == null) return;
            timer.Stop();
            var sv = timer.Tag as ScrollViewer;
            if (sv != null) SetIsScrolling(sv, false);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;
            sv.ScrollChanged -= OnScrollChanged;
            sv.Unloaded -= OnUnloaded;
            var timer = sv.GetValue(TimerProperty) as DispatcherTimer;
            if (timer != null)
            {
                timer.Stop();
                timer.Tick -= OnTimerTick;
                timer.Tag = null;
                sv.ClearValue(TimerProperty);
            }
            SetIsScrolling(sv, false);
        }
    }
}
