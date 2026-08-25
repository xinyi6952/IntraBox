using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.ImageConvert
{
    public partial class ImageConvertView : UserControl, IModuleView
    {
        private readonly List<string> _files = new List<string>();
        private string _outDir;

        public ImageConvertView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated() { }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有|*.*" };
            if (dlg.ShowDialog() != true) return;
            _files.Clear();
            _files.AddRange(dlg.FileNames);
            ListText.Text = "已选 " + _files.Count + " 张";
        }

        private void PickDir_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                _files.Clear();
                foreach (var f in Directory.GetFiles(dlg.SelectedPath))
                {
                    string e2 = Path.GetExtension(f).ToLowerInvariant();
                    if (e2 == ".png" || e2 == ".jpg" || e2 == ".jpeg" || e2 == ".bmp" || e2 == ".gif")
                        _files.Add(f);
                }
                ListText.Text = dlg.SelectedPath + "（" + _files.Count + " 张）";
            }
        }

        private void Out_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择输出目录";
                if (dlg.ShowDialog() == WinForms.DialogResult.OK) _outDir = dlg.SelectedPath;
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0) { SetMsg("请先选图", true); return; }
            if (string.IsNullOrEmpty(_outDir) || !Directory.Exists(_outDir))
            { SetMsg("请选择输出目录", true); return; }
            int scale;
            if (!int.TryParse((ScaleBox.Text ?? "").Trim(), out scale) || scale < 1) scale = 100;
            int tw, th;
            int.TryParse((WBox.Text ?? "").Trim(), out tw);
            int.TryParse((HBox.Text ?? "").Trim(), out th);
            long q;
            if (!long.TryParse((QBox.Text ?? "").Trim(), out q) || q < 1 || q > 100) q = 85;
            var item = FmtCombo.SelectedItem as ComboBoxItem;
            string fmt = item != null ? (item.Content as string) : "JPG";
            ImageFormat ifmt = ImageFormat.Jpeg;
            string ext = ".jpg";
            if (fmt == "PNG") { ifmt = ImageFormat.Png; ext = ".png"; }
            else if (fmt == "BMP") { ifmt = ImageFormat.Bmp; ext = ".bmp"; }
            else if (fmt == "GIF") { ifmt = ImageFormat.Gif; ext = ".gif"; }
            int ok = 0, skip = 0;
            foreach (var src in _files)
            {
                try
                {
                    string dest = Path.Combine(_outDir, Path.GetFileNameWithoutExtension(src) + ext);
                    if (File.Exists(dest))
                    {
                        var r = MessageBox.Show("已存在，覆盖？\n" + dest, "覆盖确认", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                        if (r == MessageBoxResult.Cancel) break;
                        if (r != MessageBoxResult.Yes) { skip++; continue; }
                    }
                    using (var bmp = new Bitmap(src))
                    {
                        int nw = tw > 0 ? tw : Math.Max(1, bmp.Width * scale / 100);
                        int nh = th > 0 ? th : Math.Max(1, bmp.Height * scale / 100);
                        using (var nb = new Bitmap(nw, nh))
                        using (var g = Graphics.FromImage(nb))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, nw, nh);
                            if (ifmt == ImageFormat.Jpeg) SaveJpeg(nb, dest, q);
                            else nb.Save(dest, ifmt);
                        }
                    }
                    ok++;
                }
                catch { skip++; }
            }
            SetMsg("完成：成功 " + ok + "，跳过 " + skip, skip > 0);
        }

        private static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (var c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }
            }
            if (codec == null) { bmp.Save(path, ImageFormat.Jpeg); return; }
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bmp.Save(path, codec, ep);
            }
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
