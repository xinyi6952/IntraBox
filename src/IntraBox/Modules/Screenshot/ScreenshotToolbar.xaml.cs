using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IntraBox.Modules.Screenshot
{
    public sealed class ToolbarDragDeltaEventArgs : EventArgs
    {
        public double Dx { get; private set; }
        public double Dy { get; private set; }

        public ToolbarDragDeltaEventArgs(double dx, double dy)
        {
            Dx = dx;
            Dy = dy;
        }
    }

    /// <summary>截屏图标工具栏：工具 / 颜色 / 线宽 / 输出。钉住窗口可切换为「取消钉住」。</summary>
    public partial class ScreenshotToolbar : UserControl
    {
        public event EventHandler ToolChanged;
        public event EventHandler ColorChanged;
        public event EventHandler WidthChanged;
        public event EventHandler UndoClicked;
        public event EventHandler RedoClicked;
        public event EventHandler CopyClicked;
        public event EventHandler SaveClicked;
        public event EventHandler ConfirmClicked;
        public event EventHandler CancelClicked;
        public event EventHandler PinClicked;
        public event EventHandler UnpinClicked;
        public event EventHandler OcrClicked;
        /// <summary>左侧拖动手柄按下。钉住窗口用来 DragMove 整窗。</summary>
        public event EventHandler GripDragStarted;
        /// <summary>拖动手柄移动。截屏覆盖层用来挪工具条位置。</summary>
        public event EventHandler<ToolbarDragDeltaEventArgs> GripDragDelta;

        public string Tool { get; private set; }
        public Color StrokeColor { get; private set; }
        public double StrokeWidth { get; private set; }

        private bool _gripDragging;
        private Point _gripLast;

        public ScreenshotToolbar()
        {
            Tool = "select";
            StrokeColor = (Color)ColorConverter.ConvertFromString("#E53935");
            StrokeWidth = 2;
            InitializeComponent();
            Loaded += (s, e) => HighlightTool();
        }

        public void SetPinnedMode(bool pinned)
        {
            BtnPin.Visibility = pinned ? Visibility.Collapsed : Visibility.Visible;
            BtnUnpin.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;
            if (GripDragStarted != null)
            {
                GripDragStarted(this, EventArgs.Empty);
                return;
            }
            _gripDragging = true;
            _gripLast = e.GetPosition(null);
            DragGrip.CaptureMouse();
        }

        private void Grip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_gripDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(null);
            double dx = p.X - _gripLast.X;
            double dy = p.Y - _gripLast.Y;
            if (dx == 0 && dy == 0) return;
            _gripLast = p;
            if (GripDragDelta != null)
                GripDragDelta(this, new ToolbarDragDeltaEventArgs(dx, dy));
        }

        private void Grip_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopGripDrag();
        }

        private void Grip_LostCapture(object sender, MouseEventArgs e)
        {
            _gripDragging = false;
        }

        private void StopGripDrag()
        {
            _gripDragging = false;
            if (DragGrip.IsMouseCaptured)
                DragGrip.ReleaseMouseCapture();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            Tool = btn.Tag.ToString();
            HighlightTool();
            if (ToolChanged != null) ToolChanged(this, EventArgs.Empty);
        }

        private void HighlightTool()
        {
            Highlight(BtnSelect, Tool == "select");
            Highlight(BtnRect, Tool == "rect");
            Highlight(BtnEllipse, Tool == "ellipse");
            Highlight(BtnArrow, Tool == "arrow");
            Highlight(BtnPen, Tool == "pen");
            Highlight(BtnText, Tool == "text");
            Highlight(BtnMosaic, Tool == "mosaic");
        }

        private static void Highlight(Button btn, bool on)
        {
            if (btn == null) return;
            btn.Background = on
                ? new SolidColorBrush(Color.FromRgb(0x4B, 0x6E, 0xAF))
                : (Brush)btn.FindResource("ButtonBgBrush");
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            StrokeColor = (Color)ColorConverter.ConvertFromString(btn.Tag.ToString());
            if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
        }

        private void Width_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (WidthCombo == null || WidthCombo.SelectedItem == null) return;
            var item = WidthCombo.SelectedItem as ComboBoxItem;
            if (item == null || item.Tag == null) return;
            double w;
            if (!double.TryParse(item.Tag.ToString(), out w)) return;
            StrokeWidth = w;
            if (WidthChanged != null) WidthChanged(this, EventArgs.Empty);
        }

        private void Ocr_Click(object sender, RoutedEventArgs e)
        {
            if (OcrClicked != null) OcrClicked(this, e);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (UndoClicked != null) UndoClicked(this, e);
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (RedoClicked != null) RedoClicked(this, e);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (CopyClicked != null) CopyClicked(this, e);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SaveClicked != null) SaveClicked(this, e);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmClicked != null) ConfirmClicked(this, e);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (CancelClicked != null) CancelClicked(this, e);
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            if (PinClicked != null) PinClicked(this, e);
        }

        private void Unpin_Click(object sender, RoutedEventArgs e)
        {
            if (UnpinClicked != null) UnpinClicked(this, e);
        }
    }
}
