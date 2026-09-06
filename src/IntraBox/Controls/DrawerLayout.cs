using System;
using System.Windows;
using System.Windows.Controls;

namespace IntraBox.Controls
{
    /// <summary>
    /// 右侧抽屉宽度：还原时按宿主宽度比例；最大化铺满导航右侧整块工作区（盖住模块标题与工具顶栏）。
    /// </summary>
    public static class DrawerLayout
    {
        public static void Apply(Border panel, Border mask, FrameworkElement host, bool maximized, double ratio)
        {
            if (panel == null) return;
            if (maximized)
            {
                panel.Width = double.NaN;
                panel.HorizontalAlignment = HorizontalAlignment.Stretch;
                panel.BorderThickness = new Thickness(0);
            }
            else
            {
                double w = host != null ? host.ActualWidth : 0;
                if (w < 1 && host != null && host.Parent is FrameworkElement parent)
                    w = parent.ActualWidth;
                if (w < 1) w = 800;
                if (ratio < 0.2) ratio = 0.2;
                if (ratio > 1) ratio = 1;
                double target = Math.Floor(w * ratio);
                if (target < 1) target = 1;
                if (target > w) target = w;
                panel.Width = target;
                panel.HorizontalAlignment = HorizontalAlignment.Right;
                panel.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            if (mask != null)
                mask.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;

            bool fill = host != null && host.Visibility == Visibility.Visible && maximized;
            var win = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (win != null)
                win.SetWorkspaceFill(fill);
        }
    }
}
