using System;
using System.Drawing;
using System.Windows;
using WF = System.Windows.Forms;

namespace IntraBox.Core
{
    /// <summary>
    /// 系统托盘：单击打开主窗口；右键「显示主窗口 / 退出」。
    /// 点关闭只隐藏到托盘；从托盘退出才真正结束进程。
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private WF.NotifyIcon _notifyIcon;
        private System.Drawing.Image _showImg;
        private System.Drawing.Image _exitImg;

        public void Initialize()
        {
            _notifyIcon = new WF.NotifyIcon
            {
                Icon = ExtractAppIcon(),
                Text = AppVersion.ProductTitle,
                Visible = true
            };

            _showImg = IconToMenuImage(ExtractAppIcon());
            _exitImg = IconToMenuImage(SystemIcons.Error);
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("显示主窗口", _showImg, (s, e) => ShowMainWindow());
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出", _exitImg, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = menu;

            // 左键单击/双击都打开主窗口；右键仍弹出菜单
            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
            _notifyIcon.MouseUp += NotifyIcon_MouseUp;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        /// <summary>退出前确认（未保存等）。返回 false 则取消退出。</summary>
        public System.Func<bool> ConfirmExit { get; set; }

        private void NotifyIcon_MouseClick(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
                ShowMainWindow();
        }

        private void NotifyIcon_MouseUp(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
                ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            WindowRestore.ShowAndRestore(w);
        }

        private void ExitApplication()
        {
            ShowMainWindow();
            if (ConfirmExit != null && !ConfirmExit()) return;
            if (!ConfirmHelper.ExitApp()) return;
            ConfirmHelper.IsExiting = true;
            if (Application.Current != null) Application.Current.Shutdown();
        }

        private static Icon ExtractAppIcon()
        {
            try
            {
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var path = p.MainModule.FileName;
                    return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private static System.Drawing.Image IconToMenuImage(Icon icon)
        {
            if (icon == null) return null;
            try
            {
                using (var bmp = icon.ToBitmap())
                    return new Bitmap(bmp, 16, 16);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
                _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            if (_showImg != null) { _showImg.Dispose(); _showImg = null; }
            if (_exitImg != null) { _exitImg.Dispose(); _exitImg = null; }
        }
    }
}
