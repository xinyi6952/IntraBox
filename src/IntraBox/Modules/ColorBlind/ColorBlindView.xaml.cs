using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

        public ColorBlindView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated()
        {
            DisposeBmp(ref _src);
            DisposeBmp(ref _out);
            Preview.Source = null;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                DisposeBmp(ref _src);
                _src = new Bitmap(dlg.FileName);
                Apply_Click(null, null);
            }
            catch (Exception ex) { MsgText.Text = ex.RootMessage(); }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            int mode = ModeCombo.SelectedIndex;
            if (_src != null)
            {
                DisposeBmp(ref _out);
                _out = Transform(_src, mode);
                Preview.Source = ToSource(_out);
                MsgText.Text = "已模拟";
                return;
            }
            MediaColor c;
            if (!TryParseColor(ColorBox.Text, out c))
            {
                MsgText.Text = "颜色格式应为 #RRGGBB";
                return;
            }
            var t = Map(c.R, c.G, c.B, mode);
            ColorPreview.Fill = new SolidColorBrush(MediaColor.FromRgb(t[0], t[1], t[2]));
            MsgText.Text = string.Format("#{0:X2}{1:X2}{2:X2}", t[0], t[1], t[2]);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_out == null) { MsgText.Text = "没有可导出的图片"; return; }
            var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = "colorblind.png" };
            if (dlg.ShowDialog() != true) return;
            try { _out.Save(dlg.FileName, ImageFormat.Png); MsgText.Text = "已保存"; }
            catch (Exception ex) { MsgText.Text = ex.RootMessage(); }
        }

        private static Bitmap Transform(Bitmap src, int mode)
        {
            var dst = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src.GetPixel(x, y);
                    var t = Map(p.R, p.G, p.B, mode);
                    dst.SetPixel(x, y, System.Drawing.Color.FromArgb(p.A, t[0], t[1], t[2]));
                }
            }
            return dst;
        }

        private static byte[] Map(byte r, byte g, byte b, int mode)
        {
            double R = r / 255.0, G = g / 255.0, B = b / 255.0;
            double nR, nG, nB;
            if (mode == 0) { nR = 0.567 * R + 0.433 * G; nG = 0.558 * R + 0.442 * G; nB = 0.242 * G + 0.758 * B; }
            else if (mode == 1) { nR = 0.625 * R + 0.375 * G; nG = 0.700 * R + 0.300 * G; nB = 0.300 * G + 0.700 * B; }
            else if (mode == 2) { nR = 0.950 * R + 0.050 * G; nG = 0.433 * G + 0.567 * B; nB = 0.475 * G + 0.525 * B; }
            else { double y = 0.299 * R + 0.587 * G + 0.114 * B; nR = nG = nB = y; }
            return new byte[] { Clamp(nR), Clamp(nG), Clamp(nB) };
        }

        private static byte Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 255;
            return (byte)Math.Round(v * 255);
        }

        private static bool TryParseColor(string s, out MediaColor c)
        {
            c = default(MediaColor);
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return false;
            try
            {
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                c = MediaColor.FromRgb(r, g, b);
                return true;
            }
            catch { return false; }
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
