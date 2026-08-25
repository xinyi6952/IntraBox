using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Core;
using DColor = System.Drawing.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;

namespace IntraBox.Modules.ColorPicker
{
    /// <summary>
    /// 颜色取色器：屏幕取色 + HEX/RGB/HSL 互转 + 复制。
    /// 取色模式：移动鼠标实时跟踪颜色，左键点击确定（全局鼠标钩子）。
    /// </summary>
    public partial class ColorPickerView : UserControl, IModuleView
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private readonly DispatcherTimer _timer;
        private LowLevelMouseProc _proc;   // 保持引用防止 GC
        private IntPtr _hook;
        private bool _picking;
        private bool _updating;

        public ColorPickerView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (s, e) => CaptureColor();

            UpdateFromColor(DColor.Black);
        }

        private void PickBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_picking) StartPicking();
            else StopPicking();
        }

        private void StartPicking()
        {
            _picking = true;
            PickBtn.Content = "取色中…（左键确定）";
            _timer.Start();
            _proc = MouseHookProc;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                _picking = false;
                PickBtn.Content = "屏幕取色";
                _timer.Stop();
                MessageBox.Show("无法安装鼠标钩子，请重试。", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StopPicking()
        {
            _picking = false;
            PickBtn.Content = "屏幕取色";
            _timer.Stop();
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            IntPtr hook = _hook;
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                Dispatcher.BeginInvoke((Action)StopPicking);
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private void CaptureColor()
        {
            POINT p;
            if (!GetCursorPos(out p)) return;
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(p.X, p.Y, 0, 0, new System.Drawing.Size(1, 1));
                    UpdateFromColor(bmp.GetPixel(0, 0));
                }
            }
            catch { /* 某些环境截屏失败，忽略 */ }
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            var hex = HexBox.Text.Trim().TrimStart('#');
            if (hex.Length != 6) return;
            try
            {
                var r = (byte)Convert.ToInt32(hex.Substring(0, 2), 16);
                var g = (byte)Convert.ToInt32(hex.Substring(2, 2), 16);
                var b = (byte)Convert.ToInt32(hex.Substring(4, 2), 16);
                UpdateFromColor(DColor.FromArgb(r, g, b));
            }
            catch { }
        }

        private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (!byte.TryParse(RBox.Text.Trim(), out byte r)) return;
            if (!byte.TryParse(GBox.Text.Trim(), out byte g)) return;
            if (!byte.TryParse(BBox.Text.Trim(), out byte b)) return;
            UpdateFromColor(DColor.FromArgb(r, g, b));
        }

        private void UpdateFromColor(DColor c)
        {
            _updating = true;
            HexBox.Text = c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            RBox.Text = c.R.ToString();
            GBox.Text = c.G.ToString();
            BBox.Text = c.B.ToString();

            var (h, s, l) = RgbToHsl(c.R, c.G, c.B);
            HBox.Text = Math.Round(h).ToString();
            SBox.Text = Math.Round(s * 100).ToString() + "%";
            LBox.Text = Math.Round(l * 100).ToString() + "%";

            ColorPreview.Background = new MediaBrush(MediaColor.FromRgb(c.R, c.G, c.B));
            _updating = false;
        }

        private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
        {
            double dr = r / 255.0, dg = g / 255.0, db = b / 255.0;
            double max = Math.Max(dr, Math.Max(dg, db));
            double min = Math.Min(dr, Math.Min(dg, db));
            double l = (max + min) / 2;
            double h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == dr) h = (dg - db) / d + (dg < db ? 6 : 0);
                else if (max == dg) h = (db - dr) / d + 2;
                else h = (dr - dg) / d + 4;
                h *= 60;
            }
            return (h, s, l);
        }

        private void CopyHex_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!ClipboardHelper.TrySetText("#" + HexBox.Text.Trim(), out err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void CopyRgb_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!ClipboardHelper.TrySetText("rgb(" + RBox.Text + ", " + GBox.Text + ", " + BBox.Text + ")", out err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("colorpicker", out state)) return;
            var hex = HistoryManager.GetString(state, "hex");
            if (!string.IsNullOrEmpty(hex)) HexBox.Text = hex;
        }

        public void OnDeactivated()
        {
            if (_picking) StopPicking();
            HistoryManager.Save("colorpicker", new Dictionary<string, object>
            {
                { "hex", HexBox.Text ?? "" }
            });
        }
    }
}
