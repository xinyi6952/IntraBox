using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IntraBox.Modules.Todo
{
    public partial class TodoImagePreviewWindow : Window
    {
        private double _zoom = 1;

        public TodoImagePreviewWindow(string path)
        {
            InitializeComponent();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                TitleText.Text = "找不到图片文件";
                return;
            }
            TitleText.Text = Path.GetFileName(path) + "（滚轮缩放）";
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
                Loaded += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        ZoomFit_Click(s, e);
                    }), DispatcherPriority.Loaded);
                };
            }
            catch (Exception ex)
            {
                TitleText.Text = "无法打开图片：" + ex.Message;
            }
        }

        private void SetZoom(double zoom)
        {
            if (zoom < 0.1) zoom = 0.1;
            if (zoom > 8) zoom = 8;
            _zoom = zoom;
            ZoomScale.ScaleX = _zoom;
            ZoomScale.ScaleY = _zoom;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoom * 1.25);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoom / 1.25);
        }

        private void ZoomActual_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            var bmp = PreviewImage.Source as BitmapSource;
            if (bmp == null || ZoomHost == null) return;
            double vw = ZoomHost.ViewportWidth;
            double vh = ZoomHost.ViewportHeight;
            if (vw < 8 || vh < 8)
            {
                vw = ZoomHost.ActualWidth;
                vh = ZoomHost.ActualHeight;
            }
            if (vw < 8 || vh < 8) return;
            double sx = vw / bmp.PixelWidth;
            double sy = vh / bmp.PixelHeight;
            double fit = Math.Min(1, Math.Min(sx, sy));
            if (fit <= 0) fit = 1;
            SetZoom(fit);
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) SetZoom(_zoom * 1.15);
            else SetZoom(_zoom / 1.15);
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                SetZoom(_zoom * 1.25);
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                SetZoom(_zoom / 1.25);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ZoomFit_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
