using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntraBox.Core;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;

namespace IntraBox.Modules.ColorBlind
{
    public partial class ColorBlindView : UserControl, IModuleView
    {
        private Bitmap _src;
        private Bitmap _out;
        private bool _ready;
        private bool _isDemo = true;
        private Border[] _pairOrig;
        private Border[] _pairSim;

        public ColorBlindView()
        {
            InitializeComponent();
            BuildPairs();
            Loaded += ColorBlindView_Loaded;
        }

        public void OnActivated()
        {
            if (_src != null) return;
            LoadDemo();
            if (_ready) ApplyAll();
        }

        public void OnDeactivated()
        {
            DisposeBmp(ref _src);
            DisposeBmp(ref _out);
            OrigImage.Source = null;
            SimImage.Source = null;
        }

        private void ColorBlindView_Loaded(object sender, RoutedEventArgs e)
        {
            _ready = true;
            if (_src == null) LoadDemo();
            ApplyAll();
        }

        private void BuildPairs()
        {
            _pairOrig = new Border[ColorBlindHelper.SampleHex.Length];
            _pairSim = new Border[ColorBlindHelper.SampleHex.Length];
            for (int i = 0; i < ColorBlindHelper.SampleHex.Length; i++)
            {
                var block = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 8) };
                var name = new TextBlock
                {
                    Text = ColorBlindHelper.SampleNames[i],
                    Width = 22,
                    VerticalAlignment = VerticalAlignment.Center
                };
                name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                block.Children.Add(name);
                var orig = new Border
                {
                    Width = 40,
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    ToolTip = "正常人看到的"
                };
                orig.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                var arrow = new TextBlock
                {
                    Text = "→",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                arrow.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var sim = new Border
                {
                    Width = 40,
                    Height = 24,
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    ToolTip = "这种色盲大约看成的"
                };
                sim.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                int index = i;
                orig.MouseLeftButtonDown += (s, ev) =>
                {
                    ColorBox.Text = ColorBlindHelper.SampleHex[index];
                };
                block.Children.Add(orig);
                block.Children.Add(arrow);
                block.Children.Add(sim);
                PairPanel.Children.Add(block);
                _pairOrig[i] = orig;
                _pairSim[i] = sim;
            }
        }

        private void LoadDemo()
        {
            DisposeBmp(ref _src);
            _src = ColorBlindHelper.CreateDemoBitmap();
            _isDemo = true;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有|*.*" };
            if (dlg.ShowDialog() != true) return;
            string sizeErr;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out sizeErr))
            {
                MsgText.Text = sizeErr;
                return;
            }
            try
            {
                DisposeBmp(ref _src);
                _src = new Bitmap(dlg.FileName);
                _isDemo = false;
                ApplyAll();
            }
            catch (Exception ex) { MsgText.Text = ex.RootMessage(); }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            LoadDemo();
            ApplyAll();
            MsgText.Text = "已回到示例图";
        }

        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            ApplyAll();
        }

        private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            ApplySwatches();
        }

        private void ColorBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyAll();
            e.Handled = true;
        }

        private void ApplyAll()
        {
            bool hexOk = ApplySwatches();
            if (_src == null) LoadDemo();
            int mode = ModeCombo.SelectedIndex;
            DisposeBmp(ref _out);
            _out = ColorBlindHelper.Transform(_src, mode);
            OrigImage.Source = ToSource(_src);
            SimImage.Source = ToSource(_out);
            OrigImageCaption.Text = "左边：视力正常的人看到的";
            SimImageCaption.Text = "右边：" + ColorBlindHelper.SimulatedCaption(mode);
            ImageKindText.Text = _isDemo ? "当前是内置示例图" : "当前是你打开的图";
            if (hexOk) MsgText.Text = "";
        }

        private bool ApplySwatches()
        {
            int mode = ModeCombo.SelectedIndex;
            RelationText.Text = ColorBlindHelper.RelationHint(mode);
            SimCaptionShort.Text = "色盲大约看成";
            for (int i = 0; i < ColorBlindHelper.SampleHex.Length; i++)
            {
                byte sr, sg, sb;
                ColorBlindHelper.TryParseHex(ColorBlindHelper.SampleHex[i], out sr, out sg, out sb);
                var t = ColorBlindHelper.Map(sr, sg, sb, mode);
                _pairOrig[i].Background = new SolidColorBrush(MediaColor.FromRgb(sr, sg, sb));
                _pairSim[i].Background = new SolidColorBrush(MediaColor.FromRgb(t[0], t[1], t[2]));
            }

            byte r, g, b;
            if (!ColorBlindHelper.TryParseHex(ColorBox.Text, out r, out g, out b))
            {
                MsgText.Text = "颜色格式应为 #RRGGBB";
                return false;
            }
            var mapped = ColorBlindHelper.Map(r, g, b, mode);
            OrigPreview.Background = new SolidColorBrush(MediaColor.FromRgb(r, g, b));
            SimPreview.Background = new SolidColorBrush(MediaColor.FromRgb(mapped[0], mapped[1], mapped[2]));
            OrigHex.Text = ColorBlindHelper.ToHex(r, g, b);
            SimHex.Text = ColorBlindHelper.ToHex(mapped[0], mapped[1], mapped[2]);
            if (MsgText.Text == "颜色格式应为 #RRGGBB") MsgText.Text = "";
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_out == null) { MsgText.Text = "没有可导出的图"; return; }
            var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = "colorblind.png" };
            if (dlg.ShowDialog() != true) return;
            try { _out.Save(dlg.FileName, ImageFormat.Png); MsgText.Text = "已保存右边这张模拟图"; }
            catch (Exception ex) { MsgText.Text = ex.RootMessage(); }
        }

        private static BitmapSource ToSource(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        private static void DisposeBmp(ref Bitmap b)
        {
            if (b == null) return;
            b.Dispose();
            b = null;
        }
    }
}
