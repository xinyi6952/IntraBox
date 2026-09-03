using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using IntraBox.Core;
using Microsoft.Win32;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using WpfRect = System.Windows.Rect;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>钉在桌面的置顶窗口：保留图标工具栏，可继续标注、保存、复制；取消钉住即关闭。</summary>
    public partial class PinImageWindow : Window
    {
        private readonly BitmapSource _baseImage;
        private readonly double _imgW;
        private readonly double _imgH;
        private AnnotationSession _anno;

        public PinImageWindow(BitmapSource image, double dipWidth, double dipHeight)
        {
            InitializeComponent();
            _baseImage = image;
            _imgW = Math.Max(40, dipWidth);
            _imgH = Math.Max(40, dipHeight);
            Img.Width = _imgW;
            Img.Height = _imgH;
            Img.Source = image;
            Layer.Width = _imgW;
            Layer.Height = _imgH;
            Bar.SetPinnedMode(true);
            _anno = new AnnotationSession(Layer, () => new WpfRect(0, 0, _imgW, _imgH), null);
            _anno.Tool = Bar.Tool;
            _anno.Color = Bar.StrokeColor;
            _anno.Thickness = Bar.StrokeWidth;

            Bar.ToolChanged += (s, e) => { _anno.Tool = Bar.Tool; };
            Bar.ColorChanged += (s, e) => { _anno.Color = Bar.StrokeColor; _anno.ApplyStyle(); };
            Bar.WidthChanged += (s, e) => { _anno.Thickness = Bar.StrokeWidth; _anno.ApplyStyle(); };
            Bar.UndoClicked += (s, e) => _anno.Undo();
            Bar.RedoClicked += (s, e) => _anno.Redo();
            Bar.CopyClicked += (s, e) => Copy(false);
            Bar.SaveClicked += (s, e) => Save();
            Bar.ConfirmClicked += (s, e) => Copy(true);
            Bar.CancelClicked += (s, e) => Close();
            Bar.UnpinClicked += (s, e) => Close();
            Bar.OcrClicked += (s, e) => RunOcr();
            Bar.GripDragStarted += (s, e) =>
            {
                try { DragMove(); }
                catch { }
            };
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Delete && _anno != null && _anno.Selected != null)
            {
                if (ConfirmHelper.DeleteOverTopmost(this, "删除当前选中的标注？"))
                    _anno.DeleteSelected();
            }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) _anno.Undo();
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) _anno.Redo();
        }

        private void Layer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _anno.OnMouseDown(e.GetPosition(Layer));
        }

        private void Layer_MouseMove(object sender, MouseEventArgs e)
        {
            _anno.OnMouseMove(e.GetPosition(Layer));
        }

        private void Layer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _anno.OnMouseUp(e.GetPosition(Layer));
        }

        private void RunOcr()
        {
            Bitmap bmp = BuildBitmap();
            if (bmp == null) return;
            RunOcrAsync(bmp);
        }

        // 识别放后台线程：Tesseract 单次识别耗时数秒，同步会冻结钉图窗口。
        // TesseractEngine 非线程安全，在后台线程内 using 新建，不跨线程复用。
        private async void RunOcrAsync(Bitmap bmp)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                string text = await Task.Run(() => OcrService.Recognize(bmp))
                    .ContinueWith(t => t.Result, scheduler);
                Mouse.OverrideCursor = null;
                var w = new OcrResultWindow(text);
                w.Owner = this;
                w.Show();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show("OCR 失败：" + ex.RootMessage(), "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                if (bmp != null) bmp.Dispose();
                GcHelper.CollectSafely();
            }
        }

        private void Copy(bool close)
        {
            var src = BuildResult();
            if (src == null) return;
            string err;
            if (!ClipboardHelper.TrySetImage(src, out err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (close) Close();
        }

        private void Save()
        {
            var bmp = BuildBitmap();
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
                    MessageBox.Show("保存失败：" + ex.RootMessage(), "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            bmp.Dispose();
        }

        private BitmapSource BuildResult()
        {
            using (var bmp = BuildBitmap())
            {
                if (bmp == null) return null;
                return ScreenCapture.ToBitmapSource(bmp);
            }
        }

        private Bitmap BuildBitmap()
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_baseImage));
            Bitmap bmp;
            using (var ms = new System.IO.MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                using (var tmp = new Bitmap(ms))
                    bmp = new Bitmap(tmp);
            }
            double sx = bmp.Width / _imgW;
            double sy = bmp.Height / _imgH;
            var crop = new Rectangle(0, 0, bmp.Width, bmp.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var el in _anno.Items)
                    CaptureOverlayWindow.RenderElement(g, el, crop, sx, sy, bmp);
            }
            return bmp;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (Img != null) Img.Source = null;
            base.OnClosed(e);
            GcHelper.CollectSafely();
        }
    }
}
