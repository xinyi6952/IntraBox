using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntraBox.Modules.Screenshot
{
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

        public string Tool { get; private set; }
        public Color StrokeColor { get; private set; }
        public double StrokeWidth { get; private set; }

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
